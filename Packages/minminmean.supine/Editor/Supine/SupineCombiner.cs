using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;

using ModularAvatarMergeAnimator = nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator;
using Supine.Utilities;

namespace Supine
{

    /// <summary>
    /// avatarにごろ寝システムPrefabを設置するためのクラス
    /// </summary>

    public class SupineCombiner
    {
        private GameObject _avatar;
        private string _avatar_name_with_suffix;
        private VRCAvatarDescriptor _avatarDescriptor;
        private bool _exMode = false;
        public bool canCombine = true;

        private string MmmAssetPath           = "Assets/MinMinMart";
        private string SupineNormalPrefabName = "SupineMA";
        private string SupineExPrefabName     = "SupineMA_EX";
        private string _maPrefabGuid    = Utility.GuidList.prefabs.normal;
        private string _controllerGuid  = Utility.GuidList.controllers.normal;
        private string _appVersion      = Utility.GetAppVersion();
        private string[] SittingAnimationGuids =
        {
            Utility.GuidList.animations.sitting.petan,          // ぺたん
            Utility.GuidList.animations.sitting.tatehiza_girl,  // 立膝（女）
            Utility.GuidList.animations.sitting.agura,          // あぐら
            Utility.GuidList.animations.sitting.tatehiza_boy    // 立膝（男）
        };

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="avatar">GameObject アバター</param>
        /// <param name="exMode">bool EXモード</param>
        public SupineCombiner(GameObject avatar, bool exMode = false)
        {
            _avatar = avatar;
            _avatar_name_with_suffix = avatar.name;
            _avatarDescriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            _exMode = exMode;
            
            if(_exMode) SwitchToExMode();
            else SwitchToNormalMode();

            if (_avatarDescriptor == null)
            {
                // avatar descriptorがなければエラー
                Debug.LogError("[VRCSupine] Could not find VRCAvatarDescriptor.");
                canCombine = false;
            }
            else if (HasGeneratedFiles())
            {
                //  すでに組込済みの場合、(アバター名)_(数字)で作れるようになるまでループ回す
                int suffix;
                for (suffix=1; HasGeneratedFiles(suffix); suffix++);
                _avatar_name_with_suffix += "_" + suffix.ToString();
            }
        }

        private void SwitchToExMode()
        {
            _maPrefabGuid   = Utility.GuidList.prefabs.ex;
            _controllerGuid = Utility.GuidList.controllers.ex;
            _appVersion     = Utility.GetAppVersionEX();
        }

        private void SwitchToNormalMode()
        {
            _maPrefabGuid   = Utility.GuidList.prefabs.normal;
            _controllerGuid = Utility.GuidList.controllers.normal;
            _appVersion     = Utility.GetAppVersion();
        }

        /// <summary>
        /// コントローラを編集してMA Prefabに差し込みavatar直下に設置
        /// </summary>
        /// <param name="shouldInheritOriginalAnimation">bool 歩行アニメーションの継承</param>
        /// <param name="disableJumpMotion">bool ジャンプモーションの無効</param>
        /// <param name="enableJumpAtDesktop">bool デスクトップでジャンプモーションを有効化</param>
        /// <param name="sittingPoseOrder1">int 座りポーズ1</param>
        /// <param name="sittingPoseOrder2">int 座りポーズ2</param>
        /// <param name="shouldCleanCombinedSupine">bool 古いごろ寝システムを削除する</param>
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
                // オプションに従ってLocomotionを編集
                AnimatorController supineLocomotion = CopyAssetFromGuid<AnimatorController>(_controllerGuid);

                if (shouldInheritOriginalAnimation)
                {
                    AnimatorController originalLocomotion = _avatarDescriptor.baseAnimationLayers[0].animatorController as AnimatorController;
                    supineLocomotion = InheritOriginalAnimation(supineLocomotion, originalLocomotion);
                }
                supineLocomotion = ToggleJumpMotion(supineLocomotion, !disableJumpMotion, enableJumpAtDesktop);
                supineLocomotion = SetSittingAnimations(supineLocomotion, sittingPoseOrder1, sittingPoseOrder2);

                // MA Prefabを設置＆編集したLocomotionを差す
                string maPrefabPath = AssetDatabase.GUIDToAssetPath(_maPrefabGuid);
                GameObject maPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(maPrefabPath);
                GameObject alreadyPlacedPrefab = GameObject.Find(maPrefab.name);
                GameObject maPrefabInstance = PrefabUtility.InstantiatePrefab(maPrefab, _avatar.transform) as GameObject;

                ModularAvatarMergeAnimator component = maPrefabInstance.GetComponents<ModularAvatarMergeAnimator>()[0];
                component.animator = supineLocomotion;
                EditorUtility.SetDirty(component);

                // 設置済みのMAPrefabを整理
                SortAndCleanMAPrefab(maPrefabInstance, alreadyPlacedPrefab);

                // 結合済みの古いごろ寝システムを削除
                if (shouldCleanCombinedSupine) {
                    OldSupineCleaner.CleanCombinedSupine(_avatarDescriptor);
                }

