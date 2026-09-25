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
        private readonly SupineTemplate _template;
        private readonly string _versionFolderName;
        private readonly string _avatarName;

        public bool CanCombine { get; private set; } = true;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="avatar">GameObject アバター</param>
        /// <param name="versionFolderName">string 生成先フォルダ名（例: "Supine v4.5.1"）</param>
        public SupineCombiner(GameObject avatar, string versionFolderName)
        {
            _avatar = avatar;
            _avatarName = AssetPathUtility.SanitizeFileName(avatar.name);
            _avatarDescriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            _template = JsonHelper.GetGuidList().template;
            _versionFolderName = versionFolderName;

            if (_avatarDescriptor == null)
            {
                // avatar descriptorがなければエラー
                Debug.LogError("[VRCSupine] Could not find VRCAvatarDescriptor.");
                CanCombine = false;
            }
            else if (!_template.IsValid)
            {
                // guids.jsonが読めていない、または内容が欠けている
                Debug.LogError("[VRCSupine] Could not resolve the Supine template assets. Check guids.json.");
                CanCombine = false;
            }
        }

        /// <summary>
        /// 組込前の検証を行う。
        /// 警告があっても組込自体は続行できるため、失敗と警告は分けて返す。
        /// </summary>
        /// <param name="options">SupineCombineOptions 組込オプション</param>
        public SupineCheckResult Validate(SupineCombineOptions options)
        {
            return new SupineCombineValidator(_avatarDescriptor).Validate(options);
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

            // ここから先は生成物のステートを名前で引くので、実名への対応表を1つだけ作って回す
            StateNameMap stateNames = new StateNameMap(options, renamedStates);

            SetSittingAnimations(supineLocomotion, options.sittingPose1, options.sittingPose2, stateNames);

            // プロジェクトに置かれたポーズパックを差し込む。
            // パックが1つも無ければ何も起きないため、従来どおりの生成物になる
            List<string> posePackWarnings = new List<string>();
            List<SupinePosePack> packs = PosePack.SupinePosePackRegistry.CollectPacks(posePackWarnings);
            List<PosePack.ResolvedPose> injectedPoses = InjectPosePacks(
                supineLocomotion, packs, stateNames, posePackWarnings);

            // しゃがみポーズの切り替えを組み込む。ポーズとは別の軸なので、パックが1つも無くても効く。
            // 既存の切り替えを活かすなら、コントローラには手を入れない
            PosePack.SupineCrouchInjection crouch = options.keepExistingCrouchPose
                ? null
                : PosePack.SupineCrouchInjector.Inject(
                    supineLocomotion, packs, stateNames, options, posePackWarnings);

            // コントローラへの変更はここまで。しゃがみの根ツリーも子アセットとして抱かせ終えている
            EditorUtility.SetDirty(supineLocomotion);
            AssetDatabase.SaveAssets();

            // ここからはシーン上のMA Prefabだけを触る。
            // 設置済みのごろ寝システムMA Prefabを、新しいものを置く前に集めておく
            List<GameObject> oldPrefabs = FindPlacedSupinePrefabs();

            // MA Prefabを設置＆編集したLocomotionを差す
            string maPrefabPath = AssetDatabase.GUIDToAssetPath(_template.prefab);
            GameObject maPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(maPrefabPath);
            GameObject maPrefabInstance = PrefabUtility.InstantiatePrefab(maPrefab, _avatar.transform) as GameObject;
            Undo.RegisterCreatedObjectUndo(maPrefabInstance, "Create Supine MA Prefab");

            ModularAvatarMergeAnimator component = maPrefabInstance.GetComponents<ModularAvatarMergeAnimator>()[0];
            component.animator = supineLocomotion;

            // どちらのモードでも、生成物はBaseレイヤーそのものになる。
            // 従来モードはごろ寝システムのLocomotion一式、追加モードはアバターのBaseレイヤーを丸ごと含むので、
            // 追記(Append)にすると元のレイヤーと二重に走ってしまう。
            // 従来モードでこれをやると、元のアニメーターに残ったJumpAndFallが
            // EnableJumpMotionで塞がれないまま動き、ジャンプ・落下の無効化が効かなくなる。
            // Replaceにすると元のレイヤー順・マスク・レイヤー参照がそのまま生成物側で保たれる。
            component.mergeAnimatorMode = MergeAnimatorMode.Replace;

            EditorUtility.SetDirty(component);

            BuildMenus(maPrefabInstance, options, injectedPoses, crouch, posePackWarnings);

            foreach (string warning in posePackWarnings)
            {
                Debug.LogWarning("[VRCSupine] " + warning);
            }

            // 設置済みのMA Prefabを整理
            SortAndCleanMAPrefabs(maPrefabInstance, oldPrefabs);

            // 結合済みの古いごろ寝システムを削除
            OldSupineCleaner.CleanCombinedSupine(_avatarDescriptor);

            EditorSceneManager.MarkSceneDirty(_avatar.scene);
            Undo.CollapseUndoOperations(undoGroup);

            Debug.Log("[VRCSupine] MA Prefab creation is done.");
        }

        /// <summary>
        /// 設置したMA Prefabのメニューを、コントローラへの組込結果に合わせる。
        ///
        /// 順番に意味がある。トップレベルのページ送りは末尾の項目（Crouch Poses など）を
        /// 各ページへ複製するので、しゃがみのメニューが出来上がってから最後に畳む。
        /// 先に畳むと、複製にパックのしゃがみが入らず、既存を優先したときも1枚目しか消えない。
        /// </summary>
        private static void BuildMenus(
            GameObject maPrefabInstance,
            SupineCombineOptions options,
            List<PosePack.ResolvedPose> injectedPoses,
            PosePack.SupineCrouchInjection crouch,
            List<string> warnings)
        {
            // コントローラ側の採番とここが同じ並びを使うので、値の対応がずれない
            PosePack.SupinePoseMenuBuilder.Build(maPrefabInstance, injectedPoses, warnings);

            if (options.keepExistingCrouchPose)
            {
                PosePack.SupineCrouchMenuBuilder.Remove(maPrefabInstance);
            }
            else if (crouch != null)
            {
                PosePack.SupineCrouchMenuBuilder.Build(maPrefabInstance, crouch, warnings);
            }

            PosePack.SupinePoseMenuBuilder.PaginateRoot(maPrefabInstance, warnings);
        }

        /// <summary>
        /// 従来方式。ごろ寝システムのテンプレートをコピーして使う。
        /// </summary>
        private AnimatorController BuildStandardLocomotion(SupineCombineOptions options)
        {
            AnimatorController supineLocomotion = CopyAssetFromGuid<AnimatorController>(_template.controller);

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

            AnimatorController template = _template.LoadController();
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
            bool foundEnableJump = AnimatorParameterUtility.SetDefaultBool(
                supineLocomotion, SupineNames.Parameters.EnableJumpMotion, enableJump);
            bool foundEnableJumpAtDesktop = AnimatorParameterUtility.SetDefaultBool(
                supineLocomotion, SupineNames.Parameters.EnableJumpAtDesktop, enableJumpAtDesktop);

            // パラメータが見つからなければ、オプションは黙って無視されたことになる。
            // テンプレートの作りが変わったときに気付けるよう、必ず声を上げる
            if (!foundEnableJump || !foundEnableJumpAtDesktop)
            {
                Debug.LogWarning(
                    "[VRCSupine] Could not find the jump parameters in the generated controller" +
                    (foundEnableJump ? "" : " (EnableJumpMotion)") +
                    (foundEnableJumpAtDesktop ? "" : " (EnableJumpAtDesktop)") +
                    ". The jump and fall options had no effect.");
            }
        }

        /// <summary>
        /// プロジェクト内のポーズパックを集め、コントローラへ差し込む。
        ///
        /// パッケージからアセットは参照できないため、こちらがパックを知ることはできない。
        /// AssetDatabaseから拾う形にして、依存の向きを一方向に保つ。
        /// </summary>
        /// <returns>差し込めたポーズの一覧。メニュー生成が同じ並びを使う</returns>
        private List<PosePack.ResolvedPose> InjectPosePacks(
            AnimatorController supineLocomotion, IReadOnlyList<SupinePosePack> packs,
            StateNameMap stateNames, List<string> warnings)
        {
            List<PosePack.ResolvedPose> resolved =
                PosePack.SupinePosePackRegistry.Resolve(packs, supineLocomotion, warnings);

            if (resolved.Count == 0) return resolved;

            return new PosePack.SupinePoseInjector(supineLocomotion, stateNames, warnings).Inject(resolved);
        }

        /// <summary>
        /// 座りモーションの設定
        /// </summary>
        /// <param name="supineLocomotion">ごろ寝システムのBaseコントローラ</param>
        /// <param name="sittingPose1">SittingPose 座りポーズ1</param>
        /// <param name="sittingPose2">SittingPose 座りポーズ2</param>
        /// <param name="stateNames">生成物での実名への対応表</param>
        private void SetSittingAnimations(
            AnimatorController supineLocomotion,
            SittingPose sittingPose1,
            SittingPose sittingPose2,
            StateNameMap stateNames)
        {
            // statesを取り出し
            ChildAnimatorState[] supineLocomotionStates = supineLocomotion.layers[0].stateMachine.states;

            // 座りアニメーションを変更
            SetSittingAnimation(supineLocomotionStates, stateNames.Resolve(SupineNames.States.Sit1), sittingPose1);
            SetSittingAnimation(supineLocomotionStates, stateNames.Resolve(SupineNames.States.Sit2), sittingPose2);
        }

        private void SetSittingAnimation(ChildAnimatorState[] states, string stateName, SittingPose pose)
        {
            AnimatorState state = AnimatorStateUtility.FindAnimatorStateByName(states, stateName);
            if (state == null)
            {
                Debug.LogWarning("[VRCSupine] Could not find the state '" + stateName + "' in the locomotion controller.");
                return;
            }

            string guid = SittingPoseTable.GetAnimationGuid(pose, JsonHelper.GetGuidList().animations.sitting);
            state.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
        }

        /// <summary>
        /// アバター直下から設置済みのごろ寝システムMA Prefabを探す。
        /// Prefab名を直接知らなくても、SupineMASlotの有無だけで判定する。
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
        /// 5.0より前のEX版など、古い版のMA Prefabもここで入れ替える。
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

            // 組み込むたびにフォルダごと増えると探しにくいので、
            // フォルダはアバターごとに1つに固定して、同名になるときだけファイル名に連番を足す
            string destinationPath = AssetPathUtility.MakeUniqueAssetPath(
                MakeGeneratedDirPath() + "/" + _avatarName + "_" + templateName);

            return AssetPathUtility.CopyAssetFromPath<T>(templatePath, destinationPath);
        }

        /// <summary>
        /// 生成したごろ寝システムコントローラを置くディレクトリパスを作成。
        /// アバターごとに1つで、組み込み直しても増えない。
        /// </summary>
        private string MakeGeneratedDirPath()
        {
            return MmmAssetPath + '/' + _versionFolderName + "/Generated/" + _avatarName;
        }
    }
}
