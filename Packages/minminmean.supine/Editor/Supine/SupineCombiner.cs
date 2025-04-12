using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;

using ExpressionsMenu = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu;
using ExpressionsMenuControl = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control;
using ExpressionParameters = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters;
using ExpressionParameter = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter;

using ModularAvatarMergeAnimator = nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator;

/// <summary>
/// avatarにLocomotion, Menu, Parametersを組み込むクラス
/// </summary>

public class SupineCombiner
{
    private GameObject _avatar;
    private string _avatar_name;
    private VRCAvatarDescriptor _avatarDescriptor;
    public bool canCombine = true;
    public bool alreadyCombined = false;
    public bool otherSupinePrefabPlaced = false;

    private string _supineDirGuid = "2d76018424e030f4597625fa4cdb0d28";
    private string _maPrefabGuid = "f0776ab98fcb1bd4fbb991a3fb0f3d54";
    private string _animatorGuid = "54574f02780fe18449d0bdf9e17bee7d";
    private string[] _sittingAnimationGuids =
    {
        "4de1b4829899b754db2ec3e28014a61e", // ぺたん
        "8d965ea5d99a8cf40bd517890509b5b2", // 立膝（女）
        "7763fb4ad4e8be942861867e99e04deb", // あぐら
        "1e0291513c4de5e4c8c210f788354cba"  // 立膝（男）
    };

    private ExpressionParameter[] _oldSupineParameters = new ExpressionParameter[11]
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


    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="avatar">アバターのGameObject</param>
    public SupineCombiner(GameObject avatar)
    {
        _avatar = avatar;
        _avatar_name = avatar.name;
        _avatarDescriptor = avatar.GetComponent<VRCAvatarDescriptor>();

        if (_avatarDescriptor == null)
        {
            // avatar descriptorがなければエラー
            Debug.LogError("[VRCSupine] Could not find VRCAvatarDescriptor.");
            canCombine = false;
        }
        else if (hasGeneratedFiles())
        {
            //  すでに組込済みの場合、(アバター名)_(数字)で作れるようになるまでループ回す
            alreadyCombined = true;
            Debug.Log("[VRCSupine] Directory already exists. Try to create new directory.");
            int suffix;
            for (suffix=1; hasGeneratedFiles(suffix); suffix++);
            _avatar_name = _avatar_name + "_" + suffix.ToString();
        }

        if (FindOtherSupinePrefab())
        {
            otherSupinePrefabPlaced = true;
            Debug.Log("[VRCSupine] Detect other supine prefab. Will replace by new prefab.");
        }
    }

    /// <summary>
    /// Modular Avatar Prefabを組み立ててavatar直下に設置
    /// </summary>
    public void CreateMAPrefab(
        bool shouldInheritOriginalAnimation = true,
        bool disableJumpMotion = true,
        bool enableJumpAtDesktop = true,
        int sittingPoseOrder1 = 0,
        int sittingPoseOrder2 = 1,
        bool shouldCleanCombinedSupine = false
    )
    {
        if (canCombine)
        {
            // Locomotionを組む
            var supineLocomotion = GetTemplateController();

            if (shouldInheritOriginalAnimation)
            {
                var originalLocomotion = _avatarDescriptor.baseAnimationLayers[0].animatorController as AnimatorController;
                supineLocomotion = InheritOriginalAnimation(supineLocomotion, originalLocomotion) as AnimatorController;
            }
            
            supineLocomotion = ToggleJumpMotion(supineLocomotion, !disableJumpMotion, enableJumpAtDesktop) as AnimatorController;

            SetSittingAnimations(supineLocomotion, sittingPoseOrder1, sittingPoseOrder2);

            Debug.Log("[VRCSupine] Created the directory '" + generatedDirectory() + "'.");

            var maPrefab = GetMAPrefab();
            var createdPrefab = GameObject.Find(maPrefab.name);
            var maGameObj = PrefabUtility.InstantiatePrefab(maPrefab, _avatar.transform) as GameObject;
            var component = maGameObj.GetComponents<ModularAvatarMergeAnimator>()[0];
            component.animator = supineLocomotion;

            EditorUtility.SetDirty(component);

            // 設置済みのMAPrefabを整理
            SortAndCleanMAPrefab(maGameObj, createdPrefab);

            // 結合済みのごろ寝システムを削除
            if (shouldCleanCombinedSupine) {
                CleanCombinedSupine();
            }

            Debug.Log("[VRCSupine] MA Prefab creation is done.");
        } else {
            Debug.LogError("[VRCSupine] Could not create MA Prefab.");
        }
    }

