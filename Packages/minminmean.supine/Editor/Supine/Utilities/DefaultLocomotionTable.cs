using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Supine.Utilities
{
    /// <summary>
    /// VRChat既定Locomotionが持つノード名の一覧。
    ///
    /// テンプレートのどのステートが「ごろ寝システムが足した分」なのかを、
    /// 既定Locomotionとの差分で判定するために使う。
    /// テンプレート側のステート名をハードコードしないので、
    /// 通常版とEX版のようにテンプレートが違っても同じ判定が成立する。
    /// </summary>
    internal static class DefaultLocomotionTable
    {
        // NDMFがisDefault時のマージ先として使うのと同じアセット。
        // 同じものを基準にすることで、生成時とビルド時で判定がずれない。
        private const string DefaultControllerPath =
            "Packages/com.vrchat.avatars/Samples/AV3 Demo Assets/Animation/Controllers/vrc_AvatarV3LocomotionLayer.controller";

        // 既定Locomotionを読めなかったときの保険。
        private static readonly string[] FallbackStateNames =
            {
                "Standing", "Crouching", "Prone",
                "Short Fall", "HardLand", "LongFall", "RestoreTracking",
                "SmallHop", "RestoreToHop", "QuickLand"
            };

        private static readonly string[] FallbackStateMachineNames = { "JumpAndFall" };

        private static HashSet<string> _stateNames;
        private static HashSet<string> _stateMachineNames;

        /// <summary>
        /// VRChat既定Locomotionコントローラを読む。パス直指定で見つからなければGUIDで引く。
        /// </summary>
        public static AnimatorController LoadController()
        {
            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(DefaultControllerPath);
            if (controller != null) return controller;

            string guid = JsonHelper.GetGuidList().vrchat.default_locomotion;
            if (string.IsNullOrEmpty(guid)) return null;

            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;

            return AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        }

        /// <summary>既定Locomotionに元から在るステート名か</summary>
        public static bool IsDefaultStateName(string name)
        {
            EnsureLoaded();
            return _stateNames.Contains(name);
        }

        /// <summary>既定Locomotionに元から在るステートマシン名か</summary>
        public static bool IsDefaultStateMachineName(string name)
        {
            EnsureLoaded();
            return _stateMachineNames.Contains(name);
        }

        private static void EnsureLoaded()
        {
            if (_stateNames != null) return;

            AnimatorController controller = LoadController();
            if (controller == null || controller.layers.Length == 0 || controller.layers[0].stateMachine == null)
            {
                Debug.LogWarning(
                    "[VRCSupine] Could not load the VRChat default locomotion controller. " +
                    "Falling back to the built-in state name list.");
                _stateNames        = new HashSet<string>(FallbackStateNames);
                _stateMachineNames = new HashSet<string>(FallbackStateMachineNames);
                return;
            }

            // レイヤー0のグラフだけを辿る。
            // 既定Locomotionのアセットにはどこからも参照されていない残骸ステートが含まれるため、
            // オブジェクトを総なめにすると余計な名前を拾ってしまう。
            AnimatorStateMachine root = controller.layers[0].stateMachine;
            _stateNames        = new HashSet<string>(AnimatorStateUtility.BuildStateIndex(root).Keys);
            _stateMachineNames = new HashSet<string>(AnimatorStateUtility.BuildStateMachineIndex(root).Keys);
        }
    }
}
