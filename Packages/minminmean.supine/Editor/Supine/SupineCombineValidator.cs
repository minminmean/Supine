using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Supine.Utilities;

namespace Supine
{
    /// <summary>
    /// 組込前の検証。
    ///
    /// 「このオプションで組み込めるか」だけを見る役割で、設置処理からは切り離してある。
    /// ステート名の解決は SupineLocomotionAdder と同じ経路を通すこと。
    /// ここの解釈が生成側とずれると、警告と実際の結果が食い違う。
    /// </summary>
    internal sealed class SupineCombineValidator
    {
        private readonly VRCAvatarDescriptor _avatarDescriptor;
        private readonly SupineVariant _variant;

        public SupineCombineValidator(VRCAvatarDescriptor avatarDescriptor, SupineVariant variant)
        {
            _avatarDescriptor = avatarDescriptor;
            _variant = variant;
        }

        /// <summary>
        /// 警告があっても組込自体は続行できるため、失敗と警告は分けて返す。
        /// </summary>
        public SupineCheckResult Validate(SupineCombineOptions options)
        {
            SupineCheckResult result = new SupineCheckResult();

            if (_avatarDescriptor == null)
            {
                result.Failure = SupineCombineFailure.NoAvatarDescriptor;
                return result;
            }

            if (!_variant.IsValid)
            {
                result.Failure = SupineCombineFailure.InvalidVariant;
                return result;
            }

            AnimatorController template = _variant.LoadController();
            if (template == null || template.layers.Length == 0 || template.layers[0].stateMachine == null)
            {
                result.Failure = SupineCombineFailure.InvalidVariant;
                return result;
            }

            if (options.mode == SupineCombineMode.Add)
            {
                ValidateAddTarget(options, template, result);
            }
            else if (options.ShouldInherit)
            {
                ValidateInheritSource(options, result);
            }

            return result;
        }

        /// <summary>
        /// 継承元のステートが引けるかを見る。引けなければモーションが差し替わらない。
        /// </summary>
        private void ValidateInheritSource(SupineCombineOptions options, SupineCheckResult result)
        {
            AnimatorController source = BaseAnimatorResolver.FindBaseLayerController(_avatarDescriptor);
            if (source == null || source.layers.Length == 0 || source.layers[0].stateMachine == null)
            {
                result.Warnings.Add(
                    "The avatar has no animator on its Base layer, so there is nothing to inherit.");
                return;
            }

            Dictionary<string, AnimatorState> sourceStates =
                AnimatorStateUtility.BuildStateIndex(source.layers[0].stateMachine);

            foreach (string templateStateName in InheritedStateTable.TemplateStateNames)
            {
                // 空指定は「ごろ寝システムのアニメーションを使う」という選択なので警告しない
                if (!InheritedStateTable.TryResolveSourceStateName(
                        options, templateStateName, out string sourceStateName)) continue;

                if (sourceStates.ContainsKey(sourceStateName)) continue;

                result.Warnings.Add(
                    "No state matching '" + templateStateName + "' was found in the avatar's animator, " +
                    "so its animation will not be inherited. " +
                    "Pick the corresponding state in the combiner window.");
            }
        }

        private void ValidateAddTarget(
            SupineCombineOptions options, AnimatorController template, SupineCheckResult result)
        {
            BaseAnimatorResolution resolution =
                BaseAnimatorResolver.Resolve(_avatarDescriptor, options.EffectiveAddTargetOverride);

            if (!resolution.IsValid)
            {
                result.Failure = SupineCombineFailure.AddTargetNotFound;
                return;
            }

            AnimatorController target = resolution.controller;
            if (target.layers.Length == 0 || target.layers[0].stateMachine == null)
            {
                result.Failure = SupineCombineFailure.AddTargetNoLayer;
                return;
            }

            Dictionary<string, AnimatorState> targetStates =
                AnimatorStateUtility.BuildStateIndex(target.layers[0].stateMachine);

            // 名前の解決は生成側と同じ経路を通す。ここがずれると警告と実際の結果が食い違う
            Dictionary<string, string> stateNameOverrides =
                SupineLocomotionAdder.BuildStateNameOverrides(options);

            // 入口ステートが決まらないと、ごろ寝システムへ入る経路そのものが作れない
            if (!SupineLocomotionAdder.TryResolveStateName(
                    stateNameOverrides, SupineLocomotionAdder.EntryStateName, out string entryStateName) ||
                !targetStates.ContainsKey(entryStateName))
            {
                result.Failure = SupineCombineFailure.AddEntryStateNotSelected;
                return;
            }

            SupineLocomotionAdder.TryResolveStateName(
                stateNameOverrides, SupineLocomotionAdder.ProneStateName, out string proneStateName);

            // 組込済みなら前回の分を掃除してから足し直すので、同名衝突は起きない
            bool alreadyCombined = SupineLocomotionAdder.IsSupineCombined(template, target);
            if (alreadyCombined)
            {
                result.Warnings.Add(
                    "The target animator already contains Supine. " +
                    "The previous Supine states and layers will be removed before adding it again, " +
                    "though some unused states may be left behind.");
            }

            WarnOnStateNameCollisions(template, targetStates, alreadyCombined, result);
            WarnOnMissingConnectedStates(template, targetStates, stateNameOverrides, result);

            if (SupineLocomotionAdder.HasConflictingLieDownDestination(
                    SupineLocomotionAdder.CollectLieDownDestinationNames(template, target, entryStateName),
                    proneStateName))
            {
                result.Warnings.Add(
                    "The entry state has transitions that lie down into states other than the chosen prone state. " +
                    "They take priority over the Supine poses, so they will be removed.");
            }

            WarnOnParameterTypeMismatch(template, target, result);

            if (target.layers[0].avatarMask != null)
            {
                result.Warnings.Add(
                    "The first layer of the target animator has an avatar mask. " +
                    "The added Supine states cannot animate the bones excluded by it.");
            }
        }

