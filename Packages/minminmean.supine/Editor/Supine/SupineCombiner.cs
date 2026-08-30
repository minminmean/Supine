using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using VRC.SDK3.Avatars.Components;

using ModularAvatarMergeAnimator = nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator;
using MergeAnimatorMode = nadena.dev.modular_avatar.core.MergeAnimatorMode;
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
        /// <param name="versionFolderName">string 生成先フォルダ名（例: "Supine v4.5.0"）</param>
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
        /// 組込前の検証を行う。
        /// 警告があっても組込自体は続行できるため、失敗と警告は分けて返す。
        /// </summary>
        /// <param name="options">SupineCombineOptions 組込オプション</param>
        public SupineCheckResult Validate(SupineCombineOptions options)
        {
            return new SupineCombineValidator(_avatarDescriptor, _variant).Validate(options);
        }

        /// <summary>
        /// コントローラを編集してMA Prefabに差し込みavatar直下に設置
        /// </summary>
        /// <param name="options">SupineCombineOptions 組込オプション</param>
        public void CreateMAPrefab(SupineCombineOptions options)
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

            // オプションに従ってLocomotionを用意
            IReadOnlyDictionary<string, string> renamedStates = new Dictionary<string, string>();
            AnimatorController supineLocomotion = options.mode == SupineCombineMode.Add
                ? BuildAddedLocomotion(options, out renamedStates)
                : BuildStandardLocomotion(options);

            if (supineLocomotion == null)
            {
                Debug.LogError("[VRCSupine] Could not create MA Prefab.");
                return;
            }

            // ジャンプのオプションはごろ寝システムのアニメーターに手を入れるためのもの。
            // 追加モードでは既存のジャンプ・落下の挙動をそのまま残すので触らない。
            if (options.ShouldApplyJumpOptions)
            {
                ToggleJumpMotion(supineLocomotion, !options.disableJumpMotion, options.enableJumpAtDesktop);
            }
            SetSittingAnimations(supineLocomotion, options.sittingPose1, options.sittingPose2, renamedStates);

            EditorUtility.SetDirty(supineLocomotion);
            AssetDatabase.SaveAssets();

            // 設置済みのごろ寝システムMA Prefabを、新しいものを置く前に集めておく
            List<GameObject> oldPrefabs = FindPlacedSupinePrefabs();

            // MA Prefabを設置＆編集したLocomotionを差す
            string maPrefabPath = AssetDatabase.GUIDToAssetPath(_variant.prefab);
            GameObject maPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(maPrefabPath);
            GameObject maPrefabInstance = PrefabUtility.InstantiatePrefab(maPrefab, _avatar.transform) as GameObject;
            Undo.RegisterCreatedObjectUndo(maPrefabInstance, "Create Supine MA Prefab");

            ModularAvatarMergeAnimator component = maPrefabInstance.GetComponents<ModularAvatarMergeAnimator>()[0];
            component.animator = supineLocomotion;

            // 追加モードの生成物はアバターのBaseレイヤーを丸ごと含むため、
            // 追記(Append)にすると元のレイヤーと二重に走ってしまう。
            // Replaceにすると元のレイヤー順・マスク・レイヤー参照がそのまま生成物側で保たれる。
            component.mergeAnimatorMode = options.mode == SupineCombineMode.Add
                ? MergeAnimatorMode.Replace
                : MergeAnimatorMode.Append;

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
        /// 従来方式。ごろ寝システムのテンプレートをコピーして使う。
        /// </summary>
        private AnimatorController BuildStandardLocomotion(SupineCombineOptions options)
        {
            AnimatorController supineLocomotion = CopyAssetFromGuid<AnimatorController>(_variant.controller);

            if (options.ShouldInherit)
            {
                InheritOriginalAnimation(
                    supineLocomotion, BaseAnimatorResolver.FindBaseLayerController(_avatarDescriptor), options);
            }

            return supineLocomotion;
        }

        /// <summary>
        /// 追加方式。既存アニメーターのコピーへ、ごろ寝システムのステート群を追加する。
        /// </summary>
        private AnimatorController BuildAddedLocomotion(
            SupineCombineOptions options, out IReadOnlyDictionary<string, string> renamedStates)
        {
            renamedStates = new Dictionary<string, string>();

            BaseAnimatorResolution resolution =
                BaseAnimatorResolver.Resolve(_avatarDescriptor, options.EffectiveAddTargetOverride);

            if (!resolution.IsValid)
            {
                Debug.LogError("[VRCSupine] Could not resolve the animator to add the Supine states to.");
                return null;
            }

            if (resolution.source == BaseAnimatorSource.VrcDefault)
            {
                Debug.Log(
                    "[VRCSupine] The avatar has no animator on its Base layer. " +
                    "Using the VRChat default locomotion as the target.");
            }

            AnimatorController template = _variant.LoadController();
            if (template == null)
            {
                Debug.LogError("[VRCSupine] Could not load the Supine template controller.");
                return null;
            }

            // 元のアセットは絶対に書き換えない。必ずコピーへ追加する
            AnimatorController generated = CopyAssetFromController(resolution.controller);

            SupineAddReport report = new SupineLocomotionAdder(
                template, generated, SupineLocomotionAdder.BuildStateNameOverrides(options)).Add();
            foreach (string warning in report.Warnings)
            {
                Debug.LogWarning("[VRCSupine] " + warning);
            }

            if (!report.Succeeded)
            {
                Debug.LogError("[VRCSupine] Could not add the Supine states to the target animator.");

                // 中途半端なコピーを残すと、生成先フォルダが埋まって次回の連番がずれる
                DiscardGeneratedAsset(generated);
                return null;
            }

            renamedStates = report.RenamedStates;
            return generated;
        }

        /// <summary>
        /// 生成に失敗したアセットを片付ける。空になった生成先フォルダも畳む。
        /// </summary>
        private void DiscardGeneratedAsset(Object asset)
        {
            if (asset == null) return;

            string generatedDirPath = MakeGeneratedDirPath();
            string assetPath = AssetDatabase.GetAssetPath(asset);

            // 生成先フォルダの中身以外は絶対に消さない
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith(generatedDirPath + "/")) return;

            AssetDatabase.DeleteAsset(assetPath);

            if (AssetDatabase.IsValidFolder(generatedDirPath) &&
                AssetDatabase.FindAssets(string.Empty, new[] { generatedDirPath }).Length == 0)
            {
                AssetDatabase.DeleteAsset(generatedDirPath);
            }
        }

        /// <summary>
        /// 歩行モーションを継承する。
        /// 継承元のステートは名前一致で探すが、オプションで指定があればそちらを優先する。
        /// </summary>
        /// <param name="supineLocomotion">ごろ寝システムのBaseコントローラ</param>
        /// <param name="originalLocomotion">オリジナルのBaseコントローラ</param>
        /// <param name="options">SupineCombineOptions 組込オプション</param>
        private void InheritOriginalAnimation(
            AnimatorController supineLocomotion, AnimatorController originalLocomotion, SupineCombineOptions options)
        {
            // 元のLocomotionが無ければ何もしない
            if (originalLocomotion == null) return;
            if (originalLocomotion.layers.Length == 0 || originalLocomotion.layers[0].stateMachine == null) return;

            ChildAnimatorState[] supineLocomotionStates = supineLocomotion.layers[0].stateMachine.states;

            // 継承元はサブステートマシンに入っていることもあるので再帰的に引く
            Dictionary<string, AnimatorState> originalStates =
                AnimatorStateUtility.BuildStateIndex(originalLocomotion.layers[0].stateMachine);

            // モーション上書き
            foreach (string templateStateName in InheritedStateTable.TemplateStateNames)
            {
                AnimatorState supine =
                    AnimatorStateUtility.FindAnimatorStateByName(supineLocomotionStates, templateStateName);
                if (supine == null) continue;

                // 指定が空なら継承しない。ごろ寝システムに元から入っているアニメーションが残る
                if (!InheritedStateTable.TryResolveSourceStateName(
                        options, templateStateName, out string sourceStateName)) continue;

                if (!originalStates.TryGetValue(sourceStateName, out AnimatorState original)) continue;

                supine.motion = original.motion;
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
        /// <param name="renamedStates">追加時にリネームされたステートの対応表</param>
        private void SetSittingAnimations(
            AnimatorController supineLocomotion,
            SittingPose sittingPose1,
            SittingPose sittingPose2,
            IReadOnlyDictionary<string, string> renamedStates)
        {
            // statesを取り出し
            ChildAnimatorState[] supineLocomotionStates = supineLocomotion.layers[0].stateMachine.states;

            // 座りアニメーションを変更
            SetSittingAnimation(supineLocomotionStates, ResolveStateName("Sit 1", renamedStates), sittingPose1);
            SetSittingAnimation(supineLocomotionStates, ResolveStateName("Sit 2", renamedStates), sittingPose2);
        }

        /// <summary>
        /// 追加時に名前が衝突してリネームされている場合、生成物での実名を返す
        /// </summary>
        private static string ResolveStateName(string name, IReadOnlyDictionary<string, string> renamedStates)
        {
            if (renamedStates != null && renamedStates.TryGetValue(name, out string renamed)) return renamed;
            return name;
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
            return CopyAssetFrom<T>(templatePath);
        }

        /// <summary>
        /// 追加先コントローラを生成先フォルダへコピーする
        /// </summary>
        private AnimatorController CopyAssetFromController(AnimatorController source)
        {
            return CopyAssetFrom<AnimatorController>(AssetDatabase.GetAssetPath(source));
        }

        private T CopyAssetFrom<T>(string templatePath) where T : Object
        {
            string templateName = AssetPathUtility.SanitizeFileName(Path.GetFileName(templatePath));
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
