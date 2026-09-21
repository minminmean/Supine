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

        /// <summary>連番をいくつまで試すか</summary>
        private const int MaxNameSuffix = 999;

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

        /// <summary>
        /// 既存のファイルとぶつからないパスを返す。
        /// ぶつかる場合は、拡張子の手前に連番を足す。
        /// </summary>
        public static string MakeUniqueAssetPath(string destinationPath)
        {
            if (!File.Exists(destinationPath)) return destinationPath;

            string dirPath   = NormalizePath(Path.GetDirectoryName(destinationPath));
            string name      = Path.GetFileNameWithoutExtension(destinationPath);
            string extension = Path.GetExtension(destinationPath);

            // 現実的な回数で空きが見つからないなら、何かがおかしい。
            // 黙って回り続けるより、組込を失敗させて気付かせる
            for (int suffix = 1; suffix <= MaxNameSuffix; suffix++)
            {
                string candidate = dirPath + '/' + name + '_' + suffix + extension;
                if (!File.Exists(candidate)) return candidate;
            }

            throw new IOException(
                "[VRCSupine] Could not find a free file name for: (" + destinationPath + ")");
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
