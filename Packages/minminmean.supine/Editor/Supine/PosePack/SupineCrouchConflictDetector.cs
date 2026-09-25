using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using Supine.Utilities;

namespace Supine.PosePack
{
    /// <summary>
    /// 別のツールが入れたしゃがみポーズの切り替えを見つける。
    /// 組込ウィンドウが「どちらを活かすか」を選ばせるかどうかの判断に使う。
    /// </summary>
    internal static class SupineCrouchConflictDetector
    {
        private const string CrouchingStateName = SupineNames.States.Crouching;

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

            AnimatorState state = AnimatorStateUtility.FindState(source, stateName)?.State;
            if (state == null) return null;

            BlendTree tree = state.motion as BlendTree;
            if (tree == null) return null;

            foreach (ChildMotion child in tree.children)
            {
                if (child.motion is BlendTree) return tree;
            }
            return null;
        }
    }
}
