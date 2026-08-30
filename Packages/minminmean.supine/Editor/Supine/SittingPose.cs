using Supine.Utilities;

namespace Supine
{
    public enum SittingPose
    {
        Petan        = 0,
        TatehizaGirl = 1,
        Agura        = 2,
        TatehizaBoy  = 3
    }

    /// <summary>
    /// 座りポーズとアニメーションGUID・表示ラベルの対応表。
    /// UIの並び順とアニメーションの対応をこの1箇所に集約し、
    /// 配列の添字が別ファイル間で暗黙に対応する状態を避ける。
    /// </summary>
    internal static class SittingPoseTable
    {
        /// <summary>Popupに並べる順序</summary>
        public static readonly SittingPose[] DisplayOrder =
            {
                SittingPose.Petan,
                SittingPose.TatehizaGirl,
                SittingPose.Agura,
                SittingPose.TatehizaBoy
            };

        public static string GetAnimationGuid(SittingPose pose, GuidDictionary.Animations.Sitting guids)
        {
            switch (pose)
            {
                case SittingPose.Petan:        return guids.petan;
                case SittingPose.TatehizaGirl: return guids.tatehiza_girl;
                case SittingPose.Agura:        return guids.agura;
                case SittingPose.TatehizaBoy:  return guids.tatehiza_boy;
                default:                       return guids.petan;
            }
        }

        public static string GetLabel(SittingPose pose, LocalizeDictionary dict)
        {
            switch (pose)
            {
                case SittingPose.Petan:        return dict.petan;
                case SittingPose.TatehizaGirl: return dict.tatehiza_girl;
                case SittingPose.Agura:        return dict.agura;
                case SittingPose.TatehizaBoy:  return dict.tatehiza_boy;
                default:                       return dict.petan;
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

        public static int IndexOf(SittingPose pose)
        {
            for (int i = 0; i < DisplayOrder.Length; i++)
            {
                if (DisplayOrder[i] == pose) return i;
            }
            return 0;
        }

        public static SittingPose FromIndex(int index)
        {
            if (index < 0 || index >= DisplayOrder.Length) return DisplayOrder[0];
            return DisplayOrder[index];
        }
    }
}
