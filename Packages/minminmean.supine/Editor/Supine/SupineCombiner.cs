using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
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
        private const string MmmAssetPath = "Assets/MinMinMart";

        private readonly GameObject _avatar;
        private readonly VRCAvatarDescriptor _avatarDescriptor;
        private readonly SupineVariant _variant;
        private readonly string _versionFolderName;
        private string _avatarNameWithSuffix;

        public bool CanCombine { get; private set; } = true;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="avatar">GameObject アバター</param>
        /// <param name="variant">SupineVariant 設置するバリアント（通常版 / EX版）</param>
        /// <param name="versionFolderName">string 生成先フォルダ名（例: "Supine v4.4.0"）</param>
        public SupineCombiner(GameObject avatar, SupineVariant variant, string versionFolderName)
        {
            _avatar = avatar;
            _avatarNameWithSuffix = AssetPathUtility.SanitizeFileName(avatar.name);
            _avatarDescriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            _variant = variant;
            _versionFolderName = versionFolderName;

            if (_avatarDescriptor == null)
            {
                // avatar descriptorがなければエラー
                Debug.LogError("[VRCSupine] Could not find VRCAvatarDescriptor.");
                CanCombine = false;
            }
            else if (!_variant.IsValid)
            {
                // guids.jsonが読めていない、または内容が欠けている
                Debug.LogError("[VRCSupine] Could not resolve the Supine variant assets. Check guids.json.");
                CanCombine = false;
            }
            else if (HasGeneratedFiles())
            {
                //  すでに組込済みの場合、(アバター名)_(数字)で作れるようになるまでループ回す
                int suffix = 1;
                while (HasGeneratedFiles(suffix)) suffix++;
                _avatarNameWithSuffix += "_" + suffix.ToString();
            }
        }

        /// <summary>
        /// コントローラを編集してMA Prefabに差し込みavatar直下に設置
        /// </summary>
        /// <param name="shouldInheritOriginalAnimation">bool 歩行アニメーションの継承</param>
        /// <param name="disableJumpMotion">bool ジャンプモーションの無効</param>
        /// <param name="enableJumpAtDesktop">bool デスクトップでジャンプモーションを有効化</param>
        /// <param name="sittingPose1">SittingPose 座りポーズ1</param>
        /// <param name="sittingPose2">SittingPose 座りポーズ2</param>
        public void CreateMAPrefab(
            bool shouldInheritOriginalAnimation = true,
            bool disableJumpMotion = true,
            bool enableJumpAtDesktop = true,
            SittingPose sittingPose1 = SittingPose.Petan,
            SittingPose sittingPose2 = SittingPose.TatehizaGirl
        )
        {
            if (!CanCombine)
            {
                Debug.LogError("[VRCSupine] Could not create MA Prefab.");
                return;
            }

            // IncrementCurrentGroupを挟まないと、直前のユーザー操作のUndoグループに
            // この組込がまるごと融合してしまい、Ctrl+Zが1操作分で収まらなくなる
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create Supine MA Prefab");

            // オプションに従ってLocomotionを編集
            AnimatorController supineLocomotion = CopyAssetFromGuid<AnimatorController>(_variant.controller);

            if (shouldInheritOriginalAnimation)
            {
                AnimatorController originalLocomotion = _avatarDescriptor.baseAnimationLayers[0].animatorController as AnimatorController;
                InheritOriginalAnimation(supineLocomotion, originalLocomotion);
            }
            ToggleJumpMotion(supineLocomotion, !disableJumpMotion, enableJumpAtDesktop);
            SetSittingAnimations(supineLocomotion, sittingPose1, sittingPose2);

            // 設置済みのごろ寝システムMA Prefabを、新しいものを置く前に集めておく
            List<GameObject> oldPrefabs = FindPlacedSupinePrefabs();

            // MA Prefabを設置＆編集したLocomotionを差す
            string maPrefabPath = AssetDatabase.GUIDToAssetPath(_variant.prefab);
            GameObject maPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(maPrefabPath);
            GameObject maPrefabInstance = PrefabUtility.InstantiatePrefab(maPrefab, _avatar.transform) as GameObject;
            Undo.RegisterCreatedObjectUndo(maPrefabInstance, "Create Supine MA Prefab");

            ModularAvatarMergeAnimator component = maPrefabInstance.GetComponents<ModularAvatarMergeAnimator>()[0];
            component.animator = supineLocomotion;
            EditorUtility.SetDirty(component);

            // 設置済みのMA Prefabを整理
            SortAndCleanMAPrefabs(maPrefabInstance, oldPrefabs);

            // 結合済みの古いごろ寝システムを削除
            OldSupineCleaner.CleanCombinedSupine(_avatarDescriptor);

            EditorSceneManager.MarkSceneDirty(_avatar.scene);
            Undo.CollapseUndoOperations(undoGroup);

            Debug.Log("[VRCSupine] MA Prefab creation is done.");
        }

        /// <summary>
        /// 歩行モーションを継承する
        /// </summary>
        /// <param name="supineLocomotion">ごろ寝システムのBaseコントローラ</param>
        /// <param name="originalLocomotion">オリジナルのBaseコントローラ</param>
        private void InheritOriginalAnimation(AnimatorController supineLocomotion, AnimatorController originalLocomotion)
        {
            // 元のLocomotionが無ければ何もしない
            if (originalLocomotion == null) return;

            // statesを取り出し
            ChildAnimatorState[] supineLocomotionStates = supineLocomotion.layers[0].stateMachine.states;
            ChildAnimatorState[] originalLocomotionStates = originalLocomotion.layers[0].stateMachine.states;

            // モーション上書き
            foreach (string stateName in new[] { "Standing", "Crouching", "Prone" })
            {
                AnimatorState original = AnimatorStateUtility.FindAnimatorStateByName(originalLocomotionStates, stateName);
                AnimatorState supine   = AnimatorStateUtility.FindAnimatorStateByName(supineLocomotionStates, stateName);
                if (original != null && supine != null)
                {
                    supine.motion = original.motion;
                }
            }
        }

        /// <summary>
        /// ジャンプモーションの有効・無効を切り替える
        /// </summary>
        /// <param name="supineLocomotion">ごろ寝システムのBaseコントローラ</param>
        /// <param name="enableJump">bool ジャンプを有効にするか</param>
        /// <param name="enableJumpAtDesktop">bool デスクトップでジャンプを有効にするか</param>
        private void ToggleJumpMotion(AnimatorController supineLocomotion, bool enableJump, bool enableJumpAtDesktop)
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
        }

        /// <summary>
        /// 座りモーションの設定
        /// </summary>
        /// <param name="supineLocomotion">ごろ寝システムのBaseコントローラ</param>
        /// <param name="sittingPose1">SittingPose 座りポーズ1</param>
        /// <param name="sittingPose2">SittingPose 座りポーズ2</param>
        private void SetSittingAnimations(AnimatorController supineLocomotion, SittingPose sittingPose1, SittingPose sittingPose2)
        {
            // statesを取り出し
            ChildAnimatorState[] supineLocomotionStates = supineLocomotion.layers[0].stateMachine.states;

            // 座りアニメーションを変更
            SetSittingAnimation(supineLocomotionStates, "Sit 1", sittingPose1);
            SetSittingAnimation(supineLocomotionStates, "Sit 2", sittingPose2);
        }

        private void SetSittingAnimation(ChildAnimatorState[] states, string stateName, SittingPose pose)
        {
            AnimatorState state = AnimatorStateUtility.FindAnimatorStateByName(states, stateName);
            if (state == null)
            {
                Debug.LogWarning("[VRCSupine] Could not find the state '" + stateName + "' in the locomotion controller.");
                return;
            }

            // 座りアニメーションはバリアント共通のため、ごろ寝システム本体の guids.json から引く
            string guid = SittingPoseTable.GetAnimationGuid(pose, JsonHelper.GetGuidList().animations.sitting);
            state.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
        }

        /// <summary>
        /// アバター直下から設置済みのごろ寝システムMA Prefabを探す。
        /// バリアント名を直接知らなくても、SupineMASlotの有無だけで判定する。
        /// SupineMASlot導入以前に設置された、マーカーの無い残骸も併せて拾う（互換対応）。
        /// </summary>
        private List<GameObject> FindPlacedSupinePrefabs()
        {
            List<GameObject> found = new List<GameObject>();
            foreach (Transform child in _avatar.transform)
            {
                if (child.GetComponent<SupineMASlot>() != null ||
                    OldSupineCleaner.IsMarkerlessMAPrefab(child))
                {
                    found.Add(child.gameObject);
                }
            }
            return found;
        }

        /// <summary>
        /// 新しいMA Prefabを古いMA Prefabの位置へ移し、古いものを削除する。
        /// 他バリアント（EX⇔通常版など）の入れ替えも行う。
        /// </summary>
        /// <param name="newPrefab">新しいMA Prefab</param>
        /// <param name="oldPrefabs">設置済みだったMA Prefab</param>
        private void SortAndCleanMAPrefabs(GameObject newPrefab, List<GameObject> oldPrefabs)
        {
            if (oldPrefabs.Count == 0) return;

            // 元の並び順を保つため、最も手前にあったものの位置を引き継ぐ
            int siblingIndex = int.MaxValue;
            foreach (GameObject oldPrefab in oldPrefabs)
            {
                siblingIndex = Mathf.Min(siblingIndex, oldPrefab.transform.GetSiblingIndex());
            }
            Undo.SetSiblingIndex(newPrefab.transform, siblingIndex, "Sort Supine MA Prefab");

            foreach (GameObject oldPrefab in oldPrefabs)
            {
                Undo.DestroyObjectImmediate(oldPrefab);
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
            string destinationPath = MakeGeneratedDirPath() + "/" + _avatarNameWithSuffix + "_" + templateName;

            return AssetPathUtility.CopyAssetFromPath<T>(templatePath, destinationPath);
        }

        /// <summary>
        /// 生成したごろ寝システムコントローラを置くディレクトリパスを作成
        /// </summary>
        /// <param name="suffix">int 後ろにつける数字</param>
        private string MakeGeneratedDirPath(int suffix = 0)
        {
            string generatedDirPath = MmmAssetPath + '/' + _versionFolderName + "/Generated";
            if (suffix > 0) {
                return generatedDirPath + "/" + _avatarNameWithSuffix + "_" + suffix.ToString();
            }
            else
            {
                return generatedDirPath + "/" + _avatarNameWithSuffix;
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
