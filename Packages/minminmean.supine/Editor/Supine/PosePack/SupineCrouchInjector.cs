using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using Supine.Utilities;

namespace Supine.PosePack
{
    /// <summary>
    /// しゃがみポーズを組み込んだ結果。メニュー側はこれを見て項目と既定の印を作る。
    /// </summary>
    internal sealed class SupineCrouchInjection
    {
        /// <summary>ツリーへ実際に枝を足せたパックのしゃがみ</summary>
        public List<ResolvedCrouchPose> PackPoses;

        /// <summary>CrouchPose の既定値。ツリーの閾値から引いたもの</summary>
        public float DefaultValue;
    }

    /// <summary>
    /// しゃがみポーズの切り替えをコントローラへ組み込む。
    ///
    /// しゃがみは全ポーズの出入り口になっているハブで、入ってくる遷移だけで13本ある。
    /// ポーズごとにステートを作ると、その配線が丸ごとポーズ数ぶん増えてしまう。
    /// そこで**ステートは1つのまま**にして、モーションを入れ子のブレンドツリーへ差し替える。
    ///
    ///   根 (1D, CrouchPose)
    ///    ├ 0 → このアバターが元々持っていたモーション（＝Default）
    ///    └ 1..n → 2D ロコモーション（中心だけ自作アイドル、方向は標準を流用）
    ///
    /// VRChat自身も vrc_AvatarV3ActionLayer で同じ形（Upright で伏せ/しゃがみ/立ちを選ぶ
    /// 1Dツリーの子が2Dロコモーション）を使っているため、素性の知れた構造になっている。
    ///
    /// メニュー項目と CrouchPose の同期パラメータ登録は SupineMA Prefab が、
    /// コントローラ側の CrouchPose はテンプレートのコントローラが持っている。
    /// ここが面倒を見るのは、モーションの差し替えとコントローラ側の既定値だけ。
    /// Prefab 側は <see cref="SupineCrouchMenuBuilder"/> が受け持つ。
    /// </summary>
    internal static class SupineCrouchInjector
    {
        private const string CrouchPoseParameter = SupineNames.Parameters.CrouchPose;
        private const string CrouchingStateName = SupineNames.States.Crouching;

        /// <summary>
        /// しゃがみのモーションを差し替え、CrouchPose の既定値を反映する。
        /// 既存のしゃがみ切り替えを優先する場合は呼ばないこと。
        /// </summary>
        /// <returns>組み込めなかったら null。コントローラには何も残さない</returns>
        public static SupineCrouchInjection Inject(
            AnimatorController controller,
            IReadOnlyList<SupinePosePack> packs,
            StateNameMap stateNames,
            SupineCombineOptions options,
            List<string> warnings)
        {
            BlendTree template = LoadTemplateTree();
            if (template == null) return null;

            AnimatorState crouching =
                AnimatorStateUtility.FindState(controller, stateNames.Resolve(CrouchingStateName))?.State;
            if (crouching == null)
            {
                warnings.Add(
                    "Could not find the crouching state in the generated controller. " +
                    "Crouch poses were not applied.");
                return null;
            }

            // パックが足すしゃがみ。1つも無ければ従来どおりテンプレートの7種だけになる
            List<ResolvedCrouchPose> packPoses =
                SupinePosePackRegistry.ResolveCrouch(packs, template, warnings);

            BlendTree root = BuildRootTree(
                template, crouching.motion, controller, packPoses, warnings, out List<ResolvedCrouchPose> added);
            if (root == null) return null;

            crouching.motion = root;

            // 値はツリーの閾値から引く。数値を別に持つと、ツリーの並びを変えたときに
            // 黙ってずれて「起動時だけ違うポーズ」という気付きにくい不具合になる
            float defaultValue = ResolveDefaultValue(root, options, added);
            AnimatorParameterUtility.SetDefaultFloat(controller, CrouchPoseParameter, defaultValue);

            return new SupineCrouchInjection { PackPoses = added, DefaultValue = defaultValue };
        }

        private static BlendTree LoadTemplateTree()
        {
            string guid = JsonHelper.GetGuidList().crouch.pose_tree;
            if (string.IsNullOrEmpty(guid)) return null;

            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;

            return AssetDatabase.LoadAssetAtPath<BlendTree>(path);
        }

