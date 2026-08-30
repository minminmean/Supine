using UnityEditor.Animations;

namespace Supine
{
    /// <summary>
    /// 組込時のオプション。
    ///
    /// 一部のオプションは特定のモードでしか意味を持たないため、
    /// 「どのモードでどれが有効か」という条件はこの構造体に閉じ込め、
    /// UI側と生成側で判断が食い違わないようにする。
    /// </summary>
    public struct SupineCombineOptions
    {
        public SupineCombineMode mode;

        /// <summary>元の立ち・しゃがみ・伏せアニメーションを継承するか（Standardのみ）</summary>
        public bool shouldInheritOriginalAnimation;

        /// <summary>
        /// 継承元にする既存ステート名（Standardのみ）。空ならテンプレートと同じ名前で探す。
        /// 出し入れは InheritedStateTable を通す。
        /// </summary>
        public string inheritStandingStateName;
        public string inheritCrouchingStateName;
        public string inheritProneStateName;

        /// <summary>追加先アニメーターの手動指定（Addのみ。nullならアバターから自動取得）</summary>
        public AnimatorController addTargetOverride;

        /// <summary>
        /// ごろ寝システムの入口として使う、追加先の既存ステート名（Addのみ）。
        /// 既成のアニメーターはステート名を変えていることが多いので、名前一致に頼らず指定できるようにする。
        /// 空ならテンプレートと同じ名前で探す。
        /// </summary>
        public string entryStateName;

        /// <summary>
        /// 既定の伏せポーズとして扱う、追加先の既存ステート名（Addのみ）。空なら名前一致で探す。
        /// </summary>
        public string proneStateName;

        public bool disableJumpMotion;
        public bool enableJumpAtDesktop;

        public SittingPose sittingPose1;
        public SittingPose sittingPose2;

        public static SupineCombineOptions Default =>
            new SupineCombineOptions
                {
                    mode                          = SupineCombineMode.Standard,
                    shouldInheritOriginalAnimation = true,
                    inheritStandingStateName      = null,
                    inheritCrouchingStateName     = null,
                    inheritProneStateName         = null,
                    addTargetOverride             = null,
                    entryStateName                = null,
                    proneStateName                = null,
                    disableJumpMotion             = true,
                    enableJumpAtDesktop           = true,
                    sittingPose1                  = SittingPose.Petan,
                    sittingPose2                  = SittingPose.TatehizaGirl
                };

        /// <summary>実際に継承を行うか。追加モードでは継承の出番が無い</summary>
        public bool ShouldInherit =>
            mode == SupineCombineMode.Standard && shouldInheritOriginalAnimation;

        /// <summary>
        /// ジャンプ関連のオプションを適用するか。
        /// これらはごろ寝システムのアニメーターを書き換えるためのオプションなので、
        /// 追加モードでは既存アニメーターのジャンプ・落下の挙動をそのまま残す。
        /// </summary>
        public bool ShouldApplyJumpOptions => mode == SupineCombineMode.Standard;

        /// <summary>実際に使う手動指定。従来モードでは無視する</summary>
        public AnimatorController EffectiveAddTargetOverride =>
            mode == SupineCombineMode.Add ? addTargetOverride : null;
    }
}