    protected virtual AnimatorController GetTemplateController()
    {
        return CopyAssetFromGuid<AnimatorController>(_animatorGuid);
    }

    protected virtual GameObject GetMAPrefab()
    {
        string maPrefabPath = AssetDatabase.GUIDToAssetPath(_maPrefabGuid);
        return AssetDatabase.LoadAssetAtPath<GameObject>(maPrefabPath);
    }

    private void SortAndCleanMAPrefab(GameObject newPrefab, GameObject oldPrefab)
    {
        bool indexReplaced = false;
        if (oldPrefab != null)
        {
            int createdPrefabIndex = oldPrefab.transform.GetSiblingIndex();
            newPrefab.transform.SetSiblingIndex(createdPrefabIndex);
            GameObject.DestroyImmediate(oldPrefab);
            indexReplaced = true;
        }

        GameObject otherSupinePrefab = FindOtherSupinePrefab();
        if (otherSupinePrefab)
        {
            if (!indexReplaced)
            {
                int otherSupinePrefabIndex = otherSupinePrefab.transform.GetSiblingIndex();
                newPrefab.transform.SetSiblingIndex(otherSupinePrefabIndex);
            }

            GameObject.DestroyImmediate(otherSupinePrefab);
        }
    }

    protected virtual GameObject FindOtherSupinePrefab()
    {
        return GameObject.Find("SupineMA_EX");
    }

    private AnimatorController InheritOriginalAnimation(AnimatorController supineLocomotion, AnimatorController originalLocomotion)
    {
        // statesを取り出し
        var supineLocomotionStates = supineLocomotion.layers[0].stateMachine.states;

        // 元のLocomotionがあればアニメーションを取り出す
        if (originalLocomotion != null)
        {
            var originalLocomotionStates = originalLocomotion.layers[0].stateMachine.states;
            var standingState = FindStateByName(originalLocomotionStates, "Standing");
            var crouchingState = FindStateByName(originalLocomotionStates, "Crouching");
            var proneState = FindStateByName(originalLocomotionStates, "Prone");

            // モーション上書き
            var standing = FindStateByName(supineLocomotionStates, "Standing");
            var crouching = FindStateByName(supineLocomotionStates, "Crouching");
            var prone = FindStateByName(supineLocomotionStates, "Prone");
            if (standingState != null)
                standing.motion = standingState.motion;
            if (crouchingState != null)
                crouching.motion = crouchingState.motion;
            if (proneState != null)
                prone.motion = proneState.motion;
        }

        return supineLocomotion;
    }

    private AnimatorController ToggleJumpMotion(AnimatorController supineLocomotion, bool enableJump, bool enableJumpAtDesktop)
    {
        AnimatorControllerParameter[] parameters = supineLocomotion.parameters;
        foreach (AnimatorControllerParameter parameter in parameters)
        {
            if (parameter.name == "EnableJumpMotion")
            {
                parameter.defaultBool = enableJump;
            }
            else if (parameter.name == "EnableJumpAtDesktop")
            {
                parameter.defaultBool = enableJumpAtDesktop;
            }
        }

        supineLocomotion.parameters = parameters;

        return supineLocomotion;
    }

    private AnimatorController SetSittingAnimations(AnimatorController supineLocomotion, int sittingPoseOrder1, int sittingPoseOrder2)
    {
        // statesを取り出し
        var supineLocomotionStates = supineLocomotion.layers[0].stateMachine.states;

        // 座りアニメーションを変更
        var sittingPose1Path = AssetDatabase.GUIDToAssetPath(_sittingAnimationGuids[sittingPoseOrder1]);
        var sittingPose2Path = AssetDatabase.GUIDToAssetPath(_sittingAnimationGuids[sittingPoseOrder2]);
        var sittingPose1 = AssetDatabase.LoadAssetAtPath<AnimationClip>(sittingPose1Path);
        var sittingPose2 = AssetDatabase.LoadAssetAtPath<AnimationClip>(sittingPose2Path);
        var sittingPose1State = FindStateByName(supineLocomotionStates, "Sit 1");
        var sittingPose2State = FindStateByName(supineLocomotionStates, "Sit 2");
        sittingPose1State.motion = sittingPose1 as AnimationClip;
        sittingPose2State.motion = sittingPose2 as AnimationClip;

        return supineLocomotion;
    }
    protected T CopyAssetFromGuid<T>(string guid) where T : Object
    {
        string templatePath = AssetDatabase.GUIDToAssetPath(guid);
        string templateName = Path.GetFileName(templatePath);
        string generatedPath = generatedDirectory() + "/" + _avatar_name + "_" + templateName;

        if (!Directory.Exists(generatedDirectory()))
        {
            Directory.CreateDirectory(generatedDirectory());
        }

        if (!AssetDatabase.CopyAsset(templatePath, generatedPath))
        {
            Debug.LogError("[VRCSupine] Could not create asset: (" + generatedPath + ") from: (" + templatePath + ")");
            throw new IOException();
        }

        return AssetDatabase.LoadAssetAtPath<T>(generatedPath);
    }

