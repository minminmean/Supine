using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using VRC.SDK3.Avatars.Components;

using ExpressionsMenu = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu;
using ExpressionsMenuControl = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control;
using ExpressionParameters = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters;
using ExpressionParameter = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter;

namespace Supine
{
    class OldSupineCleaner
    {
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
            // SerializedObjectで操作する
            SerializedObject descriptorObj = new SerializedObject(avatarDescriptor);
            descriptorObj.FindProperty("customizeAnimationLayers").boolValue = true;
            descriptorObj.FindProperty("customExpressions").boolValue = true;

            // ExMenuを組む
            SerializedProperty descriptorMenuProp = descriptorObj.FindProperty("expressionsMenu");

            ExpressionsMenu descriptorMenu = avatarDescriptor.expressionsMenu;
            if (descriptorMenu == null) descriptorMenu = new ExpressionsMenu();
            List<ExpressionsMenuControl> descriptorControls = descriptorMenu.controls;
            if (descriptorControls == null) descriptorControls = new List<ExpressionsMenuControl>();
            
            EditorUtility.SetDirty(descriptorMenu);
            descriptorMenu.controls = RemoveCombinedExMenuControls(descriptorControls);

            descriptorMenuProp.objectReferenceValue = descriptorMenu;

            // ExParametersを組む
            SerializedProperty descriptorParamsProp = descriptorObj.FindProperty("expressionParameters");

            ExpressionParameters descriptorParams = avatarDescriptor.expressionParameters;
            if (descriptorParams == null) descriptorParams = new ExpressionParameters();
            ExpressionParameter[] descriptorParamsArray = descriptorParams.parameters;
            if (descriptorParamsArray == null) descriptorParamsArray = new ExpressionParameter[0];

            EditorUtility.SetDirty(descriptorParams);
            descriptorParams.parameters = RemoveCombinedExParameters(descriptorParamsArray);

            descriptorParamsProp.objectReferenceValue = descriptorParams;

            // 変更を適用
            descriptorObj.ApplyModifiedProperties();
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
    }
}