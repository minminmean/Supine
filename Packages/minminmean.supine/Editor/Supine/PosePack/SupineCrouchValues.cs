using UnityEngine;

namespace Supine.PosePack
{
    /// <summary>
    /// しゃがみポーズの枠番号と CrouchPose パラメータの値の対応。
    ///
    /// この対応だけは、ツリーから読むのではなくここが決める。
    /// 新しく足す枝には、まだツリーに無い値を割り当てる必要があるため。
    ///
    /// 素直に 0,1,2… と振ることはできない。CrouchPose は同期される float で、
    /// VRChat の同期 float は -1.0〜1.0 の 255 段階しかない。範囲外はクランプされるので、
    /// 2 以上を入れると他プレイヤーからは全部同じポーズに見える。
    ///
    /// かといって「個数で等分」にすると、種類が増えたときに既存の値まで動く。
    /// メニュー項目は保存されるため、それは「前回選んだしゃがみが別のものに化ける」
    /// という形で表に出る。
    ///
    /// そこで個数に依存しない固定の刻みを使い、値域の中へ収める。
    /// 1.0 を超えたぶんは負側へ折り返すので、順序に意味は無い。番号が動かないことだけが要る。
    /// </summary>
    internal static class SupineCrouchValues
    {
        /// <summary>
        /// 枠の間隔。量子化の誤差は最大 1/254 なので、隣のポーズが混ざるのは約3%に収まる。
        /// 細かくすれば枠は増えるが、そのぶんリモートでの混ざりが増える。
        /// </summary>
        public const float Step = 0.125f;

        /// <summary>正の側に収まる最後の枠番号</summary>
        private const int PositiveMax = 8;

        /// <summary>使える枠番号の上限。これを超えると値域から出る</summary>
        public const int MaxIndex = PositiveMax * 2;

        public static float ToValue(int index)
        {
            return index <= PositiveMax ? index * Step : -(index - PositiveMax) * Step;
        }

        /// <summary>
        /// 同じ枠を指す値か。float の往復で付く誤差を吸収する。
        /// 許容幅は刻みの 1/4 なので、隣の枠と取り違えることは無い。
        /// </summary>
        public static bool Approximately(float a, float b)
        {
            return Mathf.Abs(a - b) <= Step * 0.25f;
        }

        /// <summary>値から枠番号へ戻す。対応しない値なら -1</summary>
        public static int ToIndex(float value)
        {
            int steps = Mathf.RoundToInt(value / Step);

            // 刻みに乗っていない値は、別の仕組みが付けたものとみなして触らない
            if (!Approximately(value, steps * Step)) return -1;

            if (steps >= 0) return steps <= PositiveMax ? steps : -1;

            int folded = PositiveMax - steps;
            return folded <= MaxIndex ? folded : -1;
        }
    }
}
