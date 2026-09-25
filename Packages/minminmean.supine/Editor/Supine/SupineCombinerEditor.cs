using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using Supine.Utilities;

namespace Supine
{
    /// <summary>
    /// ごろ寝システムの組込ウィンドウ
    /// </summary>
    public sealed partial class SupineCombinerEditor : EditorWindow
    {
        /// <summary>生成先フォルダ名の接頭辞</summary>
        private const string FolderLabel = "Supine";

        /// <summary>EditorPrefsのキー接頭辞</summary>
        private const string PrefsKeyPrefix = "MinMinMart.Supine";

        /// <summary>
        /// ウィンドウの最小サイズ。
        /// 日本語・英語それぞれで一番長いラベルとヘルプが切れずに収まる大きさを実測して決めている
        /// （幅は英語の add_target_auto と日本語の結合方法Popup、
        /// 高さは競合と組込済みの警告が両方出た追加モードが効いている）。
        /// </summary>
        private static readonly Vector2 WindowMinSize = new Vector2(420f, 500f);

        private GameObject _avatar;

        /// <summary>_avatar の記述子。描画のたびに1回だけ引き直し、各欄はこれを使う</summary>
        private VRCAvatarDescriptor _avatarDescriptor;

        private SupineCombiner _supineCombiner;

        // アニメーターの中身が変わっていても参照が同じだと気付けないので、
        // ウィンドウに戻ってきたら読み直す。世代が食い違うキャッシュだけを作り直す
        private int _refreshGeneration = 1;

        private SupineLanguage _language = SupineLanguage.Japanese;

        private bool _canCombine = false;

        /// <summary>既定値の重複管理を避けるため、オプションはこの1つにまとめて持つ</summary>
        private SupineCombineOptions _options = SupineCombineOptions.Default;

        /// <summary>
        /// 生成先フォルダ名。バージョンはpackage.jsonを唯一の情報源とする。
        /// </summary>
        private string VersionFolderName
        {
            get
            {
                string version = SupinePackageVersion.Current;
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

        [MenuItem("Tools/MinMinMart/Supine Combiner")]
        private static void Create()
        {
            GetWindow<SupineCombinerEditor>("Supine Combiner");
        }

        private void OnEnable()
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

            // アバターと結合方法まわりは検証結果を左右するので、触ったらチェックからやり直させる。
            // 検証していない構成のまま生成できてしまわないようにする
            EditorGUI.BeginChangeCheck();

            DrawAvatarField(localizeDict);

            EditorGUILayout.Space();

            DrawCombineMode(localizeDict);

            if (EditorGUI.EndChangeCheck())
            {
                _canCombine = false;
            }

            EditorGUILayout.Space();

            // 座り方と既定のしゃがみは検証に関わらないので、チェックをやり直させない
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
                _avatar = EditorGUILayout.ObjectField(localizeDict.avatar, _avatar, typeof(GameObject), true) as GameObject;
            }

            // 記述子は後から付け外しされうるので、キャッシュせず描画のたびに引く
            _avatarDescriptor = _avatar != null ? _avatar.GetComponent<VRCAvatarDescriptor>() : null;
        }

