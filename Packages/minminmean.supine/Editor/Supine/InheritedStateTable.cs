using Supine.Utilities;

namespace Supine
{
    /// <summary>
    /// 「元のアニメーションを継承」でモーションを引き継ぐステートの対応表。
    ///
    /// 継承元は既存アニメーターのステートだが、既成のアニメーターはステート名を変えていることが多い。
    /// テンプレート側の名前（Standing / Crouching / Prone）と、
    /// 実際に引いてくる既存ステート名の対応をこの1箇所に集約する。
    /// </summary>
    internal static class InheritedStateTable
    {
        /// <summary>継承先になるテンプレート側のステート名。UIの並び順でもある</summary>
        public static readonly string[] TemplateStateNames =
            {
                "Standing",
                "Crouching",
                "Prone"
            };

        public static string GetLabel(string templateStateName, LocalizeDictionary dict)
        {
            switch (templateStateName)
            {
                case "Standing":  return dict.inherit_standing_state;
                case "Crouching": return dict.inherit_crouching_state;
                case "Prone":     return dict.inherit_prone_state;
                default:          return templateStateName;
            }
        }

        /// <summary>継承元にする既存ステート名。未指定ならテンプレートと同じ名前で探す</summary>
        public static string GetSourceStateName(SupineCombineOptions options, string templateStateName)
        {
            switch (templateStateName)
            {
                case "Standing":  return options.inheritStandingStateName;
                case "Crouching": return options.inheritCrouchingStateName;
                case "Prone":     return options.inheritProneStateName;
                default:          return null;
            }
        }

        public static void SetSourceStateName(
            ref SupineCombineOptions options, string templateStateName, string sourceStateName)
        {
            switch (templateStateName)
            {
                case "Standing":  options.inheritStandingStateName  = sourceStateName; break;
                case "Crouching": options.inheritCrouchingStateName = sourceStateName; break;
                case "Prone":     options.inheritProneStateName     = sourceStateName; break;
            }
        }

        /// <summary>
        /// 継承元にする既存ステート名を決める。
        ///
        /// null（未指定）ならテンプレートと同じ名前で探す。
        /// 空文字は「継承しない」という意思表示なので false を返し、
        /// ごろ寝システムに元から入っているアニメーションをそのまま使わせる。
        /// </summary>
        public static bool TryResolveSourceStateName(
            SupineCombineOptions options, string templateStateName, out string sourceStateName)
        {
            string configured = GetSourceStateName(options, templateStateName);

            if (configured == null)
            {
                sourceStateName = templateStateName;
                return true;
            }

            if (configured.Length == 0)
            {
                sourceStateName = null;
                return false;
            }

            sourceStateName = configured;
            return true;
        }
    }
}