        /// <summary>
        /// テンプレートを元に、この生成物専用の根ツリーを作る。
        ///
        /// テンプレートのアセットをそのまま差すと、追加モードで
        /// アバター元々のロコモーションを捨てることになる。
        /// 毎回作り直して、先頭の子だけ「元のモーション」に差し替える。
        /// こうすると Default は常に「そのアバターが元々そうだった姿」を意味する。
        /// </summary>
        /// <param name="added">枝を足せたパックのしゃがみ。メニューにはこれだけを出す</param>
        private static BlendTree BuildRootTree(
            BlendTree template, Motion originalMotion, AnimatorController controller,
            IReadOnlyList<ResolvedCrouchPose> packPoses, List<string> warnings,
            out List<ResolvedCrouchPose> added)
        {
            added = new List<ResolvedCrouchPose>();

            if (template.children.Length == 0)
            {
                warnings.Add("The crouch pose blend tree has no children. Crouch poses were not applied.");
                return null;
            }

            BlendTree root = new BlendTree
            {
                name = template.name,
                blendType = template.blendType,
                blendParameter = template.blendParameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };

            ChildMotion[] children = template.children;

            // 先頭は「元の姿」。実際に置き換わっていたものが何であれ、それを尊重する。
            //
            // ただしテンプレートのしゃがみステートにはこのツリー自身が刺さっているため、
            // 素直に差し込むと Default の中身がツリー自身になり、入れ子が二重になる。
            // 同じ CrouchPose で駆動されるぶん偶然それらしく動いてしまい、気付きにくい。
            // その場合はテンプレートが持っている先頭の子（＝標準のしゃがみ）をそのまま使う。
            if (originalMotion != null && originalMotion != template)
            {
                children[0].motion = originalMotion;
            }

            // パックのぶんを末尾へ足す。既存の枝の閾値は動かさないので、
            // 前回保存されたメニューの選択はそのまま同じポーズを指し続ける
            List<ChildMotion> all = new List<ChildMotion>(children);
            foreach (ResolvedCrouchPose pose in packPoses)
            {
                BlendTree variant = BuildVariantTree(template, pose, controller, warnings);
                if (variant == null) continue;

                all.Add(new ChildMotion
                {
                    motion = variant,
                    threshold = pose.Value,
                    timeScale = 1f,
                    position = Vector2.zero,
                    directBlendParameter = string.Empty,
                });
                added.Add(pose);
            }

            root.children = all.ToArray();

            // 生成物のコントローラに抱かせる。テンプレート側のアセットは書き換えない
            AssetDatabase.AddObjectToAsset(root, controller);
            return root;
        }

        /// <summary>
        /// 1バリアントぶんの2Dロコモーションを作る。
        ///
        /// 方向の枝は既存のものをそのまま借りる。歩き・走りのクリップはミラーと
        /// 逆再生で使い回されていて、7種のポーズが共有で4本しか使っていない。
        /// 中心（位置 0,0）だけ差し替えれば、待機の姿勢がそのポーズのものになる。
        /// おかげでバリアント1つの追加コストはクリップ1本で済む。
        /// </summary>
        private static BlendTree BuildVariantTree(
            BlendTree template, ResolvedCrouchPose pose, AnimatorController controller, List<string> warnings)
        {
            BlendTree source = null;
            foreach (ChildMotion child in template.children)
            {
                source = child.motion as BlendTree;
                if (source != null) break;
            }

            if (source == null || source.children.Length == 0)
            {
                warnings.Add(
                    "The crouch pose blend tree has no locomotion branch to copy. Crouch pose '" +
                    pose.Entry.id + "' was skipped.");
                return null;
            }

            ChildMotion[] children = source.children;

            // 中心は添字で決め打ちにせず、位置で探す。並びが変わっても壊れない
            int centre = 0;
            float nearest = float.MaxValue;
            for (int i = 0; i < children.Length; i++)
            {
                float distance = children[i].position.sqrMagnitude;
                if (distance >= nearest) continue;

                nearest = distance;
                centre = i;
            }

            children[centre].motion = pose.Entry.clip;

            BlendTree variant = new BlendTree
            {
                name = pose.Entry.ResolveDisplayName(),
                blendType = source.blendType,
                blendParameter = source.blendParameter,
                blendParameterY = source.blendParameterY,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            variant.children = children;

            AssetDatabase.AddObjectToAsset(variant, controller);
            return variant;
        }

        private static float ResolveDefaultValue(
            BlendTree root, SupineCombineOptions options, IReadOnlyList<ResolvedCrouchPose> packPoses)
        {
            // パック側を選んでいたなら、そのポーズが今回も居るときだけ採用する。
            // パックを外したまま組み直したときに、存在しない枠を既定にしないため
            if (!string.IsNullOrEmpty(options.defaultCrouchPoseKey))
            {
                foreach (ResolvedCrouchPose pose in packPoses)
                {
                    if (pose.Key == options.defaultCrouchPoseKey) return pose.Value;
                }
            }

            // 組み込みのぶんは従来どおりツリーから引く
            int index = CrouchPoseTable.ChildIndex(options.defaultCrouchPose);
            if (index < 0 || index >= root.children.Length) index = 0;

            return root.children[index].threshold;
        }
    }
}
