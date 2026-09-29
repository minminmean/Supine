using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Supine.Utilities;

namespace Supine
{
    /// <summary>
    /// 組込時の設定を、設置した MA Prefab の SupineMASlot に書き残す。
    ///
    /// ウィンドウの設定は EditorPrefs にあって次の組込で上書きされるため、
    /// 「このアバターはどういう設定で組んだか」は後から分からなくなる。
    /// 設置物そのものに控えを持たせておけば、アバターごとに見返せる。
    /// </summary>
    internal static class SupineCombineRecorder
    {
        public static void Record(
            GameObject maPrefabInstance,
            VRCAvatarDescriptor avatarDescriptor,
            SupineCombineOptions options,
            IReadOnlyList<PosePack.ResolvedPose> injectedPoses,
            PosePack.SupineCrouchInjection crouch)
        {
            SupineMASlot slot = maPrefabInstance.GetComponent<SupineMASlot>();
            if (slot == null) return;

            slot.record = new SupineCombineRecord
            {
                version                  = SupinePackageVersion.Current ?? "?",
                combinedAt               = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                mode                     = options.mode.ToString(),
                sourceAnimator           = ResolveSourceAnimator(avatarDescriptor, options),
                addTargetSpecified       = options.EffectiveAddTargetOverride != null,
                inheritOriginalAnimation = options.ShouldInherit,
                inheritStandingState     = ResolveStateName(options.inheritStandingStateName, SupineNames.States.Standing),
                inheritCrouchingState    = ResolveStateName(options.inheritCrouchingStateName, SupineNames.States.Crouching),
                inheritProneState        = ResolveStateName(options.inheritProneStateName, SupineNames.States.Prone),
                entryState               = ResolveStateName(options.entryStateName, SupineNames.States.Crouching),
                proneState               = ResolveStateName(options.proneStateName, SupineNames.States.Prone),
                disableJumpMotion        = options.ShouldApplyJumpOptions && options.disableJumpMotion,
                enableJumpAtDesktop      = options.ShouldApplyJumpOptions && options.enableJumpAtDesktop,
                sittingPose1             = options.sittingPose1.ToString(),
                sittingPose2             = options.sittingPose2.ToString(),
                keepExistingCrouchPose   = options.keepExistingCrouchPose,
                defaultCrouchPose        = ResolveDefaultCrouchPose(options, crouch),
                crouchSlide              = options.ShouldSlideCrouch,
                posePacks                = CollectPackIds(injectedPoses, crouch),
            };

            EditorUtility.SetDirty(slot);
        }

        /// <summary>
        /// 実際に使ったアバター側のアニメーター。
        /// 手動指定が無いときも自動で解決した先を残す。何を元にしたかが一番見返したい情報なので。
        /// </summary>
        private static RuntimeAnimatorController ResolveSourceAnimator(
            VRCAvatarDescriptor avatarDescriptor, SupineCombineOptions options)
        {
            if (options.mode == SupineCombineMode.Add)
            {
                return BaseAnimatorResolver.Resolve(avatarDescriptor, options.EffectiveAddTargetOverride).controller;
            }

            return options.ShouldInherit ? BaseAnimatorResolver.FindBaseLayerController(avatarDescriptor) : null;
        }

        /// <summary>
        /// 未指定（null）はテンプレートと同じ名前で探す、という意味なのでその名前にしておく。
        /// 空文字は「対応するステートを持たせない」なので空のまま残す。
        /// </summary>
        private static string ResolveStateName(string specified, string templateStateName)
        {
            return specified ?? templateStateName;
        }

        /// <summary>
        /// 既定にしたしゃがみ。組み込まなかったときは空にする。
        /// パックのものは表示名で残す。識別子だけだと、パックを外した後に何だったのか読めない。
        /// </summary>
        private static string ResolveDefaultCrouchPose(SupineCombineOptions options, PosePack.SupineCrouchInjection crouch)
        {
            if (crouch == null) return string.Empty;

            if (!string.IsNullOrEmpty(options.defaultCrouchPoseKey))
            {
                foreach (PosePack.ResolvedCrouchPose pose in crouch.PackPoses)
                {
                    if (pose.Key == options.defaultCrouchPoseKey) return pose.Entry.ResolveDisplayName();
                }
            }

            return options.defaultCrouchPose.ToString();
        }

        private static List<string> CollectPackIds(
            IReadOnlyList<PosePack.ResolvedPose> injectedPoses, PosePack.SupineCrouchInjection crouch)
        {
            List<string> ids = new List<string>();

            void Add(SupinePosePack pack)
            {
                if (pack == null) return;

                string id = pack.ResolvePackId();
                if (!ids.Contains(id)) ids.Add(id);
            }

            if (injectedPoses != null)
            {
                foreach (PosePack.ResolvedPose pose in injectedPoses) Add(pose.Pack);
            }

            if (crouch != null && crouch.PackPoses != null)
            {
                foreach (PosePack.ResolvedCrouchPose pose in crouch.PackPoses) Add(pose.Pack);
            }

            return ids;
        }
    }
}
