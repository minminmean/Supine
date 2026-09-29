using System;
using System.Collections.Generic;
using UnityEngine;

namespace Supine
{
    /// <summary>
    /// 組込時の設定の控え。SupineMASlot に持たせ、インスペクタで見返せるようにする。
    ///
    /// 組込のオプションはエディタ側の型（SupineCombineOptions や各列挙体）で持っているが、
    /// ランタイムのアセンブリからは参照できない。列挙体は名前の文字列で残し、
    /// 表示するときにエディタ側で読み替える。数値で残すと、並びを変えたときに黙って別の値に化ける。
    ///
    /// あくまで見返すための記録で、組み直すときの入力には使わない。
    /// </summary>
    [Serializable]
    public sealed class SupineCombineRecord
    {
        /// <summary>組み込んだときの本体のバージョン。空なら記録の無い古い組込</summary>
        public string version = string.Empty;

        /// <summary>組み込んだ日時（ローカル時刻、"yyyy-MM-dd HH:mm"）</summary>
        public string combinedAt = string.Empty;

        /// <summary>SupineCombineMode の名前</summary>
        public string mode = string.Empty;

        /// <summary>
        /// 実際に使ったアバターのアニメーター。
        /// 追加モードでは追加先、従来モードでは継承元（継承しないなら null）。
        /// </summary>
        public RuntimeAnimatorController sourceAnimator;

        /// <summary>追加先を手動で指定していたか（追加モードのみ）</summary>
        public bool addTargetSpecified;

        public bool inheritOriginalAnimation;
        public string inheritStandingState = string.Empty;
        public string inheritCrouchingState = string.Empty;
        public string inheritProneState = string.Empty;

        public string entryState = string.Empty;
        public string proneState = string.Empty;

        public bool disableJumpMotion;
        public bool enableJumpAtDesktop;

        /// <summary>SittingPose の名前</summary>
        public string sittingPose1 = string.Empty;
        public string sittingPose2 = string.Empty;

        public bool keepExistingCrouchPose;

        /// <summary>
        /// 既定にしたしゃがみポーズ。組み込みのものは CrouchPose の名前、
        /// パックのものは表示名をそのまま入れる（パックを外した後でも読めるように）。
        /// </summary>
        public string defaultCrouchPose = string.Empty;

        public bool crouchSlide;

        /// <summary>ポーズを差し込んだパックの識別子</summary>
        public List<string> posePacks = new List<string>();

        public bool HasRecord => !string.IsNullOrEmpty(version);
    }
}
