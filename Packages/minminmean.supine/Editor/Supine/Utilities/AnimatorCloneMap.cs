using System.Collections.Generic;
using UnityEditor.Animations;

namespace Supine.Utilities
{
    /// <summary>
    /// 追加元（テンプレート）のノードと、追加先で対応するノードの対応表。
    ///
    /// 対応先には2種類ある。
    /// ・アンカー: 追加先に元から在って流用するノード（Standing など）
    /// ・クローン: テンプレートから複製して追加先に足したノード
    /// 遷移を張り直すときはどちらも同じ表で引きたいが、
    /// 「遷移を丸ごと作り直してよいのはクローンしたノードだけ」なので、両者は区別して持つ。
    /// </summary>
    internal sealed class AnimatorCloneMap
    {
        private readonly Dictionary<AnimatorState, AnimatorState> _states =
            new Dictionary<AnimatorState, AnimatorState>();
        private readonly Dictionary<AnimatorStateMachine, AnimatorStateMachine> _stateMachines =
            new Dictionary<AnimatorStateMachine, AnimatorStateMachine>();

        private readonly List<KeyValuePair<AnimatorState, AnimatorState>> _clonedStates =
            new List<KeyValuePair<AnimatorState, AnimatorState>>();
        private readonly List<KeyValuePair<AnimatorStateMachine, AnimatorStateMachine>> _clonedStateMachines =
            new List<KeyValuePair<AnimatorStateMachine, AnimatorStateMachine>>();
        private readonly List<KeyValuePair<AnimatorState, AnimatorState>> _anchorStates =
            new List<KeyValuePair<AnimatorState, AnimatorState>>();
        private readonly List<KeyValuePair<AnimatorStateMachine, AnimatorStateMachine>> _anchorStateMachines =
            new List<KeyValuePair<AnimatorStateMachine, AnimatorStateMachine>>();

        private readonly Dictionary<string, string> _renamedStates = new Dictionary<string, string>();

        /// <summary>クローンしたステート（テンプレート側 → 追加先側）</summary>
        public IReadOnlyList<KeyValuePair<AnimatorState, AnimatorState>> ClonedStates => _clonedStates;

        /// <summary>クローンしたステートマシン（テンプレート側 → 追加先側）</summary>
        public IReadOnlyList<KeyValuePair<AnimatorStateMachine, AnimatorStateMachine>> ClonedStateMachines
            => _clonedStateMachines;

        /// <summary>流用したステート（テンプレート側 → 追加先側）</summary>
        public IReadOnlyList<KeyValuePair<AnimatorState, AnimatorState>> AnchorStates => _anchorStates;

        /// <summary>流用したステートマシン（テンプレート側 → 追加先側）</summary>
        public IReadOnlyList<KeyValuePair<AnimatorStateMachine, AnimatorStateMachine>> AnchorStateMachines
            => _anchorStateMachines;

        /// <summary>Unityのユニーク化で名前が変わったクローン（テンプレート側の名前 → 追加先での実名）</summary>
        public IReadOnlyDictionary<string, string> RenamedStates => _renamedStates;

        public void RegisterClonedState(AnimatorState source, AnimatorState destination)
        {
            _states[source] = destination;
            _clonedStates.Add(new KeyValuePair<AnimatorState, AnimatorState>(source, destination));

            if (source.name != destination.name)
            {
                _renamedStates[source.name] = destination.name;
            }
        }

        public void RegisterClonedStateMachine(AnimatorStateMachine source, AnimatorStateMachine destination)
        {
            _stateMachines[source] = destination;
            _clonedStateMachines.Add(
                new KeyValuePair<AnimatorStateMachine, AnimatorStateMachine>(source, destination));
        }

        public void RegisterAnchorState(AnimatorState source, AnimatorState destination)
        {
            _states[source] = destination;
            _anchorStates.Add(new KeyValuePair<AnimatorState, AnimatorState>(source, destination));
        }

        public void RegisterAnchorStateMachine(AnimatorStateMachine source, AnimatorStateMachine destination)
        {
            _stateMachines[source] = destination;
            _anchorStateMachines.Add(
                new KeyValuePair<AnimatorStateMachine, AnimatorStateMachine>(source, destination));
        }

        public bool TryResolve(AnimatorState source, out AnimatorState destination)
        {
            if (source == null)
            {
                destination = null;
                return false;
            }
            return _states.TryGetValue(source, out destination);
        }

        public bool TryResolve(AnimatorStateMachine source, out AnimatorStateMachine destination)
        {
            if (source == null)
            {
                destination = null;
                return false;
            }
            return _stateMachines.TryGetValue(source, out destination);
        }
    }
}