    private string generatedDirectory(int suffix = 0)
    {
        string generatedDirPath = AssetDatabase.GUIDToAssetPath(_supineDirGuid) + "/Generated/";
        if (suffix > 0) {
            return generatedDirPath + _avatar_name + "_" + suffix.ToString();
        }
        else
        {
            return generatedDirPath + _avatar_name;
        }
    }

    private bool hasGeneratedFiles(int suffix = 0)
    {
        return AssetDatabase.IsValidFolder(generatedDirectory(suffix));
    }

    private void CleanCombinedSupine()
    {
        // SerializedObjectで操作する
        var descriptorObj = new SerializedObject(_avatarDescriptor);
        descriptorObj.FindProperty("customizeAnimationLayers").boolValue = true;
        descriptorObj.FindProperty("customExpressions").boolValue = true;

        // ExMenuを組む
        var descriptorMenuProp = descriptorObj.FindProperty("expressionsMenu");

        ExpressionsMenu descriptorMenu = _avatarDescriptor.expressionsMenu;
        if (descriptorMenu == null) descriptorMenu = new ExpressionsMenu();
        var descriptorControls = descriptorMenu.controls;
        if (descriptorControls == null) descriptorControls = new List<ExpressionsMenuControl>();
        
        EditorUtility.SetDirty(descriptorMenu);
        descriptorMenu.controls = RemoveCombinedExMenuControls(descriptorControls);

        descriptorMenuProp.objectReferenceValue = descriptorMenu;

        // ExParametersを組む
        var descriptorParamsProp = descriptorObj.FindProperty("expressionParameters");

        var descriptorParams = _avatarDescriptor.expressionParameters;
        if (descriptorParams == null) descriptorParams = new ExpressionParameters();
        var descriptorParamsArray = descriptorParams.parameters;
        if (descriptorParamsArray == null) descriptorParamsArray = new ExpressionParameter[0];

        EditorUtility.SetDirty(descriptorParams);
        descriptorParams.parameters = RemoveCombinedExParameters(descriptorParamsArray);

        descriptorParamsProp.objectReferenceValue = descriptorParams;

        // 変更を適用
        descriptorObj.ApplyModifiedProperties();
    }

    private List<ExpressionsMenuControl> RemoveCombinedExMenuControls(List<ExpressionsMenuControl> exMenuControls)
    {
        // ごろ寝メニューがあれば削除
        exMenuControls.RemoveAll(IsCombinedSupineMenu);
        return exMenuControls;
    }

    private ExpressionParameter[] RemoveCombinedExParameters(ExpressionParameter[] exParams)
    {
        // ごろ寝パラメータがあれば削除
        var exParamsList = new List<ExpressionParameter>(exParams);
        exParamsList.RemoveAll(IsSupineParameter);
        return exParamsList.ToArray<ExpressionParameter>();
    }

    private bool IsCombinedSupineMenu(ExpressionsMenuControl control)
    {
        // ごろ寝サブメニューか
        bool isSupineMenu = (control.name == "Suimin"   && control.type == ExpressionsMenuControl.ControlType.SubMenu) ||
                            (control.name == "SuiminEx" && control.type == ExpressionsMenuControl.ControlType.SubMenu);
        return isSupineMenu;
    }

    private bool IsSupineParameter(ExpressionParameter parameter)
    {
        // ごろ寝パラメータと一致するか
        return _oldSupineParameters.Contains(parameter, new ExParameterComparer());
    }

    private AnimatorState FindStateByName(ChildAnimatorState[] states, string name)
    {
        // 名前でステートを探索
        foreach (var childState in states)
        {
            if (childState.state.name == name)
            {
                return childState.state;
            }
        }
        return null;
    }

}