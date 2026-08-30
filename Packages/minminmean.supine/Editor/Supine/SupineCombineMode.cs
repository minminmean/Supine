using Supine.Utilities;

namespace Supine
{
    public enum SupineCombineMode
    {
        /// <summary>ごろ寝システムのアニメーターをそのまま使う（従来）</summary>
        Standard = 0,

        /// <summary>既存のアニメーターにごろ寝システムのステート群を追加する</summary>
        Add      = 1
    }

    /// <summary>
    /// 結合方法と表示ラベルの対応表。
    /// UIの並び順をこの1箇所に集約し、配列の添字が別ファイル間で暗黙に対応する状態を避ける。
    /// </summary>
    internal static class SupineCombineModeTable
    {
        /// <summary>Popupに並べる順序</summary>
        public static readonly SupineCombineMode[] DisplayOrder =
            {
                SupineCombineMode.Standard,
                SupineCombineMode.Add
            };

        public static string GetLabel(SupineCombineMode mode, LocalizeDictionary dict)
        {
            switch (mode)
            {
                case SupineCombineMode.Standard: return dict.combine_mode_standard;
                case SupineCombineMode.Add:      return dict.combine_mode_add;
                default:                         return dict.combine_mode_standard;
            }
        }

        public static string[] GetLabels(LocalizeDictionary dict)
        {
            string[] labels = new string[DisplayOrder.Length];
            for (int i = 0; i < DisplayOrder.Length; i++)
            {
                labels[i] = GetLabel(DisplayOrder[i], dict);
            }
            return labels;
        }

        public static int IndexOf(SupineCombineMode mode)
        {
            for (int i = 0; i < DisplayOrder.Length; i++)
            {
                if (DisplayOrder[i] == mode) return i;
            }
            return 0;
        }

        public static SupineCombineMode FromIndex(int index)
        {
            if (index < 0 || index >= DisplayOrder.Length) return DisplayOrder[0];
            return DisplayOrder[index];
        }
    }
}
