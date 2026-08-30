using UnityEditor.Animations;

namespace Supine.Utilities
{
    /// <summary>
    /// AnimatorController の中身を扱うユーティリティ（UnityEngine.AnimatorUtility との衝突を避けた名前）
    /// </summary>
    internal static class AnimatorStateUtility
    {
        public static AnimatorState FindAnimatorStateByName(ChildAnimatorState[] states, string name)
        {
            foreach (ChildAnimatorState childState in states)
            {
                if (childState.state.name == name)
                {
                    return childState.state;
                }
            }
            return null;
        }
    }
}
