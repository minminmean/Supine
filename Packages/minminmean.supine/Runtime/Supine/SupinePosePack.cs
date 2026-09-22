using System;
using UnityEngine;

namespace Supine
{
    /// <summary>
    /// ポーズ中のトラッキングの扱い。
    ///
    /// コントローラの Desktop Mode レイヤーは、ポーズごとに Prepare Animation と
    /// Prepare Tracking のどちらへ進むかを振り分けている。その選択をポーズ側が宣言する。
    /// 寝転び系のように全身をアニメーションで決めるものは Animation、
    /// 座り系のように上半身をプレイヤーのトラッキングへ返すものは Tracking。
    /// </summary>
    public enum SupinePoseHeadTracking
    {
        Animation = 0,
        Tracking = 1,
    }

    /// <summary>
    /// ポーズ1つ分の定義。
    ///
    /// structではなくclassにしているのは、uprightThresholdに既定値を持たせるため。
    /// structだと未設定が0になり、「しゃがんだ瞬間に解除されるポーズ」が黙って出来上がる。
    /// </summary>
    [Serializable]
    public class SupinePoseEntry
    {
        /// <summary>ステート名に使う識別子。パック内で一意にする（例: "YTB"）</summary>
        public string id = string.Empty;

        /// <summary>メニューに出す名前。空ならidを使う</summary>
        public string displayName = string.Empty;

        /// <summary>メニューのアイコン。無くてもよい</summary>
        public Texture2D icon;

        /// <summary>
        /// ポーズのアニメーションクリップ。
        /// キーを2つ以上持たせると、その間を Pose Adjust（VRCSupinePoseAdjust）でスクラブできる。
        /// キーが1つでも動作するが、その場合は調整軸を持たないポーズになる。
        /// </summary>
        public AnimationClip clip;

        /// <summary>
        /// このポーズへ入る Upright の閾値。腰の高さに対応させる。
        /// 既存ポーズの実測値は 寝転び系=0.41 / SRAG=0.55 / 座位系=0.55〜0.62。
        /// 出口は生成側が threshold + UprightHysteresis で作るため、ここには入口だけを書く。
        /// </summary>
        [Range(0.1f, 0.9f)]
        public float uprightThreshold = 0.41f;

        /// <summary>トラッキングの扱い</summary>
        public SupinePoseHeadTracking headTracking = SupinePoseHeadTracking.Animation;

        /// <summary>
        /// 使ってほしい VRCSupine の値。0なら生成時に自動採番する。
        /// 他と衝突した場合も自動採番へ回されるため、ここは希望であって保証ではない。
        /// </summary>
        public int preferredValue;

        public string ResolveDisplayName()
        {
            return string.IsNullOrEmpty(displayName) ? id : displayName;
        }
    }

    /// <summary>
    /// しゃがみポーズ1つ分の定義。
    ///
    /// 寝ポーズと違い、しゃがみはステートを増やさない。しゃがみステートに差さった
    /// 1Dツリーへ枝を1本足すだけで、その枝は2Dロコモーションになる。
    /// 方向の枝は既存のものを丸ごと借りるので、用意するのは中心の待機クリップ1本。
    /// </summary>
    [Serializable]
    public class SupineCrouchEntry
    {
        /// <summary>パック内で一意な識別子</summary>
        public string id = string.Empty;

        /// <summary>メニューに出す名前。空ならidを使う</summary>
        public string displayName = string.Empty;

        /// <summary>メニューのアイコン。無くてもよい</summary>
        public Texture2D icon;

        /// <summary>
        /// しゃがんだまま止まっているときの姿勢。
        /// 歩き出したときの動きは標準のものを流用するので、ここだけ用意すればよい。
        /// </summary>
        public AnimationClip clip;

        /// <summary>
        /// 使ってほしい枠番号。0なら生成時に自動で空きを割り当てる。
        ///
        /// 0番は本体の Default（そのアバターが元々そうだった姿）の指定席なので取れない。
        /// 本体が 0〜6 を使うため、パックは 7 以降を指定することになる。
        ///
        /// 組込をやり直しても同じ番号が付かないと、メニューは保存されるぶん
        /// 「前回選んだしゃがみが別のものに化ける」。希望が埋まっていた場合は
        /// 空き番号へ回されるので、ここは希望であって保証ではない。
        /// </summary>
        public int preferredValue;

        public string ResolveDisplayName()
        {
            return string.IsNullOrEmpty(displayName) ? id : displayName;
        }
    }

    /// <summary>
    /// ポーズをまとめて登録するアセット。
    ///
    /// ごろ寝システム側はこのアセットを AssetDatabase から拾うだけで、
    /// 個々のパックの存在を知らない。パックはコントローラもコードも持たないため、
    /// unitypackageを入れればポーズが増え、消せば消える。
    /// </summary>
    [CreateAssetMenu(fileName = "SupinePosePack", menuName = "MinMinMart/Supine Pose Pack")]
    public sealed class SupinePosePack : ScriptableObject
    {
        /// <summary>パックの識別子。警告メッセージと採番順の安定化に使う</summary>
        public string packId = string.Empty;

        /// <summary>
        /// 採番とメニューの並び順。小さいほど先。
        /// 同値のときは packId、それも同じならアセットパスで決める。
        /// 並びを安定させないと、組込をやり直すたびに VRCSupine の値がずれて、
        /// 保存済みのメニュー選択が別のポーズを指してしまう。
        /// </summary>
        public int sortOrder;

        /// <summary>
        /// 必要なごろ寝システムのバージョン。空なら検証しない。
        /// 仕組みが変わったときに、黙って壊れるのではなく組込時に気付けるようにする。
        /// </summary>
        public string minimumSupineVersion = string.Empty;

        /// <summary>
        /// メニューのどのサブメニューへ入れるか。空ならパック名をそのまま使う。
        /// 既存のサブメニュー名を名乗れば、そこへ合流できる。
        /// </summary>
        public string menuFolderName = string.Empty;

        /// <summary>
        /// このパックのメニューに Pose Adjust（調整のラジアル）を置くか。
        ///
        /// クリップにキーを2つ以上持たせたポーズは、その間をスクラブして姿勢を変えられる。
        /// 置き場所は Foot Anchor の手前で固定。
        /// キーが1つしかないポーズだけのパックで出しても回すものが無いので、既定は無し。
        /// </summary>
        public bool poseAdjust;

        /// <summary>
        /// このパックのサブメニューと Pose Adjust に使うアイコン。無くてもよい。
        /// ポーズごとのアイコンは SupinePoseEntry.icon が持つ。
        /// </summary>
        public Texture2D menuIcon;

        public SupinePoseEntry[] poses = new SupinePoseEntry[0];

        /// <summary>
        /// 追加するしゃがみポーズ。寝ポーズとは別の軸なので、片方だけのパックでもよい。
        ///
        /// 行き先は本体の Crouch Poses サブメニューで、menuFolderName は見ない。
        /// しゃがみは1つのパラメータを共有する排他の選択なので、
        /// パックごとに別階層へ散らすと選択肢が探しにくくなる。
        /// </summary>
        public SupineCrouchEntry[] crouchPoses = new SupineCrouchEntry[0];

        public string ResolvePackId()
        {
            return string.IsNullOrEmpty(packId) ? name : packId;
        }
    }
}