        /// <summary>
        /// 追加するステートと同名のステートが追加先にあると、Unityがリネームして足すことになる。
        /// </summary>
        private static void WarnOnStateNameCollisions(
            AnimatorController template,
            Dictionary<string, AnimatorState> targetStates,
            bool alreadyCombined,
            SupineCheckResult result)
        {
            // 組込済みなら足す前に掃除されるので衝突しない
            if (alreadyCombined) return;

            foreach (AnimatorState state in AnimatorStateUtility.CollectStates(template.layers[0].stateMachine))
            {
                // 既定Locomotion由来のステートは追加しない。既存のものへ紐づけるだけ
                if (DefaultLocomotionTable.IsDefaultStateName(state.name)) continue;
                if (!targetStates.ContainsKey(state.name)) continue;

                result.Warnings.Add(
                    "The target animator already has a state named '" + state.name +
                    "'. The added one will be renamed.");
            }
        }

        /// <summary>
        /// ごろ寝システムが既存ステートと繋がるために必要なステートが、追加先にあるかを見る。
        /// </summary>
        private static void WarnOnMissingConnectedStates(
            AnimatorController template,
            Dictionary<string, AnimatorState> targetStates,
            Dictionary<string, string> stateNameOverrides,
            SupineCheckResult result)
        {
            HashSet<string> requiredStates = new HashSet<string>();
            CollectStatesConnectedToSupine(template, requiredStates);

            foreach (string requiredState in requiredStates)
            {
                // 空指定は「そのステートを持たせない」という選択なので警告しない
                if (!SupineLocomotionAdder.TryResolveStateName(
                        stateNameOverrides, requiredState, out string destinationState)) continue;

                if (targetStates.ContainsKey(destinationState)) continue;

                result.Warnings.Add(
                    "No state matching '" + requiredState + "' was found in the target animator, " +
                    "so the Supine states will not be connected to it. " +
                    "Pick the corresponding state in the combiner window.");
            }
        }

        /// <summary>
        /// テンプレートのレイヤー0で、ごろ寝の追加ステートと遷移でつながっている既定Locomotion側のステート名を集める。
        /// 入口になるもの（追加ステートへ出ていく）と、戻り先になるもの（追加ステートから入ってくる）の両方。
        /// </summary>
        private static void CollectStatesConnectedToSupine(
            AnimatorController template, HashSet<string> connectedStates)
        {
            foreach (AnimatorState state in AnimatorStateUtility.CollectStates(template.layers[0].stateMachine))
            {
                bool isDefaultState = DefaultLocomotionTable.IsDefaultStateName(state.name);

                foreach (AnimatorStateTransition transition in state.transitions)
                {
                    if (transition.destinationState == null) continue;

                    bool isDefaultDestination =
                        DefaultLocomotionTable.IsDefaultStateName(transition.destinationState.name);

                    if (isDefaultState && !isDefaultDestination)
                    {
                        connectedStates.Add(state.name);
                    }
                    else if (!isDefaultState && isDefaultDestination)
                    {
                        connectedStates.Add(transition.destinationState.name);
                    }
                }
            }
        }

        private static void WarnOnParameterTypeMismatch(
            AnimatorController template, AnimatorController target, SupineCheckResult result)
        {
            Dictionary<string, AnimatorControllerParameter> targetParameters =
                new Dictionary<string, AnimatorControllerParameter>();
            foreach (AnimatorControllerParameter parameter in target.parameters)
            {
                targetParameters[parameter.name] = parameter;
            }

            foreach (AnimatorControllerParameter parameter in template.parameters)
            {
                if (!targetParameters.TryGetValue(parameter.name, out AnimatorControllerParameter current)) continue;
                if (current.type == parameter.type) continue;

                result.Warnings.Add(
                    "The parameter '" + parameter.name + "' already exists as " + current.type +
                    " but Supine expects " + parameter.type + ".");
            }
        }
    }
}
