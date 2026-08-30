using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using VRC.SDK3.Avatars.Components;
using Supine.Utilities;

namespace Supine
{
    /// <summary>
    /// ごろ寝システム組込ウィンドウの共通実装。
    ///
    /// 通常版とEX版はこのクラスを継承した「兄弟」として実装する。
    /// EditorWindow.GetWindow&lt;T&gt;()は派生型のインスタンスも拾ってしまうため、
    /// 一方をもう一方の親にすると、EX版を開いている状態で通常版を開こうとしたときに
    /// EX版のウィンドウがフォーカスされてしまう。
    /// バリアント固有の情報はVariant / FolderLabel / PrefsKeyPrefixの3つに集約する。
    /// </summary>
    public abstract class SupineCombinerWindowBase : EditorWindow
    {
        /// <summary>設置するPrefabとコントローラのGUID。自パッケージのguids.jsonから取る</summary>
        protected abstract SupineVariant Variant { get; }

        /// <summary>生成先フォルダ名の接頭辞（例: "Supine"）</summary>
        protected abstract string FolderLabel { get; }

        /// <summary>EditorPrefsのキー接頭辞。バリアント間で設定が混ざらないよう分ける</summary>
        protected abstract string PrefsKeyPrefix { get; }

        /// <summary>
        /// ウィンドウの最小サイズ。
        /// 日本語・英語それぞれで一番長いラベルとヘルプが切れずに収まる大きさを実測して決めている
        /// （幅は英語の add_target_auto と日本語の結合方法Popup、
        /// 高さは競合と組込済みの警告が両方出た追加モードが効いている）。
        /// </summary>
        private static readonly Vector2 WindowMinSize = new Vector2(420f, 500f);

        private GameObject _avatar;
        private SupineCombiner _supineCombiner;

        // 追加先アニメーターから読み取った内容。毎フレーム作り直すと重いのでキャッシュする
        private AnimatorController _cachedAddTarget;
        private string[] _addTargetStateNames = new string[0];
        private bool _addTargetAlreadyCombined;
        private string _cachedLieDownEntryStateName;
        private List<string> _lieDownDestinationNames = new List<string>();

        // 継承元アニメーターのステート一覧
        private AnimatorController _cachedInheritSource;
        private string[] _inheritSourceStateNames = new string[0];

        // アニメーターの中身が変わっていても参照が同じだと気付けないので、
        // ウィンドウに戻ってきたら読み直す。世代が食い違うキャッシュだけを作り直す
        private int _refreshGeneration = 1;
        private int _addTargetGeneration;
        private int _inheritSourceGeneration;

        private SupineLanguage _language = SupineLanguage.Japanese;

        private bool _canCombine = false;

        /// <summary>既定値の重複管理を避けるため、オプションはこの1つにまとめて持つ</summary>
        private SupineCombineOptions _options = SupineCombineOptions.Default;

        /// <summary>
        /// 生成先フォルダ名。バージョンはpackage.jsonを唯一の情報源とする。
        /// GetType()は派生ウィンドウの型を返すため、EX版ならEXパッケージのアセンブリが解決される。
        /// </summary>
        private string VersionFolderName
        {
            get
            {
                string version = PackageInfo.FindForAssembly(GetType().Assembly)?.version;
                if (string.IsNullOrEmpty(version))
                {
                    Debug.LogWarning(
                        "[VRCSupine] Could not resolve the package version of " + GetType().Name +
                        ". Falling back to a folder name without a version.");
                    return FolderLabel;
                }
                return FolderLabel + " v" + version;
            }
        }

        protected virtual void OnEnable()
        {
            minSize = WindowMinSize;
            LoadPrefs();
        }

        /// <summary>
        /// Animatorウィンドウで編集してから戻ってきた場合に備えて、ステート一覧を読み直させる。
        /// </summary>
        private void OnFocus()
        {
            _refreshGeneration++;
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();

            LocalizeDictionary localizeDict = DrawLanguageSelector();

            EditorGUILayout.Space();

            DrawAvatarField(localizeDict);

            EditorGUILayout.Space();

            DrawCombineMode(localizeDict);

            EditorGUILayout.Space();

            DrawSittingPoses(localizeDict);

            EditorGUILayout.Space();

            DrawButtons(localizeDict);

            if (EditorGUI.EndChangeCheck())
            {
                SavePrefs();
            }
        }

