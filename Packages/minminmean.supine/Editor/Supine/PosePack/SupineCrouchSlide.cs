using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using Supine.Utilities;

namespace Supine.PosePack
{
    /// <summary>
    /// しゃがみ中の移動を、歩きではなく滑りにする。
    ///
    /// 寝ポーズは移動の枝を持たないので、移動しても姿勢のまま滑っていく。
    /// しゃがみも同じにしたい場合に、しゃがみのモーションから移動の枝を落とし、
    /// 待機の姿勢だけを残す。
    ///
    /// しゃがみのモーションは入れ子になっている（CrouchPose の1Dツリーの子が2Dロコモーション）。
    /// 移動の軸で分けているツリー（2D、または速度で分ける1D）は中心の子に置き換え、
    /// それ以外の軸で分けているツリー（CrouchPose など）は子を辿って同じことをする。
    /// ポーズの切り替えは残したまま、どのポーズでも滑るようになる。
    /// </summary>
    internal static class SupineCrouchSlide
    {
        private const string CrouchingStateName = SupineNames.States.Crouching;

        /// <summary>
        /// 1Dツリーの軸のうち、移動で動くもの。VRChatの組込パラメータ。
        /// これで分けているツリーは移動のツリーとみなし、止まっているときの子だけを残す。
        /// </summary>
        private static readonly HashSet<string> MovementParameters = new HashSet<string>
            {
                "VelocityX", "VelocityY", "VelocityZ", "VelocityMagnitude", "AngularY",
            };

        /// <summary>
        /// しゃがみのモーションを、移動の枝を持たないものへ差し替える。
        /// しゃがみポーズの組込より後に呼ぶこと。組込が作った枝も滑りにするため。
        /// </summary>
        public static void Apply(AnimatorController controller, StateNameMap stateNames, List<string> warnings)
        {
            AnimatorState crouching =
                AnimatorStateUtility.FindState(controller, stateNames.Resolve(CrouchingStateName))?.State;
            if (crouching == null)
            {
                warnings.Add(
                    "Could not find the crouching state in the generated controller. " +
                    "Crouch sliding was not applied.");
                return;
            }

            if (crouching.motion == null) return;

            crouching.motion = ToSlide(crouching.motion, controller);
        }

        /// <summary>
        /// 移動の枝を落としたモーションを返す。変える所が無ければ引数をそのまま返す。
        ///
        /// 生成物のコントローラが抱えているツリーはその場で書き換え、
        /// それ以外（テンプレートや VRChat 標準のアセット）は写しを作ってコントローラに抱かせる。
        /// 共有のアセットを書き換えると、他の生成物や元のアニメーターまで滑るようになる。
        /// </summary>
        private static Motion ToSlide(Motion motion, AnimatorController controller)
        {
            BlendTree tree = motion as BlendTree;
            if (tree == null) return motion;

            ChildMotion[] children = tree.children;
            if (children.Length == 0) return motion;

            if (IsMovementTree(tree))
            {
                return ToSlide(children[FindRestingChild(tree, children)].motion, controller);
            }

            bool changed = false;
            for (int i = 0; i < children.Length; i++)
            {
                Motion slid = ToSlide(children[i].motion, controller);
                if (slid == children[i].motion) continue;

                children[i].motion = slid;
                changed = true;
            }

            if (!changed) return motion;

            if (AssetDatabase.GetAssetPath(tree) == AssetDatabase.GetAssetPath(controller))
            {
                tree.children = children;
                return tree;
            }

            BlendTree copy = new BlendTree
            {
                name = tree.name,
                blendType = tree.blendType,
                blendParameter = tree.blendParameter,
                blendParameterY = tree.blendParameterY,
                minThreshold = tree.minThreshold,
                maxThreshold = tree.maxThreshold,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            copy.children = children;

            AssetDatabase.AddObjectToAsset(copy, controller);
            return copy;
        }

        /// <summary>移動で子を選ぶツリーか</summary>
        private static bool IsMovementTree(BlendTree tree)
        {
            switch (tree.blendType)
            {
                case BlendTreeType.SimpleDirectional2D:
                case BlendTreeType.FreeformDirectional2D:
                case BlendTreeType.FreeformCartesian2D:
                    return true;
                case BlendTreeType.Simple1D:
                    return MovementParameters.Contains(tree.blendParameter);
                default:
                    return false;
            }
        }

        /// <summary>
        /// 止まっているときに選ばれる子。2Dなら原点、1Dなら閾値 0 に一番近いもの。
        /// 添字で決め打ちにすると、並びの違うアニメーターで別の子を拾ってしまう。
        /// </summary>
        private static int FindRestingChild(BlendTree tree, ChildMotion[] children)
        {
            bool is1D = tree.blendType == BlendTreeType.Simple1D;

            int resting = 0;
            float nearest = float.MaxValue;
            for (int i = 0; i < children.Length; i++)
            {
                float distance = is1D ? Mathf.Abs(children[i].threshold) : children[i].position.sqrMagnitude;
                if (distance >= nearest) continue;

                nearest = distance;
                resting = i;
            }
            return resting;
        }
    }
}
