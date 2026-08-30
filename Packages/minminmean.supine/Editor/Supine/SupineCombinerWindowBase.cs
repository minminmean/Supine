using System.IO;
using UnityEngine;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
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

        private GameObject _avatar;
        private SupineCombiner _supineCombiner;

        private SupineLanguage _language = SupineLanguage.Japanese;

        private bool _canCombine = false;
        private bool _shouldInheritOriginalAnimation = true;
        private bool _disableJumpMotion = true;
        private bool _enableJumpAtDesktop = true;

        private SittingPose _sittingPose1 = SittingPose.Petan;
        private SittingPose _sittingPose2 = SittingPose.TatehizaGirl;

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
            LoadPrefs();
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();

            LocalizeDictionary localizeDict = DrawLanguageSelector();

            EditorGUILayout.Space();

            DrawAvatarField(localizeDict);

            EditorGUILayout.Space();

            DrawOptions(localizeDict);

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

        private void DrawOptions(LocalizeDictionary localizeDict)
        {
            // 元の立ち、しゃがみ、伏せアニメーションを継承するか
            _shouldInheritOriginalAnimation = EditorGUILayout.ToggleLeft(localizeDict.inherit_original, _shouldInheritOriginalAnimation);

            // ジャンプモーションを無効にするか
            _disableJumpMotion = EditorGUILayout.ToggleLeft(localizeDict.disable_jump_motion, _disableJumpMotion);
            using (new EditorGUI.DisabledGroupScope(!_disableJumpMotion))
            {
                EditorGUI.indentLevel++;
                _enableJumpAtDesktop = EditorGUILayout.ToggleLeft(localizeDict.enable_jump_at_desktop, _enableJumpAtDesktop);
                if (!_disableJumpMotion)
                {
                    _enableJumpAtDesktop = false;
                }
                EditorGUI.indentLevel--;
            }
        }

        private void DrawSittingPoses(LocalizeDictionary localizeDict)
        {
            // 座り方選択
            string[] sittingPoses = SittingPoseTable.GetLabels(localizeDict);
            _sittingPose1 = SittingPoseTable.FromIndex(
                EditorGUILayout.Popup(localizeDict.sit1, SittingPoseTable.IndexOf(_sittingPose1), sittingPoses));
            _sittingPose2 = SittingPoseTable.FromIndex(
                EditorGUILayout.Popup(localizeDict.sit2, SittingPoseTable.IndexOf(_sittingPose2), sittingPoses));
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
            if (_supineCombiner.CanCombine)
            {
                _canCombine = true;
                EditorUtility.DisplayDialog(localizeDict.check_successful, localizeDict.check_successful_message, "OK");
                Debug.Log("[VRCSupine] Check OK.");
            }
            else
            {
                _canCombine = false;
                EditorUtility.DisplayDialog(localizeDict.check_failure, localizeDict.check_failure_message, "OK");
                Debug.Log("[VRCSupine] Check failed.");
            }
        }

        private void TryCreatePrefab(LocalizeDictionary localizeDict)
        {
            try
            {
                _supineCombiner.CreateMAPrefab(
                    shouldInheritOriginalAnimation: _shouldInheritOriginalAnimation,
                    disableJumpMotion: _disableJumpMotion,
                    enableJumpAtDesktop: _enableJumpAtDesktop,
                    sittingPose1: _sittingPose1,
                    sittingPose2: _sittingPose2
                );
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
            _shouldInheritOriginalAnimation = EditorPrefs.GetBool(PrefsKey("inheritOriginal"), _shouldInheritOriginalAnimation);
            _disableJumpMotion  = EditorPrefs.GetBool(PrefsKey("disableJumpMotion"), _disableJumpMotion);
            _enableJumpAtDesktop = EditorPrefs.GetBool(PrefsKey("enableJumpAtDesktop"), _enableJumpAtDesktop);
            _sittingPose1 = (SittingPose)EditorPrefs.GetInt(PrefsKey("sittingPose1"), (int)_sittingPose1);
            _sittingPose2 = (SittingPose)EditorPrefs.GetInt(PrefsKey("sittingPose2"), (int)_sittingPose2);
        }

        private void SavePrefs()
        {
            EditorPrefs.SetInt(PrefsKey("language"), (int)_language);
            EditorPrefs.SetBool(PrefsKey("inheritOriginal"), _shouldInheritOriginalAnimation);
            EditorPrefs.SetBool(PrefsKey("disableJumpMotion"), _disableJumpMotion);
            EditorPrefs.SetBool(PrefsKey("enableJumpAtDesktop"), _enableJumpAtDesktop);
            EditorPrefs.SetInt(PrefsKey("sittingPose1"), (int)_sittingPose1);
            EditorPrefs.SetInt(PrefsKey("sittingPose2"), (int)_sittingPose2);
        }

        private string PrefsKey(string name)
        {
            return PrefsKeyPrefix + "." + name;
        }
    }
}
