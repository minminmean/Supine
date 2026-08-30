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

        // バリアントごとに別のJSONを読むため、GUIDをキーにしてキャッシュする。
        // 単一フィールドでキャッシュすると通常版とEX版で参照先が混線する。
        private static readonly Dictionary<string, GuidDictionary> GuidCache =
            new Dictionary<string, GuidDictionary>();
        private static readonly Dictionary<SupineLanguage, LocalizeDictionary> LocalizeCache =
            new Dictionary<SupineLanguage, LocalizeDictionary>();

        /// <summary>
        /// ごろ寝システム本体の guids.json を読む
        /// </summary>
        public static GuidDictionary GetGuidList()
        {
            return GetGuidList(SupineGuidsJsonGuid);
        }

        /// <summary>
        /// GUIDを指定して guids.json を読む。
        /// EX版など別パッケージが自分のバリアント定義を読むために使う。
        /// 読めなかった場合は既定値を返す（呼び出し側が SupineVariant.IsValid で判定する）。
        /// </summary>
        /// <param name="guidsJsonGuid">guids.json のGUID</param>
        public static GuidDictionary GetGuidList(string guidsJsonGuid)
        {
            if (GuidCache.TryGetValue(guidsJsonGuid, out GuidDictionary cached)) return cached;

            GuidDictionary guids = default;
            string path = AssetDatabase.GUIDToAssetPath(guidsJsonGuid);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("[VRCSupine] Could not find guids.json for GUID: (" + guidsJsonGuid + ")");
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

            GuidCache[guidsJsonGuid] = guids;
            return guids;
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
