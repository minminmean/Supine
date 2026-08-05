using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

using ExpressionsMenu = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu;
using ExpressionsMenuControl = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control;
using ExpressionParameters = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters;
using ExpressionParameter = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter;

namespace Supine
{
    /// <summary>
    /// v3.0.2以前のごろ寝や、SupineMASlot導入以前に設置されたMA Prefabなど、
    /// 現行アーキテクチャと互換性のない過去のごろ寝システムの残骸を削除するためのユーティリティクラス
    /// </summary>

    class OldSupineCleaner
    {
        // SupineMASlot導入以前に設置された、マーカーの無いMA Prefabの互換削除用
        private const string LegacyNormalPrefabName = "SupineMA";
        private const string LegacyExPrefabName     = "SupineMA_EX";

        private static ExpressionParameter[] _oldSupineParameters = new ExpressionParameter[11]
            {
                new ExpressionParameter { name = "VRCLockPose",                 valueType = ExpressionParameters.ValueType.Int },
                new ExpressionParameter { name = "VRCFootAnchor",               valueType = ExpressionParameters.ValueType.Int },
                new ExpressionParameter { name = "VRCMjiTime",                  valueType = ExpressionParameters.ValueType.Float },
                new ExpressionParameter { name = "VRCKjiTime",                  valueType = ExpressionParameters.ValueType.Float },
                new ExpressionParameter { name = "VRCSupine",                   valueType = ExpressionParameters.ValueType.Int },
                new ExpressionParameter { name = "VRCLockPose",                 valueType = ExpressionParameters.ValueType.Bool },
                new ExpressionParameter { name = "VRCFootAnchor",               valueType = ExpressionParameters.ValueType.Bool },
                new ExpressionParameter { name = "VRCSupineExAdjust",           valueType = ExpressionParameters.ValueType.Float },
                new ExpressionParameter { name = "VRCSupineExAdjusting",        valueType = ExpressionParameters.ValueType.Bool },
                new ExpressionParameter { name = "VRCFootAnchorHandSwitchable", valueType = ExpressionParameters.ValueType.Bool },
                new ExpressionParameter { name = "VRCSupineAutoRotation",       valueType = ExpressionParameters.ValueType.Bool }
            };

        
        public static void CleanCombinedSupine(VRCAvatarDescriptor avatarDescriptor)
        {
            bool hasCombinedMenu = HasCombinedSupineMenu(avatarDescriptor.expressionsMenu);
            bool hasOldParameters = HasOldSupineParameters(avatarDescriptor.expressionParameters);
            if (!hasCombinedMenu && !hasOldParameters) return;

            // SerializedObjectで操作する
            SerializedObject descriptorObj = new SerializedObject(avatarDescriptor);
            descriptorObj.FindProperty("customizeAnimationLayers").boolValue = true;
            descriptorObj.FindProperty("customExpressions").boolValue = true;

            if (hasCombinedMenu)
            {
                // ExMenuを組む
                SerializedProperty descriptorMenuProp = descriptorObj.FindProperty("expressionsMenu");
                ExpressionsMenu descriptorMenu = avatarDescriptor.expressionsMenu;

                EditorUtility.SetDirty(descriptorMenu);
                descriptorMenu.controls = RemoveCombinedExMenuControls(descriptorMenu.controls);

                descriptorMenuProp.objectReferenceValue = descriptorMenu;
            }

            if (hasOldParameters)
            {
                // ExParametersを組む
                SerializedProperty descriptorParamsProp = descriptorObj.FindProperty("expressionParameters");
                ExpressionParameters descriptorParams = avatarDescriptor.expressionParameters;

                EditorUtility.SetDirty(descriptorParams);
                descriptorParams.parameters = RemoveCombinedExParameters(descriptorParams.parameters);

                descriptorParamsProp.objectReferenceValue = descriptorParams;
            }

            // 変更を適用
            descriptorObj.ApplyModifiedProperties();
        }

        private static bool HasCombinedSupineMenu(ExpressionsMenu menu)
        {
            return menu != null && menu.controls != null && menu.controls.Any(IsCombinedSupineMenu);
        }

        private static bool HasOldSupineParameters(ExpressionParameters parameters)
        {
            return parameters != null && parameters.parameters != null && parameters.parameters.Any(IsSupineParameter);
        }

        private static List<ExpressionsMenuControl> RemoveCombinedExMenuControls(List<ExpressionsMenuControl> exMenuControls)
        {
            exMenuControls.RemoveAll(IsCombinedSupineMenu);
            return exMenuControls;
        }

        private static ExpressionParameter[] RemoveCombinedExParameters(ExpressionParameter[] exParams)
        {
            List<ExpressionParameter> exParamsList = new List<ExpressionParameter>(exParams);
            exParamsList.RemoveAll(IsSupineParameter);
            return exParamsList.ToArray<ExpressionParameter>();
        }

        private static bool IsCombinedSupineMenu(ExpressionsMenuControl control)
        {
            bool isSupineMenu = (control.name == "Suimin"   && control.type == ExpressionsMenuControl.ControlType.SubMenu) ||
                                (control.name == "SuiminEx" && control.type == ExpressionsMenuControl.ControlType.SubMenu);
            return isSupineMenu;
        }

        private static bool IsSupineParameter(ExpressionParameter parameter)
        {
            return _oldSupineParameters.Contains(parameter, new ExParameterComparer());
        }

        /// <summary>
        /// SupineMASlot導入以前（マーカーの無い）に設置された通常版/EX版のMA Prefabを削除する。
        /// マーカーが無いため通常のSortAndCleanMAPrefabでは検出できない、
        /// バージョン跨ぎでの入れ替え時の残骸を掃除するための互換対応。
        /// </summary>
        /// <param name="avatarTransform">アバターのTransform</param>
        /// <param name="excluding">削除対象から除外する（今回新しく設置した）MA Prefab</param>
        public static void RemoveMarkerlessMAPrefabs(Transform avatarTransform, GameObject excluding)
        {
            List<GameObject> targets = new List<GameObject>();
            foreach (Transform child in avatarTransform)
            {
                if (child.gameObject == excluding) continue;
                bool isKnownMAPrefabName = child.name == LegacyNormalPrefabName || child.name == LegacyExPrefabName;
                if (isKnownMAPrefabName && child.GetComponent<SupineMASlot>() == null)
                {
                    targets.Add(child.gameObject);
                }
            }

            foreach (GameObject target in targets)
            {
                GameObject.DestroyImmediate(target);
            }
        }
    }
}