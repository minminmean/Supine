using System.IO;
using UnityEditor;
using UnityEngine;

namespace Supine
{
    namespace Utilities
    {
        public static class JsonHelper
        {
            private static string _guidListGuid = "7ea0f79a646a7af42a8bcefeb8228622";
            private static string _localizeGuid = "560e0ecd7c0f2fc40bf8eed5acbc252a";

            private static string[] _localizeJsons =
                {
                    "ja.json",
                    "en.json"
                };

            public static GuidDictionary GetGuidList()
            {
                string guidPath = AssetDatabase.GUIDToAssetPath(_guidListGuid);
                string jsonContent = ReadJsonFromPath(guidPath);
                GuidDictionary guids = JsonUtility.FromJson<GuidDictionary>(jsonContent);
                return guids;
            }

            public static LocalizeDictionary GetLocalizedTexts(int languageOrder)
            {
                string localizePath = AssetDatabase.GUIDToAssetPath(_localizeGuid);
                string jsonContent = ReadJsonFromPath(localizePath + "/" + _localizeJsons[languageOrder]);
                LocalizeDictionary dict = JsonUtility.FromJson<LocalizeDictionary>(jsonContent);
                return dict;
            }

            private static string ReadJsonFromPath(string path)
            {
                FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                StreamReader reader = new StreamReader(fs);
                string jsonContent = reader.ReadToEnd();
                reader.Close();

                return jsonContent;
            }
        }
    }
}