                Debug.Log("[VRCSupine] MA Prefab creation is done.");
            } else {
                Debug.LogError("[VRCSupine] Could not create MA Prefab.");
            }
        }

        /// <summary>
        /// 歩行モーションを継承する
        /// </summary>
        /// <param name="originalLocomotion">オリジナルのBaseコントローラ</param>
        /// <param name="supineLocomotion">ごろ寝システムのBaseコントローラ</param>
        private AnimatorController InheritOriginalAnimation(AnimatorController supineLocomotion, AnimatorController originalLocomotion)
        {
            // statesを取り出し
            ChildAnimatorState[] supineLocomotionStates = supineLocomotion.layers[0].stateMachine.states;

            // 元のLocomotionがあればアニメーションを取り出す
            if (originalLocomotion != null)
            {
                ChildAnimatorState[] originalLocomotionStates = originalLocomotion.layers[0].stateMachine.states;
                AnimatorState originalStanding  = Utility.FindAnimatorStateByName(originalLocomotionStates, "Standing");
                AnimatorState originalCrouching = Utility.FindAnimatorStateByName(originalLocomotionStates, "Crouching");
                AnimatorState originalProne     = Utility.FindAnimatorStateByName(originalLocomotionStates, "Prone");

                // モーション上書き
                AnimatorState standing  = Utility.FindAnimatorStateByName(supineLocomotionStates, "Standing");
                AnimatorState crouching = Utility.FindAnimatorStateByName(supineLocomotionStates, "Crouching");
                AnimatorState prone     = Utility.FindAnimatorStateByName(supineLocomotionStates, "Prone");
                if (originalStanding != null)
                    standing.motion = originalStanding.motion;
                if (originalCrouching != null)
                    crouching.motion = originalCrouching.motion;
                if (originalProne != null)
                    prone.motion = originalProne.motion;
            }

            return supineLocomotion;
        }

        /// <summary>
        /// ジャンプモーションの有効・無効を切り替える
        /// </summary>
        /// <param name="supineLocomotion">ごろ寝システムのBaseコントローラ</param>
        /// <param name="enableJump">bool ジャンプを有効にするか</param>
        /// <param name="enableJumpAtDesktop">bool デスクトップでジャンプを有効にするか</param>
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

        /// <summary>
        /// 座りモーションの設定
        /// </summary>
        /// <param name="supineLocomotion">ごろ寝システムのBaseコントローラ</param>
        /// <param name="sittingPoseOrder1">int 座りポーズ1</param>
        /// <param name="sittingPoseOrder2">int 座りポーズ2</param>
        private AnimatorController SetSittingAnimations(AnimatorController supineLocomotion, int sittingPoseOrder1, int sittingPoseOrder2)
        {
            // statesを取り出し
            ChildAnimatorState[] supineLocomotionStates = supineLocomotion.layers[0].stateMachine.states;

            // 座りアニメーションを変更
            string sittingPose1Path = AssetDatabase.GUIDToAssetPath(SittingAnimationGuids[sittingPoseOrder1]);
            string sittingPose2Path = AssetDatabase.GUIDToAssetPath(SittingAnimationGuids[sittingPoseOrder2]);
            AnimationClip sittingPose1 = AssetDatabase.LoadAssetAtPath<AnimationClip>(sittingPose1Path);
            AnimationClip sittingPose2 = AssetDatabase.LoadAssetAtPath<AnimationClip>(sittingPose2Path);
            AnimatorState sittingPose1State = Utility.FindAnimatorStateByName(supineLocomotionStates, "Sit 1");
            AnimatorState sittingPose2State = Utility.FindAnimatorStateByName(supineLocomotionStates, "Sit 2");
            sittingPose1State.motion = sittingPose1;
            sittingPose2State.motion = sittingPose2;

            return supineLocomotion;
        }

        /// <summary>
        /// 新しいMA Prefabと古いMA Prefabの位置を整理する
        /// EX⇔通常版の入れ替えも行う
        /// </summary>
        /// <param name="newPrefab">新しいMA Prefab</param>
        /// <param name="oldPrefab">古いMA Prefab</param>
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

            GameObject otherSupinePrefab = _exMode ? GameObject.Find(SupineNormalPrefabName) : GameObject.Find(SupineExPrefabName);
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

        /// <summary>
        /// GUIDを指定してアセットをコピーする
        /// </summary>
        /// <param name="guid">GUID</param>
        private T CopyAssetFromGuid<T>(string guid) where T : Object
        {
            string templatePath = AssetDatabase.GUIDToAssetPath(guid);
            string templateName = Path.GetFileName(templatePath);
            string destinationPath = MakeGeneratedDirPath() + "/" + _avatar_name_with_suffix + "_" + templateName;

            return Utility.CopyAssetFromPath<T>(templatePath, destinationPath);
        }

        /// <summary>
        /// 生成したごろ寝システムコントローラを置くディレクトリパスを作成
        /// </summary>
        /// <param name="suffix">int 後ろにつける数字</param>
        private string MakeGeneratedDirPath(int suffix = 0)
        {
            string generatedDirPath = MmmAssetPath + '/' + _appVersion + "/Generated";
            if (suffix > 0) {
                return generatedDirPath + "/" + _avatar_name_with_suffix + "_" + suffix.ToString();
            }
            else
            {
                return generatedDirPath + "/" + _avatar_name_with_suffix;
            }
        }

        /// <summary>
        /// すでに作成されたファイルがあるか判定
        /// </summary>
        /// <param name="suffix">int 後ろにつける数字</param>
        /// <returns>bool</returns>
        private bool HasGeneratedFiles(int suffix = 0)
        {
            return AssetDatabase.IsValidFolder(MakeGeneratedDirPath(suffix));
        }
    }
}
