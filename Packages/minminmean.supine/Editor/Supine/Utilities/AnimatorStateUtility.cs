using System.Collections.Generic;
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

        /// <summary>
        /// ステートマシン以下の全ステートを名前で引ける表にする。
        /// サブステートマシンも再帰的に辿るため、Standingなどを入れ子にしたコントローラも拾える。
        /// 同名が複数ある場合は先に見つかったものを優先する。
        /// </summary>
        public static Dictionary<string, AnimatorState> BuildStateIndex(AnimatorStateMachine root)
        {
            Dictionary<string, AnimatorState> index = new Dictionary<string, AnimatorState>();
            CollectStates(root, index);
            return index;
        }

        /// <summary>
        /// ステートマシン以下の全サブステートマシンを名前で引ける表にする（ルート自身は含めない）。
        /// </summary>
        public static Dictionary<string, AnimatorStateMachine> BuildStateMachineIndex(AnimatorStateMachine root)
        {
            Dictionary<string, AnimatorStateMachine> index = new Dictionary<string, AnimatorStateMachine>();
            CollectStateMachines(root, index);
            return index;
        }

        /// <summary>
        /// 指定した条件を持つ発信遷移の遷移先を、遷移の並び順のまま集める。
        /// </summary>
        public static List<AnimatorState> CollectDestinationsByCondition(
            AnimatorState state, string parameter, AnimatorConditionMode mode)
        {
            List<AnimatorState> destinations = new List<AnimatorState>();
            if (state == null) return destinations;

            foreach (AnimatorStateTransition transition in state.transitions)
            {
                if (transition.destinationState == null) continue;

                foreach (AnimatorCondition condition in transition.conditions)
                {
                    if (condition.parameter != parameter || condition.mode != mode) continue;

                    destinations.Add(transition.destinationState);
                    break;
                }
            }
            return destinations;
        }

        /// <summary>
        /// ステートマシン以下の全ステートを、同名も含めてすべて集める。
        /// </summary>
        public static List<AnimatorState> CollectStates(AnimatorStateMachine root)
        {
            List<AnimatorState> states = new List<AnimatorState>();
            CollectStates(root, states);
            return states;
        }

        private static void CollectStates(AnimatorStateMachine stateMachine, List<AnimatorState> states)
        {
            if (stateMachine == null) return;

            foreach (ChildAnimatorState child in stateMachine.states)
            {
                if (child.state == null) continue;
                states.Add(child.state);
            }

            foreach (ChildAnimatorStateMachine child in stateMachine.stateMachines)
            {
                CollectStates(child.stateMachine, states);
            }
        }

        private static void CollectStates(AnimatorStateMachine stateMachine, Dictionary<string, AnimatorState> index)
        {
            if (stateMachine == null) return;

            foreach (ChildAnimatorState child in stateMachine.states)
            {
                if (child.state == null) continue;
                if (!index.ContainsKey(child.state.name))
                {
                    index.Add(child.state.name, child.state);
                }
            }

            foreach (ChildAnimatorStateMachine child in stateMachine.stateMachines)
            {
                CollectStates(child.stateMachine, index);
            }
        }

        private static void CollectStateMachines(
            AnimatorStateMachine stateMachine, Dictionary<string, AnimatorStateMachine> index)
        {
            if (stateMachine == null) return;

            foreach (ChildAnimatorStateMachine child in stateMachine.stateMachines)
            {
                if (child.stateMachine == null) continue;
                if (!index.ContainsKey(child.stateMachine.name))
                {
                    index.Add(child.stateMachine.name, child.stateMachine);
                }
                CollectStateMachines(child.stateMachine, index);
            }
        }
    }
}
