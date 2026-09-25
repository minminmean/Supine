using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using Supine.Utilities;
using ModularAvatarMenuItem = nadena.dev.modular_avatar.core.ModularAvatarMenuItem;
using ModularAvatarParameters = nadena.dev.modular_avatar.core.ModularAvatarParameters;
using ParameterConfig = nadena.dev.modular_avatar.core.ParameterConfig;

namespace Supine.PosePack
{
    /// <summary>
    /// しゃがみポーズの切り替えを組み込む。
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
    /// ここが面倒を見るのは、モーションの差し替えと既定値の反映、
    /// そして「既存を優先」したときの後片付けだけ。
    /// </summary>
    internal static class SupineCrouchInjector
    {
        private const string CrouchPoseParameter = SupineNames.Parameters.CrouchPose;
        private const string CrouchingStateName = SupineNames.States.Crouching;
        private const string CrouchMenuName = SupineNames.Menus.CrouchPoses;

        public static void Inject(
            AnimatorController controller,
            GameObject maPrefabInstance,
            IReadOnlyDictionary<string, string> stateNames,
            SupineCombineOptions options,
            List<string> warnings)
        {
            if (options.keepExistingCrouchPose)
            {
                // 既存の切り替えを活かすなら、こちらのメニューもパラメータも要らない。
                // 残すと「押しても何も起きない項目」が並び、同期枠も8bit無駄になる
                RemoveMenu(maPrefabInstance);
                RemoveParameter(maPrefabInstance);
                return;
            }

            BlendTree template = LoadTemplateTree();
            if (template == null) return;

            AnimatorState crouching = FindState(controller, ResolveName(CrouchingStateName, stateNames));
            if (crouching == null)
            {
                warnings.Add(
                    "Could not find the crouching state in the generated controller. " +
                    "Crouch poses were not applied.");
                return;
            }

            // パックが足すしゃがみ。1つも無ければ従来どおりテンプレートの7種だけになる
            List<ResolvedCrouchPose> packPoses =
                SupinePosePackRegistry.ResolveCrouch(template, warnings);

            BlendTree root = BuildRootTree(template, crouching.motion, controller, packPoses, warnings);
            if (root == null) return;

            crouching.motion = root;
            SupineCrouchMenuBuilder.Build(maPrefabInstance, packPoses, warnings);
            ApplyDefaultPose(controller, maPrefabInstance, root, options, packPoses);
        }

        /// <summary>
        /// 既定にしたいしゃがみを指す文字列。パック側のポーズを選んだときだけ使う。
        /// </summary>
        public static string MakeKey(SupinePosePack pack, SupineCrouchEntry entry)
        {
            return pack.ResolvePackId() + "/" + entry.id;
        }

        /// <summary>
        /// 既存のしゃがみモーションが入れ子のブレンドツリーかどうかを調べる。
        ///
        /// 入れ子になっているなら、別のポーズツールが同じ手法でしゃがみを
        /// 差し替えている可能性が高い。そのまま組み込むと相手の切り替えを乗っ取るため、
        /// 検出できたときだけ利用者に選ばせる。
        /// </summary>
        /// <returns>入れ子のツリー。該当しなければ null</returns>
        public static BlendTree FindExistingNestedTree(
            VRCAvatarDescriptor avatar, SupineCombineOptions options)
        {
            if (avatar == null) return null;

            AnimatorController source;
            string stateName;

            if (options.mode == SupineCombineMode.Add)
            {
                source = BaseAnimatorResolver.Resolve(avatar, options.EffectiveAddTargetOverride).controller;
                stateName = string.IsNullOrEmpty(options.entryStateName)
                    ? CrouchingStateName
                    : options.entryStateName;
            }
            else
            {
                // 継承しないなら元のモーションは生成物に持ち込まれないので、衝突しない
                if (!options.ShouldInherit) return null;
                if (!InheritedStateTable.TryResolveSourceStateName(
                        options, CrouchingStateName, out stateName)) return null;

                source = BaseAnimatorResolver.FindBaseLayerController(avatar);
            }

            if (source == null) return null;

            AnimatorState state = FindState(source, stateName);
            if (state == null) return null;

            BlendTree tree = state.motion as BlendTree;
            if (tree == null) return null;

            foreach (ChildMotion child in tree.children)
            {
                if (child.motion is BlendTree) return tree;
            }
            return null;
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
        private static BlendTree BuildRootTree(
            BlendTree template, Motion originalMotion, AnimatorController controller,
            IReadOnlyList<ResolvedCrouchPose> packPoses, List<string> warnings)
        {
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

        /// <summary>
        /// 選ばれたしゃがみポーズを既定値として反映する。
        ///
        /// 値はツリーの閾値から引く。数値を別に持つと、ツリーの並びを変えたときに
        /// 黙ってずれて「起動時だけ違うポーズ」という気付きにくい不具合になる。
        /// </summary>
        private static void ApplyDefaultPose(
            AnimatorController controller, GameObject maPrefabInstance, BlendTree root,
            SupineCombineOptions options, IReadOnlyList<ResolvedCrouchPose> packPoses)
        {
            float value = ResolveDefaultValue(root, options, packPoses);

            AnimatorParameterUtility.SetDefaultFloat(controller, CrouchPoseParameter, value);
            SetMenuParameterDefault(maPrefabInstance, value);
            MarkDefaultMenuItem(maPrefabInstance, value);
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
                    if (MakeKey(pose.Pack, pose.Entry) == options.defaultCrouchPoseKey) return pose.Value;
                }
            }

            // 組み込みのぶんは従来どおりツリーから引く
            int index = CrouchPoseTable.ChildIndex(options.defaultCrouchPose);
            if (index < 0 || index >= root.children.Length) index = 0;

            return root.children[index].threshold;
        }

