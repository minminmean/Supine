using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Supine.Utilities
{
    /// <summary>
    /// アセットのコピーとパス操作をまとめたユーティリティ
    /// </summary>
    internal static class AssetPathUtility
    {
        private const string FallbackFileName = "Avatar";

        public static T CopyAssetFromPath<T>(string templatePath, string destinationPath) where T : Object
        {
            string destinationDirPath = NormalizePath(Path.GetDirectoryName(destinationPath));
            if (!Directory.Exists(destinationDirPath))
            {
                CreateFolderRecursively(destinationDirPath);
            }

            if (!AssetDatabase.CopyAsset(templatePath, destinationPath))
            {
                throw new IOException(
                    "[VRCSupine] Could not create asset: (" + destinationPath + ") from: (" + templatePath + ")");
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

        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return FallbackFileName;

            string sanitized = name;
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                sanitized = sanitized.Replace(invalidChar, '_');
            }

            // Windowsは末尾の空白とドットを扱えない
            sanitized = sanitized.Trim().TrimEnd('.').Trim();

            return string.IsNullOrEmpty(sanitized) ? FallbackFileName : sanitized;
        }

        public static void CreateFolderRecursively(string path)
        {
            if (!path.StartsWith("Assets/"))
            {
                throw new IOException(
                    "[VRCSupine] Could not create directory: (" + path + ") this is not in Assets");
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
    }
}