        /// <summary>
        /// 結合方法の選択と、そのモードでだけ意味を持つオプション。
        /// モードごとに使う項目がまったく違うので、無効化ではなく表示の切り替えにする。
        /// 表示していない側の値は保持するため、モードを行き来しても設定は失われない。
        /// </summary>
        private void DrawCombineMode(LocalizeDictionary localizeDict)
        {
            string[] modes = SupineCombineModeTable.GetLabels(localizeDict);
            _options.mode = SupineCombineModeTable.FromIndex(
                EditorGUILayout.Popup(localizeDict.combine_mode, SupineCombineModeTable.IndexOf(_options.mode), modes));

            EditorGUI.indentLevel++;

            if (_options.mode == SupineCombineMode.Standard)
            {
                DrawStandardOptions(localizeDict);
            }
            else
            {
                DrawAddOptions(localizeDict);
            }

            DrawCrouchPoseConflict(localizeDict);

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// 従来モードのオプション。継承とジャンプはごろ寝システムのアニメーターを編集するためのもの。
        /// </summary>
        private void DrawStandardOptions(LocalizeDictionary localizeDict)
        {
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
        }

        /// <summary>
        /// 追加モードのオプション。
        /// </summary>
        private void DrawAddOptions(LocalizeDictionary localizeDict)
        {
            _options.addTargetOverride = EditorGUILayout.ObjectField(
                localizeDict.add_target, _options.addTargetOverride,
                typeof(AnimatorController), false) as AnimatorController;

            EditorGUILayout.LabelField(localizeDict.add_target_auto, EditorStyles.miniLabel);

            DrawAddTargetStates(localizeDict);
        }

        private void DrawSittingPoses(LocalizeDictionary localizeDict)
        {
            // 座り方選択
            string[] sittingPoses = SittingPoseTable.GetLabels(localizeDict);
            _options.sittingPose1 = SittingPoseTable.FromIndex(
                EditorGUILayout.Popup(localizeDict.sit1, SittingPoseTable.IndexOf(_options.sittingPose1), sittingPoses));
            _options.sittingPose2 = SittingPoseTable.FromIndex(
                EditorGUILayout.Popup(localizeDict.sit2, SittingPoseTable.IndexOf(_options.sittingPose2), sittingPoses));

            DrawCrouchPose(localizeDict);
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
            _supineCombiner = new SupineCombiner(_avatar, VersionFolderName);
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

        /// <summary>
        /// EditorPrefs に残す設定。読み書きの両方をこの表から作るので、片方だけ足し忘れることは無い。
        ///
        /// 追加先アニメーターは保存しない。
        /// EditorPrefsはマシン全体で共有されるため、別プロジェクトのGUIDが幽霊参照として残ってしまう。
        /// アバターを保存していないのと同じ方針。
        /// </summary>
        private PrefEntry[] Prefs => new[]
        {
            PrefEntry.Int("language", () => (int)_language, v => _language = (SupineLanguage)v),
            PrefEntry.Int("combineMode", () => (int)_options.mode, v => _options.mode = (SupineCombineMode)v),
            PrefEntry.Bool("inheritOriginal",
                () => _options.shouldInheritOriginalAnimation, v => _options.shouldInheritOriginalAnimation = v),
            PrefEntry.Bool("keepExistingCrouchPose",
                () => _options.keepExistingCrouchPose, v => _options.keepExistingCrouchPose = v),
            PrefEntry.Bool("disableJumpMotion",
                () => _options.disableJumpMotion, v => _options.disableJumpMotion = v),
            PrefEntry.Bool("enableJumpAtDesktop",
                () => _options.enableJumpAtDesktop, v => _options.enableJumpAtDesktop = v),
            PrefEntry.Int("defaultCrouchPose",
                () => (int)_options.defaultCrouchPose, v => _options.defaultCrouchPose = (CrouchPose)v),
            PrefEntry.Text("defaultCrouchPoseKey",
                () => _options.defaultCrouchPoseKey, v => _options.defaultCrouchPoseKey = v),
            PrefEntry.Int("sittingPose1",
                () => (int)_options.sittingPose1, v => _options.sittingPose1 = (SittingPose)v),
            PrefEntry.Int("sittingPose2",
                () => (int)_options.sittingPose2, v => _options.sittingPose2 = (SittingPose)v),
        };

        private void LoadPrefs()
        {
            foreach (PrefEntry entry in Prefs) entry.Load(PrefsKeyPrefix);
        }

        private void SavePrefs()
        {
            foreach (PrefEntry entry in Prefs) entry.Save(PrefsKeyPrefix);
        }

        private static SupineTemplate Template => JsonHelper.GetGuidList().template;

        /// <summary>EditorPrefs の1項目。今の値を既定値にして読むので、未保存なら何も変わらない</summary>
        private sealed class PrefEntry
        {
            private readonly string _name;
            private readonly Action<string> _load;
            private readonly Action<string> _save;

            private PrefEntry(string name, Action<string> load, Action<string> save)
            {
                _name = name;
                _load = load;
                _save = save;
            }

            public void Load(string prefix) => _load(prefix + "." + _name);
            public void Save(string prefix) => _save(prefix + "." + _name);

            public static PrefEntry Int(string name, Func<int> get, Action<int> set)
            {
                return new PrefEntry(name,
                    key => set(EditorPrefs.GetInt(key, get())),
                    key => EditorPrefs.SetInt(key, get()));
            }

            public static PrefEntry Bool(string name, Func<bool> get, Action<bool> set)
            {
                return new PrefEntry(name,
                    key => set(EditorPrefs.GetBool(key, get())),
                    key => EditorPrefs.SetBool(key, get()));
            }

            public static PrefEntry Text(string name, Func<string> get, Action<string> set)
            {
                return new PrefEntry(name,
                    key => set(EditorPrefs.GetString(key, get())),
                    key => EditorPrefs.SetString(key, get()));
            }
        }
    }
}