        private static void SetMenuParameterDefault(GameObject maPrefabInstance, float value)
        {
            ModularAvatarParameters parameters = FindParameters(maPrefabInstance);
            if (parameters == null) return;

            for (int i = 0; i < parameters.parameters.Count; i++)
            {
                if (parameters.parameters[i].nameOrPrefix != CrouchPoseParameter) continue;

                ParameterConfig config = parameters.parameters[i];
                config.defaultValue = value;
                config.hasExplicitDefaultValue = true;
                parameters.parameters[i] = config;

                EditorUtility.SetDirty(parameters);
                return;
            }
        }

        /// <summary>
        /// メニュー上でも既定のポーズに印を移す。
        ///
        /// 並びの何番目かではなく、項目が書き込む値で合わせる。
        /// 項目が増えてページ送りが出ると、番号と枠番号は一致しなくなる。
        /// </summary>
        private static void MarkDefaultMenuItem(GameObject maPrefabInstance, float value)
        {
            Transform menu = MenuItemUtility.FindDescendant(maPrefabInstance.transform, CrouchMenuName);
            if (menu == null) return;

            foreach (ModularAvatarMenuItem item in menu.GetComponentsInChildren<ModularAvatarMenuItem>(true))
            {
                if (item.Control == null) continue;
                if (item.Control.parameter == null) continue;
                if (item.Control.parameter.name != CrouchPoseParameter) continue;

                item.isDefault = SupineCrouchValues.Approximately(item.Control.value, value);
                EditorUtility.SetDirty(item);
            }
        }

        private static void RemoveMenu(GameObject maPrefabInstance)
        {
            Transform menu = MenuItemUtility.FindDescendant(maPrefabInstance.transform, CrouchMenuName);
            if (menu == null) return;

            Undo.DestroyObjectImmediate(menu.gameObject);
        }

        private static void RemoveParameter(GameObject maPrefabInstance)
        {
            ModularAvatarParameters parameters = FindParameters(maPrefabInstance);
            if (parameters == null) return;

            for (int i = parameters.parameters.Count - 1; i >= 0; i--)
            {
                if (parameters.parameters[i].nameOrPrefix != CrouchPoseParameter) continue;

                Undo.RecordObject(parameters, "Remove Crouch Pose Parameter");
                parameters.parameters.RemoveAt(i);
                EditorUtility.SetDirty(parameters);
            }
        }

        private static ModularAvatarParameters FindParameters(GameObject maPrefabInstance)
        {
            return maPrefabInstance == null
                ? null
                : maPrefabInstance.GetComponentInChildren<ModularAvatarParameters>(true);
        }

        private static string ResolveName(string name, IReadOnlyDictionary<string, string> stateNames)
        {
            if (stateNames != null && stateNames.TryGetValue(name, out string resolved)) return resolved;
            return name;
        }

        private static AnimatorState FindState(AnimatorController controller, string name)
        {
            foreach (AnimatorControllerLayer layer in controller.layers)
            {
                if (layer.stateMachine == null) continue;

                foreach (AnimatorState state in AnimatorStateUtility.CollectStates(layer.stateMachine))
                {
                    if (state.name == name) return state;
                }
            }
            return null;
        }
    }
}
