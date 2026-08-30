using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Supine.Utilities
{
    /// <summary>追加先アニメーターをどこから引いてきたか</summary>
    internal enum BaseAnimatorSource
    {
        /// <summary>ウィンドウで手動指定された</summary>
        Manual,

        /// <summary>アバターのBaseレイヤーから自動取得した</summary>
        AvatarDescriptor,

        /// <summary>アバターに設定が無いのでVRChat既定Locomotionを使う</summary>
        VrcDefault,

        /// <summary>どれも解決できなかった</summary>
        NotFound
    }

    internal struct BaseAnimatorResolution
    {
        public AnimatorController controller;
        public BaseAnimatorSource source;

        public bool IsValid => controller != null;
    }

    /// <summary>
    /// ごろ寝システムのステート群を追加する先のアニメーターを決める。
    /// ウィンドウの表示と実際の生成で同じ結果になるよう、解決はこの1箇所に集約する。
    /// </summary>
    internal static class BaseAnimatorResolver
    {
        public static BaseAnimatorResolution Resolve(
            VRCAvatarDescriptor avatarDescriptor, AnimatorController manualOverride)
        {
            if (manualOverride != null)
            {
                return new BaseAnimatorResolution
                    {
                        controller = manualOverride,
                        source     = BaseAnimatorSource.Manual
                    };
            }

            AnimatorController fromAvatar = FindBaseLayerController(avatarDescriptor);
            if (fromAvatar != null)
            {
                return new BaseAnimatorResolution
                    {
                        controller = fromAvatar,
                        source     = BaseAnimatorSource.AvatarDescriptor
                    };
            }

            AnimatorController vrcDefault = DefaultLocomotionTable.LoadController();
            return new BaseAnimatorResolution
                {
                    controller = vrcDefault,
                    source     = vrcDefault != null ? BaseAnimatorSource.VrcDefault : BaseAnimatorSource.NotFound
                };
        }

        /// <summary>
        /// アバターのBase（Locomotion）レイヤーに設定されたコントローラを返す。
        /// 未設定・既定のままなら null。
        /// </summary>
        public static AnimatorController FindBaseLayerController(VRCAvatarDescriptor avatarDescriptor)
        {
            if (avatarDescriptor == null || avatarDescriptor.baseAnimationLayers == null) return null;

            foreach (VRCAvatarDescriptor.CustomAnimLayer layer in avatarDescriptor.baseAnimationLayers)
            {
                if (layer.type != VRCAvatarDescriptor.AnimLayerType.Base) continue;
                if (layer.isDefault) return null;
                return AsAnimatorController(layer.animatorController);
            }

            return null;
        }

        /// <summary>
        /// AnimatorOverrideControllerが設定されている場合は、元のAnimatorControllerまで辿る。
        /// </summary>
        private static AnimatorController AsAnimatorController(RuntimeAnimatorController runtimeController)
        {
            RuntimeAnimatorController current = runtimeController;
            while (current is AnimatorOverrideController overrideController)
            {
                current = overrideController.runtimeAnimatorController;
            }
            return current as AnimatorController;
        }
    }
}
