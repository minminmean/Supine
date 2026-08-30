using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Supine.Utilities
{
    /// <summary>
    /// AnimatorControllerのノードを別のコントローラへ複製する。
    ///
    /// Unityのクローンには「参照フィールドが複製元を指したまま残る」という罠があるため、
    /// EditorUtility.CopySerializedの直後に参照フィールドを必ず潰す方針で統一する。
    /// また遷移は「遷移先を渡して生成する」形しか無いので、
    /// 「先に全ノードを作る → あとで遷移を張る」の2パス構成を前提にしている。
    ///
    /// 複製先・テンプレート・対応表は1回の複製を通して変わらないので、
    /// 静的メソッドに毎回引き回さずインスタンスに持たせる。
    /// </summary>
    internal sealed class AnimatorCloner
    {
        private readonly AnimatorController _destinationController;
        private readonly string _templateAssetPath;
        private readonly AnimatorCloneMap _map;
        private readonly List<string> _warnings;

        /// <param name="destinationController">複製先のコントローラ</param>
        /// <param name="templateAssetPath">複製元テンプレートのアセットパス。サブアセット判定に使う</param>
        /// <param name="map">複製元と複製先の対応表</param>
        /// <param name="warnings">解決できなかった遷移の警告を積む先</param>
        public AnimatorCloner(
            AnimatorController destinationController,
            string templateAssetPath,
            AnimatorCloneMap map,
            List<string> warnings)
        {
            _destinationController = destinationController;
            _templateAssetPath     = templateAssetPath;
            _map                   = map;
            _warnings              = warnings;
        }

        /// <summary>
        /// 生成したオブジェクトをコントローラのサブアセットとして登録する。
        /// Add系APIが自動登録するかどうかはUnityのバージョンで揺れるため、防御的に通す。
        /// </summary>
        public void Register(Object obj)
        {
            if (obj == null || _destinationController == null) return;
            if (AssetDatabase.Contains(obj)) return;
            if (!AssetDatabase.Contains(_destinationController)) return;

            obj.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(obj, _destinationController);
        }

        // ------------------------------------------------------------
        // ノードの複製
        // ------------------------------------------------------------

        /// <summary>
        /// ステートを1つ複製する。遷移は張らない。
        /// </summary>
        /// <returns>複製されたステート。名前はUnityによってユニーク化されている場合がある</returns>
        public AnimatorState CloneState(
            AnimatorState source, AnimatorStateMachine destinationParent, Vector3 position)
        {
            AnimatorState clone = destinationParent.AddState(source.name, position);

            // AddStateがユニーク化した名前を、CopySerializedで上書きされる前に控える
            string assignedName = clone.name;

            EditorUtility.CopySerialized(source, clone);

            clone.name = assignedName;
            // CopySerializedはテンプレート側の遷移とbehaviourを参照したまま写すので必ず潰す
            clone.transitions = new AnimatorStateTransition[0];
            clone.behaviours  = new StateMachineBehaviour[0];
            clone.motion      = ResolveMotion(source.motion);

            CloneBehaviours(
                source.behaviours,
                clone.AddStateMachineBehaviour,
                () => clone.behaviours = new StateMachineBehaviour[0]);
            Register(clone);

            return clone;
        }

        /// <summary>
        /// ステートマシンを子として複製し、中身を再帰的に埋める。遷移は張らない。
        /// 生成した全ノードをmapへ登録する。
        /// </summary>
        public AnimatorStateMachine CloneStateMachine(
            AnimatorStateMachine source, AnimatorStateMachine destinationParent, Vector3 position)
        {
            AnimatorStateMachine clone = destinationParent.AddStateMachine(source.name, position);
            AdoptStateMachine(source, clone);
            return clone;
        }

        /// <summary>
        /// レイヤーを丸ごと複製して末尾に追加する。
        /// </summary>
        public void CloneLayer(AnimatorControllerLayer source)
        {
            _destinationController.AddLayer(_destinationController.MakeUniqueLayerName(source.name));

            AnimatorControllerLayer[] layers = _destinationController.layers;
            int index = layers.Length - 1;
            AnimatorControllerLayer added = layers[index];

            // defaultWeightはテンプレートの値をそのまま尊重する。
            // ごろ寝の補助レイヤーはmotionを持たずbehaviourだけで動くため、0のままで正しい。
            added.defaultWeight            = source.defaultWeight;
            added.blendingMode             = source.blendingMode;
            added.avatarMask               = source.avatarMask;
            added.iKPass                   = source.iKPass;
            added.syncedLayerIndex         = source.syncedLayerIndex;
            added.syncedLayerAffectsTiming = source.syncedLayerAffectsTiming;

            layers[index] = added;
            _destinationController.layers = layers;

            AdoptStateMachine(source.stateMachine, added.stateMachine);
        }

        /// <summary>
        /// 生成済みのステートマシンを複製先として引き取り、登録して中身を埋める。
        /// </summary>
        private void AdoptStateMachine(AnimatorStateMachine source, AnimatorStateMachine destination)
        {
            Register(destination);
            _map.RegisterClonedStateMachine(source, destination);
            FillStateMachine(source, destination);
        }

        /// <summary>
        /// 既にあるステートマシンへ、複製元の中身（座標・behaviour・子ノード）を詰める。
        /// レイヤー追加で自動生成されたルートステートマシンに使う。
        /// </summary>
        public void FillStateMachine(AnimatorStateMachine source, AnimatorStateMachine destination)
        {
            // 本体は参照フィールドが多いのでCopySerializedは使わず、座標だけ写す
            destination.anyStatePosition           = source.anyStatePosition;
            destination.entryPosition              = source.entryPosition;
            destination.exitPosition               = source.exitPosition;
            destination.parentStateMachinePosition = source.parentStateMachinePosition;

            CloneBehaviours(
                source.behaviours,
                destination.AddStateMachineBehaviour,
                () => destination.behaviours = new StateMachineBehaviour[0]);

            foreach (ChildAnimatorState child in source.states)
            {
                AnimatorState clonedState = CloneState(child.state, destination, child.position);
                _map.RegisterClonedState(child.state, clonedState);
            }

            foreach (ChildAnimatorStateMachine child in source.stateMachines)
            {
                CloneStateMachine(child.stateMachine, destination, child.position);
            }
        }

        /// <summary>
        /// StateMachineBehaviourを複製して付け直す。
        /// AnimatorStateとAnimatorStateMachineに共通の型が無いので、
        /// 差分になる2つの操作だけを受け取る。
        /// </summary>
        private void CloneBehaviours(
            StateMachineBehaviour[] sources,
            Func<Type, StateMachineBehaviour> addBehaviour,
            Action clearBehaviours)
        {
            clearBehaviours();

            foreach (StateMachineBehaviour source in sources)
            {
                if (source == null) continue;

                StateMachineBehaviour clone = addBehaviour(source.GetType());
                EditorUtility.CopySerialized(source, clone);
                Register(clone);
            }
        }

        // ------------------------------------------------------------
        // 遷移の複製
        // ------------------------------------------------------------

        /// <summary>
        /// mapに登録済みの「クローンしたノード」について、複製元の遷移をすべて張り直す。
        /// 遷移先はmapで解決する。解決できない遷移は捨てて警告を積む。
        /// </summary>
        /// <param name="isIntentionallyUnresolved">
        /// 解決できないのが想定どおりの遷移を判定する。trueなら警告を出さずに捨てる
        /// </param>
        public void CloneTransitions(Func<AnimatorStateTransition, bool> isIntentionallyUnresolved = null)
        {
            foreach (KeyValuePair<AnimatorState, AnimatorState> pair in _map.ClonedStates)
            {
                foreach (AnimatorStateTransition transition in pair.Key.transitions)
                {
                    CloneStateTransition(transition, pair.Value, isIntentionallyUnresolved);
                }
            }

            foreach (KeyValuePair<AnimatorStateMachine, AnimatorStateMachine> pair in _map.ClonedStateMachines)
            {
                CloneStateMachineTransitions(pair.Key, pair.Value, isIntentionallyUnresolved);
            }
        }

        /// <summary>
        /// ステートの発信遷移を1本、指定したステートへ複製する。
        /// </summary>
        /// <returns>複製された遷移。遷移先を解決できなければnull</returns>
        public AnimatorStateTransition CloneStateTransition(
            AnimatorStateTransition source,
            AnimatorState destinationOwner,
            Func<AnimatorStateTransition, bool> isIntentionallyUnresolved = null)
        {
            return CloneTransition<AnimatorStateTransition>(
                source, destinationOwner.name, isIntentionallyUnresolved,
                destinationOwner.AddExitTransition,
                destinationOwner.AddTransition,
                destinationOwner.AddTransition);
        }

        /// <summary>
        /// AnyState遷移を1本、指定したステートマシンへ複製する。
        /// </summary>
        /// <returns>複製された遷移。遷移先を解決できなければnull</returns>
        public AnimatorStateTransition CloneAnyStateTransition(
            AnimatorStateTransition source,
            AnimatorStateMachine destinationOwner,
            Func<AnimatorStateTransition, bool> isIntentionallyUnresolved = null)
        {
            return CloneTransition<AnimatorStateTransition>(
                source, destinationOwner.name + " (AnyState)", isIntentionallyUnresolved,
                null,
                destinationOwner.AddAnyStateTransition,
                destinationOwner.AddAnyStateTransition);
        }

        /// <summary>
        /// AnyState / Entry / StateMachine遷移と既定ステートを複製する。
        /// </summary>
        private void CloneStateMachineTransitions(
            AnimatorStateMachine source,
            AnimatorStateMachine destination,
            Func<AnimatorStateTransition, bool> isIntentionallyUnresolved)
        {
            foreach (AnimatorStateTransition transition in source.anyStateTransitions)
            {
                CloneAnyStateTransition(transition, destination, isIntentionallyUnresolved);
            }

            foreach (AnimatorTransition transition in source.entryTransitions)
            {
                CloneTransition<AnimatorTransition>(
                    transition, destination.name + " (Entry)", null,
                    null,
                    destination.AddEntryTransition,
                    destination.AddEntryTransition);
            }

            // サブステートマシン発の遷移は、親のステートマシンが子ごとに保持している
            foreach (ChildAnimatorStateMachine child in source.stateMachines)
            {
                if (child.stateMachine == null) continue;

                if (!_map.TryResolve(child.stateMachine, out AnimatorStateMachine transitionSource))
                {
                    Warn(destination.name, child.stateMachine.name);
                    continue;
                }

                foreach (AnimatorTransition transition in source.GetStateMachineTransitions(child.stateMachine))
                {
                    CloneTransition<AnimatorTransition>(
                        transition, transitionSource.name, null,
                        () => destination.AddStateMachineExitTransition(transitionSource),
                        d => destination.AddStateMachineTransition(transitionSource, d),
                        d => destination.AddStateMachineTransition(transitionSource, d));
                }
            }

            if (source.defaultState != null && _map.TryResolve(source.defaultState, out AnimatorState defaultState))
            {
                destination.defaultState = defaultState;
            }
        }

        /// <summary>
        /// 遷移の複製。遷移の種類によって違うのは「どのAdd APIを呼ぶか」だけなので、
        /// 遷移先の解決と警告はここに集約する。
        /// </summary>
        /// <param name="ownerLabel">警告に出す発信元の名前</param>
        /// <param name="addExit">Exitへの遷移を作る。この種類がExitを扱えないならnull</param>
        /// <param name="addToStateMachine">ステートマシンへの遷移を作る</param>
        /// <param name="addToState">ステートへの遷移を作る</param>
        /// <returns>複製された遷移。遷移先を解決できなければnull</returns>
        private T CloneTransition<T>(
            AnimatorTransitionBase source,
            string ownerLabel,
            Func<AnimatorStateTransition, bool> isIntentionallyUnresolved,
            Func<T> addExit,
            Func<AnimatorStateMachine, T> addToStateMachine,
            Func<AnimatorState, T> addToState)
            where T : AnimatorTransitionBase
        {
            T clone;

            if (source.isExit && addExit != null)
            {
                clone = addExit();
            }
            else if (source.destinationStateMachine != null)
            {
                if (!_map.TryResolve(source.destinationStateMachine, out AnimatorStateMachine destination))
                {
                    return WarnUnresolved<T>(
                        ownerLabel, source.destinationStateMachine.name, source, isIntentionallyUnresolved);
                }
                clone = addToStateMachine(destination);
            }
            else if (source.destinationState != null)
            {
                if (!_map.TryResolve(source.destinationState, out AnimatorState destination))
                {
                    return WarnUnresolved<T>(
                        ownerLabel, source.destinationState.name, source, isIntentionallyUnresolved);
                }
                clone = addToState(destination);
            }
            else
            {
                return WarnUnresolved<T>(ownerLabel, "(none)", source, isIntentionallyUnresolved);
            }

            CopyTransition(source, clone);
            return clone;
        }

        /// <summary>
        /// 遷移のパラメータを写す。生成時に決めた遷移先は必ず復元する。
        /// AnimatorStateTransitionとAnimatorTransitionは、
        /// 遷移先とExitフラグをどちらも基底クラスに持つのでまとめて扱える。
        /// </summary>
        private static void CopyTransition(AnimatorTransitionBase source, AnimatorTransitionBase destination)
        {
            AnimatorState destinationState = destination.destinationState;
            AnimatorStateMachine destinationStateMachine = destination.destinationStateMachine;
            bool isExit = destination.isExit;

            EditorUtility.CopySerialized(source, destination);

            destination.destinationState        = destinationState;
            destination.destinationStateMachine = destinationStateMachine;
            destination.isExit                  = isExit;
        }

        // ------------------------------------------------------------
        // motionの解決
        // ------------------------------------------------------------

        /// <summary>
        /// motionを解決する。
        /// テンプレート外のアセットはそのまま参照し、テンプレート内のサブアセットだけ複製する。
        /// 参照のまま残すと、生成物がテンプレート（パッケージ内アセット）に依存してしまう。
        /// </summary>
        public Motion ResolveMotion(Motion source)
        {
            if (source == null) return null;
            if (AssetDatabase.GetAssetPath(source) != _templateAssetPath) return source;

            if (source is BlendTree blendTree)
            {
                return CloneBlendTree(blendTree);
            }

            Motion clone = Object.Instantiate(source);
            clone.name = source.name;
            Register(clone);
            return clone;
        }

        private BlendTree CloneBlendTree(BlendTree source)
        {
            BlendTree clone = new BlendTree();
            Register(clone);

            EditorUtility.CopySerialized(source, clone);
            clone.name = source.name;

            // 子のmotionは複製元を指したままなので、1段ずつ解決し直す
            ChildMotion[] children = clone.children;
            for (int i = 0; i < children.Length; i++)
            {
                children[i].motion = ResolveMotion(children[i].motion);
            }
            clone.children = children;

            return clone;
        }

        // ------------------------------------------------------------
        // 警告
        // ------------------------------------------------------------

        /// <summary>
        /// 遷移を捨てたことを警告に積み、常にnullを返す。
        /// 呼び出し側をreturn1行で畳むためにnullを返す形にしている。
        /// </summary>
        private T WarnUnresolved<T>(
            string ownerLabel,
            string destinationName,
            AnimatorTransitionBase source,
            Func<AnimatorStateTransition, bool> isIntentionallyUnresolved)
            where T : AnimatorTransitionBase
        {
            // 繋がないことが分かっている遷移まで警告にすると、意図した設定が異常に見えてしまう
            if (isIntentionallyUnresolved != null &&
                source is AnimatorStateTransition stateTransition &&
                isIntentionallyUnresolved(stateTransition)) return null;

            Warn(ownerLabel, destinationName);
            return null;
        }

        private void Warn(string ownerLabel, string destinationName)
        {
            _warnings.Add(
                "Dropped a transition from '" + ownerLabel + "' to '" + destinationName +
                "' because the destination could not be resolved in the target animator.");
        }
    }
}
