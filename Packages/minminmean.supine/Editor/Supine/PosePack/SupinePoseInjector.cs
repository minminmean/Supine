using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Animations;
using Supine.Utilities;

namespace Supine.PosePack
{
    /// <summary>
    /// コピー済みのコントローラへ、ポーズパックのポーズを差し込む。
    ///
    /// テンプレートのコントローラは今まで通りそのまま使う。生成するのは「葉」にあたる
    /// ポーズのステートと、その出入りの遷移、そしてポーズごとに1行ずつ必要な振り分けだけ。
    /// Locomotion、AutoRotation、Foot Anchor、JumpAndFall といった仕掛けには一切触らない。
    ///
    /// 既存ポーズ（MJI / KJI など）を実測した結果、6つとも完全に同じ形をしていた。
    /// 違うのは 名前 / クリップ / VRCSupineの値 / Uprightの閾値 の4つだけなので、
    /// その4つを差し替えるテンプレートとして実装している。
    /// </summary>
    internal sealed class SupinePoseInjector
    {
        public const string PoseParameter = "VRCSupine";

        private const string AdjustParameter = "VRCSupineExAdjust";
        private const string UprightParameter = "Upright";
        private const string LockPoseParameter = "VRCLockPose";
        private const string PoseChangedParameter = "PoseChanged";
        private const string CurrentPoseParameter = "CurrentPose";

        private const string CrouchingStateName = "Crouching";
        private const string PoseChangeStateName = "Pose Change";
        private const string PrepareSupineStateName = "Prepare Supine";
        private const string PrepareAnimationStateName = "Prepare Animation";
        private const string PrepareTrackingStateName = "Prepare Tracking";
        private const string SetCurrentPoseStateName = "Set Current Pose";
        private const string ObservingStateName = "Observing";
        private const string PoseAdjustingStateName = "Pose Adjusting";

        /// <summary>
        /// 出口の閾値を入口より少しだけ高くするための差。
        /// 既存6ポーズがすべて入口+0.02を出口にしていたので、それに合わせる。
        /// 同値にすると境界で入口と出口が同時に成立し、行き来を繰り返す。
        /// </summary>
        private const float UprightHysteresis = 0.02f;

        private const float TransitionDuration = 0.25f;

        /// <summary>生成したステートを置く列を、既存のノードからどれだけ右へ離すか</summary>
        private const float ColumnGap = 300f;

        /// <summary>生成したステートの縦の間隔。テンプレートのポーズ列の間隔に合わせている</summary>
        private const float RowGap = 110f;

        private readonly AnimatorController _controller;
        private readonly IReadOnlyDictionary<string, string> _renamedStates;
        private readonly List<string> _warnings;

        public SupinePoseInjector(
            AnimatorController controller,
            IReadOnlyDictionary<string, string> renamedStates,
            List<string> warnings)
        {
            _controller = controller;
            _renamedStates = renamedStates;
            _warnings = warnings;
        }

        /// <summary>
        /// ポーズを差し込む。差し込めたポーズの一覧を返す（メニュー生成が同じ並びを使う）。
        /// </summary>
        public List<ResolvedPose> Inject(IReadOnlyList<ResolvedPose> poses)
        {
            List<ResolvedPose> injected = new List<ResolvedPose>();
            if (poses == null || poses.Count == 0) return injected;

            // 差し込み先をすべて先に解決する。
            // 途中で足りないと気付くと、半端に配線されたコントローラが残ってしまう
            StateLocation crouching = FindState(CrouchingStateName);
            if (crouching == null) return injected;

            StateLocation poseChange = FindState(PoseChangeStateName, crouching.LayerIndex);
            StateLocation prepareSupine = FindState(PrepareSupineStateName);
            if (poseChange == null || prepareSupine == null) return injected;

            StateLocation prepareAnimation = FindState(PrepareAnimationStateName, prepareSupine.LayerIndex);
            StateLocation prepareTracking = FindState(PrepareTrackingStateName, prepareSupine.LayerIndex);

            // Observing は3つのレイヤーに同名で存在する。名前だけでは引けないので、
            // 同じレイヤーにある固有のステートを手掛かりにして特定する
            StateLocation desktopObserving = FindState(ObservingStateName, prepareSupine.LayerIndex);

            StateLocation setCurrentPose = FindState(SetCurrentPoseStateName);
            if (prepareAnimation == null || prepareTracking == null ||
                desktopObserving == null || setCurrentPose == null)
            {
                return injected;
            }

            StateLocation observerObserving = FindState(ObservingStateName, setCurrentPose.LayerIndex);
            if (observerObserving == null) return injected;

            HashSet<string> existingNames = CollectStateNames();
            Vector3 origin = FindFreeColumn(crouching.Machine);

            for (int i = 0; i < poses.Count; i++)
            {
                ResolvedPose pose = poses[i];
                pose.StateName = MakeUniqueStateName(pose.Entry.id, existingNames);

                AnimatorState state = crouching.Machine.AddState(
                    pose.StateName, origin + new Vector3(0f, RowGap * i, 0f));

                ConfigurePoseState(state, pose);
                ConnectPoseState(state, pose, crouching, poseChange);

                AddDispatchEntry(prepareSupine.State, pose, prepareAnimation, prepareTracking, false);
                AddDispatchEntry(desktopObserving.State, pose, prepareAnimation, prepareTracking, true);
                AddCurrentPoseWatch(observerObserving.State, setCurrentPose.State, pose);

                injected.Add(pose);
            }

            // Pose Adjusting は「どのポーズでもない」受け皿なので、必ず最後に評価させる
            KeepTransitionLast(desktopObserving.State, PoseAdjustingStateName);

            return injected;
        }

