namespace Supine
{
    /// <summary>
    /// テンプレートのコントローラと SupineMA Prefab が持っている名前。
    ///
    /// どれもアセット側で決まっている名前で、コードは後追いで合わせているだけ。
    /// 複数のクラスが同じ名前を書き写していると、アセット側で変えたときに直し漏れる。
    /// 1つのクラスからしか見ない名前は、そのクラスに置いたままにしている。
    /// </summary>
    internal static class SupineNames
    {
        /// <summary>テンプレートのコントローラのステート名</summary>
        internal static class States
        {
            public const string Standing = "Standing";
            public const string Crouching = "Crouching";
            public const string Prone = "Prone";
            public const string Sit1 = "Sit 1";
            public const string Sit2 = "Sit 2";
        }

        /// <summary>テンプレートのコントローラと Prefab が共有するパラメータ名</summary>
        internal static class Parameters
        {
            /// <summary>どのポーズで寝るか。メニューのトグルが書き込む</summary>
            public const string Pose = "VRCSupine";

            /// <summary>クリップのキーの間をスクラブする軸。メニュー側もこれを回す</summary>
            public const string PoseAdjust = "VRCSupinePoseAdjust";

            /// <summary>調整中であることを示すフラグ。ラジアルを開いている間だけ立つ</summary>
            public const string PoseAdjusting = "VRCSupinePoseAdjusting";

            /// <summary>VRChat標準の Upright</summary>
            public const string Upright = "Upright";

            /// <summary>しゃがみポーズの選択</summary>
            public const string CrouchPose = "CrouchPose";

            public const string EnableJumpMotion = "EnableJumpMotion";
            public const string EnableJumpAtDesktop = "EnableJumpAtDesktop";
        }

        /// <summary>SupineMA Prefab のメニュー項目名</summary>
        internal static class Menus
        {
            /// <summary>パックのサブメニューを並べる先</summary>
            public const string Root = "Suimin";

            public const string CrouchPoses = "Crouch Poses";
            public const string Misc = "Misc";
            public const string FootAnchor = "Foot Anchor";

            /// <summary>姿勢の微調整を回すラジアル。生成側が作る</summary>
            public const string PoseAdjust = "Pose Adjust";

            /// <summary>ページが溢れたときに次ページへ送る項目。生成側が作る</summary>
            public const string NextPage = "Next";
        }
    }
}
