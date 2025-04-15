using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.Animations;

namespace Supine
{
    namespace Utilities
    {
        class Utility
        {
            private static string _appVersion;
            private static string _appVersionEX;
            private static GuidDictionary _guidList = JsonHelper.GetGuidList();

            public static GuidDictionary GuidList
            {
                get { return _guidList; }
            }

            public static T CopyAssetFromPath<T>(string templatePath, string destinationPath) where T : Object
            {
                string destinationDirPath = Path.GetDirectoryName(destinationPath);
                destinationDirPath = NormalizePath(destinationDirPath);
                if (!Directory.Exists(destinationDirPath))
                {
                    CreateFolderRecursively(destinationDirPath);
                }

                if (!AssetDatabase.CopyAsset(templatePath, destinationPath))
                {
                    Debug.LogError("[VRCSupine] Could not create asset: (" + destinationPath + ") from: (" + templatePath + ")");
                    throw new IOException();
                }

                return AssetDatabase.LoadAssetAtPath<T>(destinationPath);
            }

            public static string NormalizePath(string path)
            {
                if (string.IsNullOrEmpty(path)) return path;

                path = path.Replace('\\', '/');

                while (path.Contains("//"))
                {
                    path = path.Replace("//", "/");
                }

                return path;
            }

            public static void CreateFolderRecursively(string path)
            {
                if (!path.StartsWith("Assets/"))
                {
                    Debug.LogError("[VRCSupine] Could not create directory: (" + path + ") this is not in Assets");
                    throw new IOException();
                }

                string[] dirs = path.Split('/');
                string combinePath = dirs[0];
                foreach (string dir in dirs.Skip(1))
                {
                    if (!AssetDatabase.IsValidFolder(combinePath + '/' + dir))
                    {
                        AssetDatabase.CreateFolder(combinePath, dir);
                    }
                    combinePath += '/' + dir;
                }

                Debug.Log("[VRCSupine] Created the directory '" + path + "'.");
            }

            public static AnimatorState FindAnimatorStateByName(ChildAnimatorState[] states, string name)
            {
                foreach (ChildAnimatorState childState in states)
                {
                    if (childState.state.name == name)
                    {
                        return childState.state;
                    }
                }
                return null;
            }

            public static string GetAppVersion()
            {
                if (string.IsNullOrEmpty(_appVersion))
                {
                    _appVersion = ReadTextFromGuid(_guidList.app_versions.normal);
                }
                return _appVersion;
            }

            public static string GetAppVersionEX()
            {
                if (string.IsNullOrEmpty(_appVersionEX))
                {
                    _appVersionEX = ReadTextFromGuid(_guidList.app_versions.ex);
                }
                return _appVersionEX;
            }

            private static string ReadTextFromGuid(string guid)
            {
                return File.ReadAllText(AssetDatabase.GUIDToAssetPath(guid));
            }
        }
    }

}