using System;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Supine.Utilities
{
    /// <summary>
    /// ごろ寝システム本体のバージョン。package.json を唯一の情報源とする。
    /// </summary>
    internal static class SupinePackageVersion
    {
        /// <summary>package.json の version。解決できなければ null</summary>
        public static string Current =>
            PackageInfo.FindForAssembly(typeof(SupinePackageVersion).Assembly)?.version;

        /// <summary>
        /// current が required 以上か。
        ///
        /// どちらかが読めない場合は true を返す。バージョンの書き方が崩れているだけで
        /// パックを丸ごと弾くと、利用者からは「ポーズが消えた」としか見えない。
        /// </summary>
        public static bool IsAtLeast(string current, string required)
        {
            if (!TryParse(current, out Version currentVersion)) return true;
            if (!TryParse(required, out Version requiredVersion)) return true;

            return currentVersion >= requiredVersion;
        }

        /// <summary>"5.0.2" や "5.1.0-beta.1" を読む。プレリリースの部分は見ない</summary>
        private static bool TryParse(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(text)) return false;

            string core = text.Trim();
            int suffix = core.IndexOfAny(new[] { '-', '+' });
            if (suffix >= 0) core = core.Substring(0, suffix);

            // System.Version は "5" のような1桁の表記を受け付けない
            if (core.IndexOf('.') < 0) core += ".0";

            return Version.TryParse(core, out version);
        }
    }
}