        /// <summary>
        /// ポーズのステート本体。既存6ポーズの実測値に合わせている。
        /// </summary>
        private static void ConfigurePoseState(AnimatorState state, ResolvedPose pose)
        {
            state.motion = pose.Entry.clip;
            state.writeDefaultValues = false;
            state.iKOnFeet = false;
            state.speed = 1f;
            state.mirror = false;

            // クリップのキーの間を Ex Adjust でスクラブする。
            // これがあるおかげで、ポーズを何個増やしても同期パラメータは1ビットも増えない
            state.timeParameterActive = true;
            state.timeParameter = AdjustParameter;
        }

        /// <summary>
        /// ポーズの出入り4本。既存ポーズと同じ形にする。
        /// </summary>
        private void ConnectPoseState(
            AnimatorState state, ResolvedPose pose, StateLocation crouching, StateLocation poseChange)
        {
            float enter = pose.Entry.uprightThreshold;
            float exit = enter + UprightHysteresis;

            // しゃがみから入る
            AnimatorStateTransition fromCrouching = crouching.State.AddTransition(state);
            Configure(fromCrouching, TransitionDuration);
            fromCrouching.AddCondition(AnimatorConditionMode.Less, enter, UprightParameter);
            fromCrouching.AddCondition(AnimatorConditionMode.Equals, pose.Value, PoseParameter);
            fromCrouching.AddCondition(AnimatorConditionMode.IfNot, 0f, LockPoseParameter);

            // ポーズ切り替えから入る
            AnimatorStateTransition fromPoseChange = poseChange.State.AddTransition(state);
            Configure(fromPoseChange, TransitionDuration);
            fromPoseChange.AddCondition(AnimatorConditionMode.Equals, pose.Value, PoseParameter);

            // 起き上がって抜ける
            AnimatorStateTransition toCrouching = state.AddTransition(crouching.State);
            Configure(toCrouching, TransitionDuration);
            toCrouching.AddCondition(AnimatorConditionMode.Greater, exit, UprightParameter);
            toCrouching.AddCondition(AnimatorConditionMode.IfNot, 0f, LockPoseParameter);

            // 別のポーズへ移る
            AnimatorStateTransition toPoseChange = state.AddTransition(poseChange.State);
            Configure(toPoseChange, 0f);
            toPoseChange.AddCondition(AnimatorConditionMode.If, 0f, PoseChangedParameter);
        }

        /// <summary>
        /// トラッキングの振り分けを1行足す。
        ///
        /// この表を手で維持していたせいで、既存の DISK と KJI_ZNK が Prepare Supine に
        /// 載っておらず、トラッキングの切り替えが走っていなかった。
        /// ポーズ側の宣言から生成することで、足し忘れが起きないようにする。
        /// </summary>
        private void AddDispatchEntry(
            AnimatorState from, ResolvedPose pose,
            StateLocation prepareAnimation, StateLocation prepareTracking, bool requirePoseChanged)
        {
            AnimatorState destination =
                pose.Entry.headTracking == SupinePoseHeadTracking.Tracking
                    ? prepareTracking.State
                    : prepareAnimation.State;

            AnimatorStateTransition transition = from.AddTransition(destination);
            Configure(transition, 0f);
            transition.AddCondition(AnimatorConditionMode.Equals, pose.Value, PoseParameter);

            if (requirePoseChanged)
            {
                transition.AddCondition(AnimatorConditionMode.If, 0f, PoseChangedParameter);
            }
        }

        /// <summary>
        /// CurrentPose の同期を1行足す。
        /// Set Current Pose 側のドライバは VRCSupine をコピーする汎用実装なので、
        /// 「値が変わった」ことを検出する条件だけがポーズごとに必要になる。
        /// </summary>
        private void AddCurrentPoseWatch(AnimatorState observing, AnimatorState setCurrentPose, ResolvedPose pose)
        {
            AnimatorStateTransition transition = observing.AddTransition(setCurrentPose);
            Configure(transition, 0.05f);
            transition.AddCondition(AnimatorConditionMode.Equals, pose.Value, PoseParameter);
            transition.AddCondition(AnimatorConditionMode.NotEqual, pose.Value, CurrentPoseParameter);
        }

