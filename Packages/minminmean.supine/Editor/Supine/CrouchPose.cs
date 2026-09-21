using Supine.Utilities;

namespace Supine
{
    /// <summary>
    /// 既定にするしゃがみポーズ。値は SupineCrouchPose ブレンドツリーの子の添字。
    /// </summary>
    public enum CrouchPose
    {
        Default                = 0,
        KneelFeminine          = 1,
        AirChairFeminine       = 2,
        AirChairCrossFeminine  = 3,
        KneelMasculine         = 4,
        AirChairMasculine      = 5,
        AirChairCrossMasculine = 6
    }

    /// <summary>
    /// しゃがみポーズと表示ラベルの対応表。
    ///
    /// CrouchPose の値はブレンドツリーの子の添字そのものなので、
    /// 実際の CrouchPose パラメータ値はツリーの閾値から引く。
    /// 数値をここに書き写すと、ツリーの並びを変えたときに黙ってずれる。
    /// </summary>
    internal static class CrouchPoseTable
    {
        /// <summary>Popupに並べる順序。ブレンドツリーの並びと一致させる</summary>
        public static readonly CrouchPose[] DisplayOrder =
            {
                CrouchPose.Default,
                CrouchPose.KneelFeminine,
                CrouchPose.AirChairFeminine,
                CrouchPose.AirChairCrossFeminine,
                CrouchPose.KneelMasculine,
                CrouchPose.AirChairMasculine,
                CrouchPose.AirChairCrossMasculine
            };

        /// <summary>ブレンドツリーの何番目の子か</summary>
        public static int ChildIndex(CrouchPose pose)
        {
            return (int)pose;
        }

        public static string GetLabel(CrouchPose pose, LocalizeDictionary dict)
        {
            switch (pose)
            {
                case CrouchPose.Default:                return dict.crouch_default;
                case CrouchPose.KneelFeminine:          return dict.crouch_kneel_f;
                case CrouchPose.AirChairFeminine:       return dict.crouch_air_chair_f;
                case CrouchPose.AirChairCrossFeminine:  return dict.crouch_air_chair_cross_f;
                case CrouchPose.KneelMasculine:         return dict.crouch_kneel_m;
                case CrouchPose.AirChairMasculine:      return dict.crouch_air_chair_m;
                case CrouchPose.AirChairCrossMasculine: return dict.crouch_air_chair_cross_m;
                default:                                return dict.crouch_default;
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

        public static int IndexOf(CrouchPose pose)
        {
            for (int i = 0; i < DisplayOrder.Length; i++)
            {
                if (DisplayOrder[i] == pose) return i;
            }
            return 0;
        }

        public static CrouchPose FromIndex(int index)
        {
            if (index < 0 || index >= DisplayOrder.Length) return DisplayOrder[0];
            return DisplayOrder[index];
        }
    }
}