        private LocalizeDictionary DrawLanguageSelector()
        {
            // 言語選択＆辞書取得
            string[] languages = { "Japanese", "English" };
            _language = (SupineLanguage)EditorGUILayout.Popup("Language", (int)_language, languages);
            return JsonHelper.GetLocalizedTexts(_language);
        }

        private void DrawAvatarField(LocalizeDictionary localizeDict)
        {
            // アバター取得
            using (new GUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                _avatar = EditorGUILayout.ObjectField(localizeDict.avatar, _avatar, typeof(GameObject), true) as GameObject;

                if (EditorGUI.EndChangeCheck())
                {
                    _canCombine = false;
                }
            }
        }

        /// <summary>
        /// 結合方法の選択と、そのモードでだけ意味を持つオプション。
        /// モードごとに使う項目がまったく違うので、無効化ではなく表示の切り替えにする。
        /// 表示していない側の値は保持するため、モードを行き来しても設定は失われない。
        /// </summary>
        private void DrawCombineMode(LocalizeDictionary localizeDict)
        {
            EditorGUI.BeginChangeCheck();

            string[] modes = SupineCombineModeTable.GetLabels(localizeDict);
            _options.mode = SupineCombineModeTable.FromIndex(
                EditorGUILayout.Popup(localizeDict.combine_mode, SupineCombineModeTable.IndexOf(_options.mode), modes));

            if (EditorGUI.EndChangeCheck())
            {
                // 検証していない構成のまま生成できてしまわないようにする
                _canCombine = false;
            }

            EditorGUI.indentLevel++;

            if (_options.mode == SupineCombineMode.Standard)
            {
                DrawStandardOptions(localizeDict);
            }
            else
            {
                DrawAddOptions(localizeDict);
            }

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// 従来モードのオプション。継承とジャンプはごろ寝システムのアニメーターを編集するためのもの。
        /// </summary>
        private void DrawStandardOptions(LocalizeDictionary localizeDict)
        {
            EditorGUI.BeginChangeCheck();

            // 元の立ち、しゃがみ、伏せアニメーションを継承するか
            _options.shouldInheritOriginalAnimation = EditorGUILayout.ToggleLeft(
                localizeDict.inherit_original, _options.shouldInheritOriginalAnimation);

            DrawInheritSourceStates(localizeDict);

            // ジャンプモーションを無効にするか
            _options.disableJumpMotion = EditorGUILayout.ToggleLeft(
                localizeDict.disable_jump_motion, _options.disableJumpMotion);

            using (new EditorGUI.DisabledGroupScope(!_options.disableJumpMotion))
            {
                EditorGUI.indentLevel++;
                _options.enableJumpAtDesktop = EditorGUILayout.ToggleLeft(
                    localizeDict.enable_jump_at_desktop, _options.enableJumpAtDesktop);
                if (!_options.disableJumpMotion)
                {
                    _options.enableJumpAtDesktop = false;
                }
                EditorGUI.indentLevel--;
            }

            if (EditorGUI.EndChangeCheck())
            {
                _canCombine = false;
            }
        }

        /// <summary>
        /// どの既存ステートからモーションを引き継ぐかの選択。
        /// 名前が一致するものを初期選択にし、一致しなければユーザーに選ばせる。
        /// </summary>
        private void DrawInheritSourceStates(LocalizeDictionary localizeDict)
        {
            VRCAvatarDescriptor avatarDescriptor =
                _avatar != null ? _avatar.GetComponent<VRCAvatarDescriptor>() : null;
            RefreshInheritSourceStates(BaseAnimatorResolver.FindBaseLayerController(avatarDescriptor));

            EditorGUI.indentLevel++;

            using (new EditorGUI.DisabledGroupScope(
                !_options.shouldInheritOriginalAnimation || _inheritSourceStateNames.Length == 0))
            {
                string[] display = BuildDisplayNames(_inheritSourceStateNames, localizeDict);

                foreach (string templateStateName in InheritedStateTable.TemplateStateNames)
                {
                    string picked = DrawStateNamePopup(
                        InheritedStateTable.GetLabel(templateStateName, localizeDict),
                        InheritedStateTable.GetSourceStateName(_options, templateStateName),
                        _inheritSourceStateNames, display);

                    InheritedStateTable.SetSourceStateName(ref _options, templateStateName, picked);
                }

                EditorGUILayout.HelpBox(localizeDict.inherit_state_help, MessageType.Info);
            }

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// 追加モードのオプション。
        /// </summary>
        private void DrawAddOptions(LocalizeDictionary localizeDict)
        {
            EditorGUI.BeginChangeCheck();

            _options.addTargetOverride = EditorGUILayout.ObjectField(
                localizeDict.add_target, _options.addTargetOverride,
                typeof(AnimatorController), false) as AnimatorController;

            EditorGUILayout.LabelField(localizeDict.add_target_auto, EditorStyles.miniLabel);

            if (EditorGUI.EndChangeCheck())
            {
                _canCombine = false;
            }

            DrawAddTargetStates(localizeDict);
        }

        /// <summary>
        /// 継承元アニメーターのステート名一覧を作り直す。名前が一致するものを初期選択にする。
        /// </summary>
        private void RefreshInheritSourceStates(AnimatorController source)
        {
            bool sourceChanged = source != _cachedInheritSource;
            if (!sourceChanged && _inheritSourceGeneration == _refreshGeneration) return;

            _inheritSourceGeneration = _refreshGeneration;
            _cachedInheritSource = source;
            _inheritSourceStateNames = BuildStateNames(source);

            foreach (string templateStateName in InheritedStateTable.TemplateStateNames)
            {
                string current = InheritedStateTable.GetSourceStateName(_options, templateStateName);

                // 中身だけ変わった場合は、選んでいたステートが消えていたときだけ選び直す
                if (!sourceChanged && current != null &&
                    (current.Length == 0 || DisplayIndexOf(_inheritSourceStateNames, current) > 0)) continue;

                // 名前が一致すればそれを初期選択に、しなければ「なし」（＝ごろ寝システムのアニメーション）
                InheritedStateTable.SetSourceStateName(
                    ref _options, templateStateName,
                    AutoSelectStateName(_inheritSourceStateNames, templateStateName));
            }
        }

        /// <summary>
        /// 追加先の解決結果と、どのステートをごろ寝システムに使うかの選択。
        ///
        /// 既成のアニメーターはステート名を変えていることが多く、名前一致だけでは拾えない。
        /// 追加先のステート名を一覧で出し、名前が一致するものを初期選択にしたうえで、
        /// 一致しない場合はユーザーが選び直せるようにする。
        /// </summary>
        private void DrawAddTargetStates(LocalizeDictionary localizeDict)
        {
            VRCAvatarDescriptor avatarDescriptor =
                _avatar != null ? _avatar.GetComponent<VRCAvatarDescriptor>() : null;
            BaseAnimatorResolution resolution =
                BaseAnimatorResolver.Resolve(avatarDescriptor, _options.addTargetOverride);

            DrawAddTargetHelp(localizeDict, avatarDescriptor, resolution);
            RefreshAddTargetStates(resolution.controller);

            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();

            using (new EditorGUI.DisabledGroupScope(_addTargetStateNames.Length == 0))
            {
                string[] display = BuildDisplayNames(_addTargetStateNames, localizeDict);

                string previousEntry = _options.entryStateName;
                _options.entryStateName = DrawStateNamePopup(
                    localizeDict.entry_state, _options.entryStateName, _addTargetStateNames, display);

                // 入口が変わったら、そこから伏せへ降りる先を推測して選び直す
                if (_options.entryStateName != previousEntry)
                {
                    _options.proneStateName =
                        InferProneStateName(resolution.controller, _options.entryStateName);
                }

                _options.proneStateName = DrawStateNamePopup(
                    localizeDict.prone_state, _options.proneStateName, _addTargetStateNames, display);

                EditorGUILayout.HelpBox(localizeDict.add_state_help, MessageType.Info);

                RefreshLieDownDestinations(resolution.controller, false);

                if (SupineLocomotionAdder.HasConflictingLieDownDestination(
                        _lieDownDestinationNames, _options.proneStateName))
                {
                    EditorGUILayout.HelpBox(localizeDict.add_state_conflict, MessageType.Warning);
                }

                if (_addTargetAlreadyCombined)
                {
                    EditorGUILayout.HelpBox(localizeDict.add_state_already_combined, MessageType.Warning);
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                _canCombine = false;
            }

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// 追加先のステート名一覧を作り直す。名前が一致するものを初期選択にする。
        /// 一致しなければ「なし」にして、ユーザーに選ばせる。
        /// </summary>
        private void RefreshAddTargetStates(AnimatorController target)
        {
            bool targetChanged = target != _cachedAddTarget;
            if (!targetChanged && _addTargetGeneration == _refreshGeneration) return;

            _addTargetGeneration = _refreshGeneration;
            _cachedAddTarget = target;
            _addTargetStateNames = BuildStateNames(target);
            _addTargetAlreadyCombined =
                SupineLocomotionAdder.IsSupineCombined(Variant.LoadController(), target);

            // 追加先が変わったなら選び直す。中身だけ変わった場合は、
            // 選んでいたステートが消えていたときだけ選び直して、手動の指定を無駄に壊さない
            if (targetChanged || !IsSelectableStateName(_options.entryStateName))
            {
                _options.entryStateName = AutoSelectStateName(
                    _addTargetStateNames, SupineLocomotionAdder.InferEntryStateName(target));
            }
            if (targetChanged || !IsSelectableStateName(_options.proneStateName))
            {
                _options.proneStateName = InferProneStateName(target, _options.entryStateName);
            }

            RefreshLieDownDestinations(target, true);
        }

        /// <summary>一覧から選べる状態か。「なし」も選択として有効</summary>
        private bool IsSelectableStateName(string stateName)
        {
            return stateName != null && (stateName.Length == 0 ||
                DisplayIndexOf(_addTargetStateNames, stateName) > 0);
        }

        /// <summary>
        /// 入口ステートから降りる先を控え直す。競合の判定を毎フレーム走査しないためのキャッシュ。
        /// </summary>
        private void RefreshLieDownDestinations(AnimatorController target, bool force)
        {
            if (!force && _options.entryStateName == _cachedLieDownEntryStateName) return;

            _cachedLieDownEntryStateName = _options.entryStateName;
            _lieDownDestinationNames = SupineLocomotionAdder.CollectLieDownDestinationNames(
                Variant.LoadController(), target, _options.entryStateName);
        }

        /// <summary>
        /// 伏せ状態にあたるステートを推測する。
        ///
        /// 入口ステートから Upright less than で降りる先があれば、それがそのアニメーターの伏せ状態。
        /// ステート名を変えていても拾えるので、名前一致より優先する。
        /// </summary>
        private string InferProneStateName(AnimatorController target, string entryStateName)
        {
            List<AnimatorState> destinations =
                SupineLocomotionAdder.CollectLieDownDestinations(target, entryStateName);

            if (destinations.Count > 0) return destinations[0].name;

            return AutoSelectStateName(_addTargetStateNames, SupineLocomotionAdder.ProneStateName);
        }

        /// <summary>
        /// ステート名のPopupを1つ描く。
        /// </summary>
        /// <returns>選ばれたステート名。「なし」なら空文字</returns>
        private static string DrawStateNamePopup(
            string label, string current, string[] stateNames, string[] displayNames)
        {
            int index = DisplayIndexOf(stateNames, current);
            int picked = EditorGUILayout.Popup(label, index, displayNames);

            return picked == index ? current : StateNameAtDisplayIndex(stateNames, picked);
        }

        private static string[] BuildStateNames(AnimatorController controller)
        {
            if (controller == null || controller.layers.Length == 0 ||
                controller.layers[0].stateMachine == null)
            {
                return new string[0];
            }

            // 追加処理と同じ索引から作る。ここで選んだ名前がそのまま向こうで引ける
            Dictionary<string, AnimatorState> index =
                AnimatorStateUtility.BuildStateIndex(controller.layers[0].stateMachine);

            string[] names = new string[index.Count];
            index.Keys.CopyTo(names, 0);
            return names;
        }

        /// <summary>
        /// Popupに出す並び。先頭に「なし」を足す。
        /// 表示名は言語で変わるので、キャッシュせず毎回組み立てる。
        /// </summary>
        private static string[] BuildDisplayNames(string[] stateNames, LocalizeDictionary localizeDict)
        {
            string[] displayNames = new string[stateNames.Length + 1];
            displayNames[0] = localizeDict.state_none;
            stateNames.CopyTo(displayNames, 1);
            return displayNames;
        }

        /// <summary>名前が一覧にあればそれを、無ければ「なし」を初期選択にする</summary>
        private static string AutoSelectStateName(string[] stateNames, string name)
        {
            return DisplayIndexOf(stateNames, name) > 0 ? name : string.Empty;
        }

        /// <summary>Popupでの位置。空文字は「なし」で先頭、未指定(null)は空欄の-1</summary>
        private static int DisplayIndexOf(string[] stateNames, string name)
        {
            if (name == null) return -1;
            if (name.Length == 0) return 0;

            for (int i = 0; i < stateNames.Length; i++)
            {
                if (stateNames[i] == name) return i + 1;
            }
            return -1;
        }

        private static string StateNameAtDisplayIndex(string[] stateNames, int displayIndex)
        {
            if (displayIndex <= 0 || displayIndex > stateNames.Length) return string.Empty;
            return stateNames[displayIndex - 1];
        }

        /// <summary>
        /// 自動取得の結果を実名で見せる。手動指定時はフィールドが答えなので出さない。
        /// </summary>
        private void DrawAddTargetHelp(
            LocalizeDictionary localizeDict, VRCAvatarDescriptor avatarDescriptor, BaseAnimatorResolution resolution)
        {
            if (_options.addTargetOverride != null) return;
            if (avatarDescriptor == null) return;

            switch (resolution.source)
            {
                case BaseAnimatorSource.AvatarDescriptor:
                    EditorGUILayout.HelpBox(
                        string.Format(localizeDict.add_target_resolved, resolution.controller.name),
                        MessageType.None);
                    break;

                case BaseAnimatorSource.VrcDefault:
                    EditorGUILayout.HelpBox(localizeDict.add_target_vrc_default, MessageType.Info);
                    break;

                default:
                    EditorGUILayout.HelpBox(localizeDict.check_failure_add_target_message, MessageType.Warning);
                    break;
            }
        }

        private void DrawSittingPoses(LocalizeDictionary localizeDict)
        {
            // 座り方選択
            string[] sittingPoses = SittingPoseTable.GetLabels(localizeDict);
            _options.sittingPose1 = SittingPoseTable.FromIndex(
                EditorGUILayout.Popup(localizeDict.sit1, SittingPoseTable.IndexOf(_options.sittingPose1), sittingPoses));
            _options.sittingPose2 = SittingPoseTable.FromIndex(
                EditorGUILayout.Popup(localizeDict.sit2, SittingPoseTable.IndexOf(_options.sittingPose2), sittingPoses));
        }

        private void DrawButtons(LocalizeDictionary localizeDict)
        {
            using (new GUILayout.VerticalScope())
            {
                // Checkボタン
                using (new EditorGUI.DisabledGroupScope(!_avatar))
                {
                    if (GUILayout.Button(localizeDict.check))
                    {
                        RunCheck(localizeDict);
                    }
                }

                // Prefab生成ボタン
                using (new EditorGUI.DisabledGroupScope(!_canCombine))
                {
                    if (GUILayout.Button(localizeDict.create_ma_prefab, GUILayout.Height(40), GUILayout.MinWidth(150)))
                    {
                        TryCreatePrefab(localizeDict);
                    }
                }
            }
        }

        private void RunCheck(LocalizeDictionary localizeDict)
        {
            _supineCombiner = new SupineCombiner(_avatar, Variant, VersionFolderName);
            SupineCheckResult result = _supineCombiner.Validate(_options);

            foreach (string warning in result.Warnings)
            {
                Debug.LogWarning("[VRCSupine] " + warning);
            }

            if (!result.CanCombine)
            {
                _canCombine = false;
                EditorUtility.DisplayDialog(
                    localizeDict.check_failure,
                    SupineCombineFailureTable.GetMessage(result.Failure, localizeDict),
                    "OK");
                Debug.Log("[VRCSupine] Check failed.");
                return;
            }

            _canCombine = true;

            if (result.HasWarnings)
            {
                EditorUtility.DisplayDialog(
                    localizeDict.check_successful_warning, localizeDict.check_successful_warning_message, "OK");
                Debug.Log("[VRCSupine] Check OK with " + result.Warnings.Count + " warning(s).");
            }
            else
            {
                EditorUtility.DisplayDialog(
                    localizeDict.check_successful, localizeDict.check_successful_message, "OK");
                Debug.Log("[VRCSupine] Check OK.");
            }
        }

        private void TryCreatePrefab(LocalizeDictionary localizeDict)
        {
            try
            {
                _supineCombiner.CreateMAPrefab(_options);
            }
            catch (IOException)
            {
                EditorUtility.DisplayDialog(localizeDict.ma_prefab_create_failure, localizeDict.ma_prefab_create_failure_message, "OK");
                throw;
            }
            EditorUtility.DisplayDialog(localizeDict.ma_prefab_created, localizeDict.ma_prefab_created_message, "OK");
            _canCombine = false;
        }

        private void LoadPrefs()
        {
            _language = (SupineLanguage)EditorPrefs.GetInt(PrefsKey("language"), (int)_language);
            _options.mode = (SupineCombineMode)EditorPrefs.GetInt(PrefsKey("combineMode"), (int)_options.mode);
            _options.shouldInheritOriginalAnimation =
                EditorPrefs.GetBool(PrefsKey("inheritOriginal"), _options.shouldInheritOriginalAnimation);
            _options.disableJumpMotion   = EditorPrefs.GetBool(PrefsKey("disableJumpMotion"), _options.disableJumpMotion);
            _options.enableJumpAtDesktop = EditorPrefs.GetBool(PrefsKey("enableJumpAtDesktop"), _options.enableJumpAtDesktop);
            _options.sittingPose1 = (SittingPose)EditorPrefs.GetInt(PrefsKey("sittingPose1"), (int)_options.sittingPose1);
            _options.sittingPose2 = (SittingPose)EditorPrefs.GetInt(PrefsKey("sittingPose2"), (int)_options.sittingPose2);
        }

        private void SavePrefs()
        {
            // 追加先アニメーターは保存しない。
            // EditorPrefsはマシン全体で共有されるため、別プロジェクトのGUIDが幽霊参照として残ってしまう。
            // アバターを保存していないのと同じ方針。
            EditorPrefs.SetInt(PrefsKey("language"), (int)_language);
            EditorPrefs.SetInt(PrefsKey("combineMode"), (int)_options.mode);
            EditorPrefs.SetBool(PrefsKey("inheritOriginal"), _options.shouldInheritOriginalAnimation);
            EditorPrefs.SetBool(PrefsKey("disableJumpMotion"), _options.disableJumpMotion);
            EditorPrefs.SetBool(PrefsKey("enableJumpAtDesktop"), _options.enableJumpAtDesktop);
            EditorPrefs.SetInt(PrefsKey("sittingPose1"), (int)_options.sittingPose1);
            EditorPrefs.SetInt(PrefsKey("sittingPose2"), (int)_options.sittingPose2);
        }

        private string PrefsKey(string name)
        {
            return PrefsKeyPrefix + "." + name;
        }
    }
}