        private static void Configure(AnimatorStateTransition transition, float duration)
        {
            transition.hasExitTime = false;
            transition.exitTime = 0f;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            transition.offset = 0f;
            transition.canTransitionToSelf = false;
        }

        /// <summary>
        /// 指定した名前のステートへ向かう遷移を、評価順の最後へ回す。
        /// </summary>
        private static void KeepTransitionLast(AnimatorState state, string destinationStateName)
        {
            List<AnimatorStateTransition> head = new List<AnimatorStateTransition>();
            List<AnimatorStateTransition> tail = new List<AnimatorStateTransition>();

            foreach (AnimatorStateTransition transition in state.transitions)
            {
                bool isTail = transition.destinationState != null &&
                              transition.destinationState.name == destinationStateName;
                (isTail ? tail : head).Add(transition);
            }

            if (tail.Count == 0) return;

            head.AddRange(tail);
            state.transitions = head.ToArray();
        }

        private HashSet<string> CollectStateNames()
        {
            HashSet<string> names = new HashSet<string>();

            foreach (AnimatorControllerLayer layer in _controller.layers)
            {
                if (layer.stateMachine == null) continue;

                foreach (AnimatorState state in AnimatorStateUtility.CollectStates(layer.stateMachine))
                {
                    names.Add(state.name);
                }
            }

            return names;
        }

        private string MakeUniqueStateName(string id, HashSet<string> existingNames)
        {
            string name = id;
            int suffix = 1;

            while (!existingNames.Add(name))
            {
                suffix++;
                name = id + " " + suffix;
            }

            return name;
        }

        /// <summary>
        /// 既存のノードの右隣に、空いた列の先頭を返す。
        ///
        /// しゃがみからの相対位置で置くと、テンプレートのレイアウト次第で既存のポーズ列に
        /// ぴったり重なる。実際そうなった。ノード全体の右端を見て、その外側に置く。
        /// </summary>
        private static Vector3 FindFreeColumn(AnimatorStateMachine machine)
        {
            float maxX = 0f;
            float minY = 0f;
            bool any = false;

            foreach (ChildAnimatorState child in machine.states)
            {
                Extend(child.position, ref maxX, ref minY, ref any);
            }
            foreach (ChildAnimatorStateMachine child in machine.stateMachines)
            {
                Extend(child.position, ref maxX, ref minY, ref any);
            }

            // Entry / Exit / AnyState も画面上の場所を取るので、右端の計算に入れる
            Extend(machine.entryPosition, ref maxX, ref minY, ref any);
            Extend(machine.exitPosition, ref maxX, ref minY, ref any);
            Extend(machine.anyStatePosition, ref maxX, ref minY, ref any);

            if (!any) return Vector3.zero;

            return new Vector3(maxX + ColumnGap, minY, 0f);
        }

        private static void Extend(Vector3 position, ref float maxX, ref float minY, ref bool any)
        {
            if (!any)
            {
                maxX = position.x;
                minY = position.y;
                any = true;
                return;
            }

            if (position.x > maxX) maxX = position.x;
            if (position.y < minY) minY = position.y;
        }

        /// <summary>
        /// ステートを名前で探す。layerIndex を指定すると、そのレイヤーの中だけを見る。
        /// </summary>
        private StateLocation FindState(string name, int layerIndex = -1)
        {
            string resolved = ResolveStateName(name);

            AnimatorControllerLayer[] layers = _controller.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layerIndex >= 0 && i != layerIndex) continue;
                if (layers[i].stateMachine == null) continue;

                StateLocation found = FindState(layers[i].stateMachine, resolved, i);
                if (found != null) return found;
            }

            _warnings.Add(
                "Could not find the state '" + resolved +
                "' in the generated controller. Pose packs were not applied.");
            return null;
        }

        private static StateLocation FindState(AnimatorStateMachine machine, string name, int layerIndex)
        {
            foreach (ChildAnimatorState child in machine.states)
            {
                if (child.state != null && child.state.name == name)
                {
                    return new StateLocation { Machine = machine, State = child.state, LayerIndex = layerIndex };
                }
            }

            foreach (ChildAnimatorStateMachine child in machine.stateMachines)
            {
                if (child.stateMachine == null) continue;

                StateLocation found = FindState(child.stateMachine, name, layerIndex);
                if (found != null) return found;
            }

            return null;
        }

        /// <summary>
        /// 追加モードで名前が衝突してリネームされている場合、生成物での実名を返す。
        /// </summary>
        private string ResolveStateName(string name)
        {
            if (_renamedStates != null && _renamedStates.TryGetValue(name, out string renamed)) return renamed;
            return name;
        }

        private sealed class StateLocation
        {
            public AnimatorStateMachine Machine;
            public AnimatorState State;
            public int LayerIndex;
        }
    }
}
