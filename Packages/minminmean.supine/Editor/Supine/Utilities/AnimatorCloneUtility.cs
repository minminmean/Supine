using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Supine.Utilities
{
    /// <summary>
    /// AnimatorControllerのノードを別のコントローラへ複製するユーティリティ。
    ///
    /// Unityのクローンには「参照フィールドが複製元を指したまま残る」という罠があるため、
    /// EditorUtility.CopySerializedの直後に参照フィールドを必ず潰す方針で統一する。
    /// また遷移は「遷移先を渡して生成する」形しか無いので、
    /// 「先に全ノードを作る → あとで遷移を張る」の2パス構成を前提にしている。
    /// </summary>
    internal static class AnimatorCloneUtility
    {
        /// <summary>
        /// 生成したオブジェクトをコントローラのサブアセットとして登録する。
        /// Add系APIが自動登録するかどうかはUnityのバージョンで揺れるため、防御的に通す。
        /// </summary>
        public static void Register(Object obj, AnimatorController destinationController)
        {
            if (obj == null || destinationController == null) return;
            if (AssetDatabase.Contains(obj)) return;
            if (!AssetDatabase.Contains(destinationController)) return;

            obj.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(obj, destinationController);
        }

        /// <summary>
        /// ステートを1つ複製する。遷移は張らない。
        /// </summary>
        /// <returns>複製されたステート。名前はUnityによってユニーク化されている場合がある</returns>
        public static AnimatorState CloneState(
            AnimatorState source,
            AnimatorStateMachine destinationParent,
            AnimatorController destinationController,
            string templateAssetPath,
            Vector3 position)
        {
            AnimatorState clone = destinationParent.AddState(source.name, position);

            // AddStateがユニーク化した名前を、CopySerializedで上書きされる前に控える
            string assignedName = clone.name;

            EditorUtility.CopySerialized(source, clone);

            clone.name = assignedName;
            // CopySerializedはテンプレート側の遷移とbehaviourを参照したまま写すので必ず潰す
            clone.transitions = new AnimatorStateTransition[0];
            clone.behaviours  = new StateMachineBehaviour[0];
            clone.motion      = ResolveMotion(source.motion, templateAssetPath, destinationController);

            CloneBehaviours(source.behaviours, clone, destinationController);
            Register(clone, destinationController);

            return clone;
        }

        /// <summary>
        /// ステートマシンを子として複製し、中身を再帰的に埋める。遷移は張らない。
        /// 生成した全ノードをmapへ登録する。
        /// </summary>
        public static AnimatorStateMachine CloneStateMachine(
            AnimatorStateMachine source,
            AnimatorStateMachine destinationParent,
            AnimatorController destinationController,
            string templateAssetPath,
            Vector3 position,
            AnimatorCloneMap map)
        {
            AnimatorStateMachine clone = destinationParent.AddStateMachine(source.name, position);
            Register(clone, destinationController);
            map.RegisterClonedStateMachine(source, clone);

            FillStateMachine(source, clone, destinationController, templateAssetPath, map);
            return clone;
        }

        /// <summary>
        /// 既にあるステートマシンへ、複製元の中身（座標・behaviour・子ノード）を詰める。
        /// レイヤー追加で自動生成されたルートステートマシンに使う。
        /// </summary>
        public static void FillStateMachine(
            AnimatorStateMachine source,
            AnimatorStateMachine destination,
            AnimatorController destinationController,
            string templateAssetPath,
            AnimatorCloneMap map)
        {
            // 本体は参照フィールドが多いのでCopySerializedは使わず、座標だけ写す
            destination.anyStatePosition           = source.anyStatePosition;
            destination.entryPosition              = source.entryPosition;
            destination.exitPosition               = source.exitPosition;
            destination.parentStateMachinePosition = source.parentStateMachinePosition;

            CloneBehaviours(source.behaviours, destination, destinationController);

            foreach (ChildAnimatorState child in source.states)
            {
                AnimatorState clonedState = CloneState(
                    child.state, destination, destinationController, templateAssetPath, child.position);
                map.RegisterClonedState(child.state, clonedState);
            }

            foreach (ChildAnimatorStateMachine child in source.stateMachines)
            {
                CloneStateMachine(
                    child.stateMachine, destination, destinationController,
                    templateAssetPath, child.position, map);
            }
        }

        /// <summary>
        /// レイヤーを丸ごと複製して末尾に追加する。
        /// </summary>
        public static void CloneLayer(
            AnimatorControllerLayer source,
            AnimatorController destinationController,
            string templateAssetPath,
            AnimatorCloneMap map)
        {
            destinationController.AddLayer(destinationController.MakeUniqueLayerName(source.name));

            AnimatorControllerLayer[] layers = destinationController.layers;
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
            destinationController.layers = layers;

            AnimatorStateMachine destinationStateMachine = added.stateMachine;
            Register(destinationStateMachine, destinationController);
            map.RegisterClonedStateMachine(source.stateMachine, destinationStateMachine);

            FillStateMachine(
                source.stateMachine, destinationStateMachine,
                destinationController, templateAssetPath, map);
        }

        /// <summary>
        /// mapに登録済みの「クローンしたノード」について、複製元の遷移をすべて張り直す。
        /// 遷移先はmapで解決する。解決できない遷移は捨てて警告を積む。
        /// </summary>
        /// <param name="isIntentionallyUnresolved">
        /// 解決できないのが想定どおりの遷移を判定する。trueなら警告を出さずに捨てる
        /// </param>
        public static void CloneTransitions(
            AnimatorCloneMap map,
            List<string> warnings,
            Func<AnimatorStateTransition, bool> isIntentionallyUnresolved = null)
        {
            foreach (KeyValuePair<AnimatorState, AnimatorState> pair in map.ClonedStates)
            {
                foreach (AnimatorStateTransition transition in pair.Key.transitions)
                {
                    CloneStateTransition(transition, pair.Value, map, warnings, isIntentionallyUnresolved);
                }
            }

            foreach (KeyValuePair<AnimatorStateMachine, AnimatorStateMachine> pair in map.ClonedStateMachines)
            {
                CloneStateMachineTransitions(pair.Key, pair.Value, map, warnings, isIntentionallyUnresolved);
            }
        }

        /// <summary>
        /// ステートの発信遷移を1本、指定したステートへ複製する。
        /// </summary>
        /// <returns>複製された遷移。遷移先を解決できなければnull</returns>
        public static AnimatorStateTransition CloneStateTransition(
            AnimatorStateTransition source,
            AnimatorState destinationOwner,
            AnimatorCloneMap map,
            List<string> warnings,
            Func<AnimatorStateTransition, bool> isIntentionallyUnresolved = null)
        {
            AnimatorStateTransition clone;

            if (source.isExit)
            {
                clone = destinationOwner.AddExitTransition();
            }
            else if (source.destinationStateMachine != null)
            {
                if (!map.TryResolve(source.destinationStateMachine, out AnimatorStateMachine destination))
                {
                    WarnUnresolved(warnings, destinationOwner.name, source.destinationStateMachine.name,
                        source, isIntentionallyUnresolved);
                    return null;
                }
                clone = destinationOwner.AddTransition(destination);
            }
            else if (source.destinationState != null)
            {
                if (!map.TryResolve(source.destinationState, out AnimatorState destination))
                {
                    WarnUnresolved(warnings, destinationOwner.name, source.destinationState.name,
                        source, isIntentionallyUnresolved);
                    return null;
                }
                clone = destinationOwner.AddTransition(destination);
            }
            else
            {
                WarnUnresolved(warnings, destinationOwner.name, "(none)", source, isIntentionallyUnresolved);
                return null;
            }

            CopyStateTransition(source, clone);
            return clone;
        }

        /// <summary>
        /// AnyState / Entry / StateMachine遷移と既定ステートを複製する。
        /// </summary>
        private static void CloneStateMachineTransitions(
            AnimatorStateMachine source,
            AnimatorStateMachine destination,
            AnimatorCloneMap map,
            List<string> warnings,
            Func<AnimatorStateTransition, bool> isIntentionallyUnresolved)
        {
            foreach (AnimatorStateTransition transition in source.anyStateTransitions)
            {
                CloneAnyStateTransition(transition, destination, map, warnings, isIntentionallyUnresolved);
            }

            foreach (AnimatorTransition transition in source.entryTransitions)
            {
                CloneEntryTransition(transition, destination, map, warnings);
            }

            // サブステートマシン発の遷移は、親のステートマシンが子ごとに保持している
            foreach (ChildAnimatorStateMachine child in source.stateMachines)
            {
                if (child.stateMachine == null) continue;

                if (!map.TryResolve(child.stateMachine, out AnimatorStateMachine transitionSource))
                {
                    WarnUnresolved(warnings, destination.name, child.stateMachine.name);
                    continue;
                }

                foreach (AnimatorTransition transition in source.GetStateMachineTransitions(child.stateMachine))
                {
                    CloneStateMachineTransition(transition, destination, transitionSource, map, warnings);
                }
            }

            if (source.defaultState != null && map.TryResolve(source.defaultState, out AnimatorState defaultState))
            {
                destination.defaultState = defaultState;
            }
        }

        /// <summary>
        /// AnyState遷移を1本、指定したステートマシンへ複製する。
        /// </summary>
        public static AnimatorStateTransition CloneAnyStateTransition(
            AnimatorStateTransition source,
            AnimatorStateMachine destinationOwner,
            AnimatorCloneMap map,
            List<string> warnings,
            Func<AnimatorStateTransition, bool> isIntentionallyUnresolved = null)
        {
            AnimatorStateTransition clone;

            if (source.destinationStateMachine != null)
            {
                if (!map.TryResolve(source.destinationStateMachine, out AnimatorStateMachine destination))
                {
                    WarnUnresolved(warnings, destinationOwner.name + " (AnyState)",
                        source.destinationStateMachine.name, source, isIntentionallyUnresolved);
                    return null;
                }
                clone = destinationOwner.AddAnyStateTransition(destination);
            }
            else if (source.destinationState != null)
            {
                if (!map.TryResolve(source.destinationState, out AnimatorState destination))
                {
                    WarnUnresolved(warnings, destinationOwner.name + " (AnyState)",
                        source.destinationState.name, source, isIntentionallyUnresolved);
                    return null;
                }
                clone = destinationOwner.AddAnyStateTransition(destination);
            }
            else
            {
                WarnUnresolved(warnings, destinationOwner.name + " (AnyState)", "(none)",
                    source, isIntentionallyUnresolved);
                return null;
            }

            CopyStateTransition(source, clone);
            return clone;
        }

        private static void CloneEntryTransition(
            AnimatorTransition source,
            AnimatorStateMachine destinationOwner,
            AnimatorCloneMap map,
            List<string> warnings)
        {
            AnimatorTransition clone;

            if (source.destinationStateMachine != null)
            {
                if (!map.TryResolve(source.destinationStateMachine, out AnimatorStateMachine destination))
                {
                    WarnUnresolved(warnings, destinationOwner.name + " (Entry)",
                        source.destinationStateMachine.name);
                    return;
                }
                clone = destinationOwner.AddEntryTransition(destination);
            }
            else if (source.destinationState != null)
            {
                if (!map.TryResolve(source.destinationState, out AnimatorState destination))
                {
                    WarnUnresolved(warnings, destinationOwner.name + " (Entry)", source.destinationState.name);
                    return;
                }
                clone = destinationOwner.AddEntryTransition(destination);
            }
            else
            {
                WarnUnresolved(warnings, destinationOwner.name + " (Entry)", "(none)");
                return;
            }

            CopyTransition(source, clone);
        }

        private static void CloneStateMachineTransition(
            AnimatorTransition source,
            AnimatorStateMachine destinationOwner,
            AnimatorStateMachine transitionSource,
            AnimatorCloneMap map,
            List<string> warnings)
        {
            AnimatorTransition clone;

            if (source.isExit)
            {
                clone = destinationOwner.AddStateMachineExitTransition(transitionSource);
            }
            else if (source.destinationStateMachine != null)
            {
                if (!map.TryResolve(source.destinationStateMachine, out AnimatorStateMachine destination))
                {
                    WarnUnresolved(warnings, transitionSource.name, source.destinationStateMachine.name);
                    return;
                }
                clone = destinationOwner.AddStateMachineTransition(transitionSource, destination);
            }
            else if (source.destinationState != null)
            {
                if (!map.TryResolve(source.destinationState, out AnimatorState destination))
                {
                    WarnUnresolved(warnings, transitionSource.name, source.destinationState.name);
                    return;
                }
                clone = destinationOwner.AddStateMachineTransition(transitionSource, destination);
            }
            else
            {
                WarnUnresolved(warnings, transitionSource.name, "(none)");
                return;
            }

            CopyTransition(source, clone);
        }

        /// <summary>
        /// 遷移のパラメータを写す。生成時に決めた遷移先は必ず復元する。
        /// </summary>
        private static void CopyStateTransition(AnimatorStateTransition source, AnimatorStateTransition destination)
        {
            AnimatorState destinationState = destination.destinationState;
            AnimatorStateMachine destinationStateMachine = destination.destinationStateMachine;
            bool isExit = destination.isExit;

            EditorUtility.CopySerialized(source, destination);

            destination.destinationState        = destinationState;
            destination.destinationStateMachine = destinationStateMachine;
            destination.isExit                  = isExit;
        }

        private static void CopyTransition(AnimatorTransition source, AnimatorTransition destination)
        {
            AnimatorState destinationState = destination.destinationState;
            AnimatorStateMachine destinationStateMachine = destination.destinationStateMachine;
            bool isExit = destination.isExit;

            EditorUtility.CopySerialized(source, destination);

            destination.destinationState        = destinationState;
            destination.destinationStateMachine = destinationStateMachine;
            destination.isExit                  = isExit;
        }

        private static void CloneBehaviours(
            StateMachineBehaviour[] sources, AnimatorState destination, AnimatorController destinationController)
        {
            destination.behaviours = new StateMachineBehaviour[0];
            foreach (StateMachineBehaviour source in sources)
            {
                if (source == null) continue;
                StateMachineBehaviour clone = destination.AddStateMachineBehaviour(source.GetType());
                EditorUtility.CopySerialized(source, clone);
                Register(clone, destinationController);
            }
        }

        private static void CloneBehaviours(
            StateMachineBehaviour[] sources, AnimatorStateMachine destination, AnimatorController destinationController)
        {
            destination.behaviours = new StateMachineBehaviour[0];
            foreach (StateMachineBehaviour source in sources)
            {
                if (source == null) continue;
                StateMachineBehaviour clone = destination.AddStateMachineBehaviour(source.GetType());
                EditorUtility.CopySerialized(source, clone);
                Register(clone, destinationController);
            }
        }

        /// <summary>
        /// motionを解決する。
        /// テンプレート外のアセットはそのまま参照し、テンプレート内のサブアセットだけ複製する。
        /// 参照のまま残すと、生成物がテンプレート（パッケージ内アセット）に依存してしまう。
        /// </summary>
        public static Motion ResolveMotion(
            Motion source, string templateAssetPath, AnimatorController destinationController)
        {
            if (source == null) return null;
            if (AssetDatabase.GetAssetPath(source) != templateAssetPath) return source;

            if (source is BlendTree blendTree)
            {
                return CloneBlendTree(blendTree, templateAssetPath, destinationController);
            }

            Motion clone = Object.Instantiate(source);
            clone.name = source.name;
            Register(clone, destinationController);
            return clone;
        }

        private static BlendTree CloneBlendTree(
            BlendTree source, string templateAssetPath, AnimatorController destinationController)
        {
            BlendTree clone = new BlendTree();
            Register(clone, destinationController);

            EditorUtility.CopySerialized(source, clone);
            clone.name = source.name;

            // 子のmotionは複製元を指したままなので、1段ずつ解決し直す
            ChildMotion[] children = clone.children;
            for (int i = 0; i < children.Length; i++)
            {
                children[i].motion = ResolveMotion(children[i].motion, templateAssetPath, destinationController);
            }
            clone.children = children;

            return clone;
        }

        private static void WarnUnresolved(
            List<string> warnings,
            string ownerName,
            string destinationName,
            AnimatorStateTransition source = null,
            Func<AnimatorStateTransition, bool> isIntentionallyUnresolved = null)
        {
            // 繋がないことが分かっている遷移まで警告にすると、意図した設定が異常に見えてしまう
            if (source != null && isIntentionallyUnresolved != null && isIntentionallyUnresolved(source)) return;

            warnings.Add(
                "Dropped a transition from '" + ownerName + "' to '" + destinationName +
                "' because the destination could not be resolved in the target animator.");
        }
    }
}
