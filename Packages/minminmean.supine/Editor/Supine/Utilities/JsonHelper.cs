using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Supine.Utilities
{
    public static class JsonHelper
    {
        // guids.json / Localize フォルダへの起点参照。
        // 他のアセットはすべて guids.json 側で管理する。
        private const string SupineGuidsJsonGuid = "7ea0f79a646a7af42a8bcefeb8228622";
        private const string LocalizeFolderGuid  = "560e0ecd7c0f2fc40bf8eed5acbc252a";

        private static readonly string[] LocalizeJsons =
            {
                "ja.json",
                "en.json"
            };

        private static GuidDictionary? _guidCache;
        private static readonly Dictionary<SupineLanguage, LocalizeDictionary> LocalizeCache =
            new Dictionary<SupineLanguage, LocalizeDictionary>();

        /// <summary>
        /// guids.json を読む。
        /// 読めなかった場合は既定値を返す（呼び出し側が SupineTemplate.IsValid で判定する）。
        /// </summary>
        public static GuidDictionary GetGuidList()
        {
            if (_guidCache.HasValue) return _guidCache.Value;

            GuidDictionary guids = default;
            string path = AssetDatabase.GUIDToAssetPath(SupineGuidsJsonGuid);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("[VRCSupine] Could not find guids.json for GUID: (" + SupineGuidsJsonGuid + ")");
            }
            else
            {
                try
                {
                    guids = JsonUtility.FromJson<GuidDictionary>(File.ReadAllText(path));
                }
                catch (Exception e)
                {
                    Debug.LogError("[VRCSupine] Could not read guids.json: (" + path + ") " + e.Message);
                }
            }

            _guidCache = guids;
            return guids;
        }

        /// <summary>
        /// EX 4.x の組込ウィンドウがコンパイルを通すためだけの口。何も読まずに既定値を返す。
        /// 詳しくは <see cref="SupineCombinerWindowBase"/>。
        /// </summary>
        [Obsolete(SupineLegacyEx.UpdateMessage)]
        public static LegacyGuidDictionary GetGuidList(string guidsJsonGuid)
        {
            return default;
        }

        public static LocalizeDictionary GetLocalizedTexts(SupineLanguage language)
        {
            if (LocalizeCache.TryGetValue(language, out LocalizeDictionary cached)) return cached;

            LocalizeDictionary dict = default;
            string localizeDirPath = AssetDatabase.GUIDToAssetPath(LocalizeFolderGuid);
            string path = string.IsNullOrEmpty(localizeDirPath)
                ? null
                : localizeDirPath + "/" + LocalizeJsons[(int)language];

            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("[VRCSupine] Could not find the localize directory.");
            }
            else
            {
                try
                {
                    dict = JsonUtility.FromJson<LocalizeDictionary>(File.ReadAllText(path));
                }
                catch (Exception e)
                {
                    Debug.LogError("[VRCSupine] Could not read the localize file: (" + path + ") " + e.Message);
                }
            }

            LocalizeCache[language] = dict;
            return dict;
        }
    }
}
