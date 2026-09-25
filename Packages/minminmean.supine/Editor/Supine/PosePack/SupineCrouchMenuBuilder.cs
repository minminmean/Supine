using System.Collections.Generic;
using UnityEngine;
using Supine.Utilities;

namespace Supine.PosePack
{
    /// <summary>
    /// パックが足したしゃがみポーズを Crouch Poses サブメニューへ並べる。
    ///
    /// 寝ポーズのメニューと違い、パックごとに階層を分けない。
    /// しゃがみは1つのパラメータを共有する排他の選択なので、
    /// 出どころで散らすと「今どれが選ばれているのか」が探しにくくなる。
    ///
    /// 枠が足りなくなったら送りページへ逃がす。ここには Foot Anchor も
    /// Pose Adjust も置かないため、1ページまるごと8枠使える。
    /// </summary>
    internal static class SupineCrouchMenuBuilder
    {
        private const string CrouchMenuName = SupineNames.Menus.CrouchPoses;
        private const string CrouchPoseParameter = SupineNames.Parameters.CrouchPose;

        public static void Build(
            GameObject maPrefabInstance, IReadOnlyList<ResolvedCrouchPose> poses, List<string> warnings)
        {
            if (maPrefabInstance == null) return;
            if (poses == null || poses.Count == 0) return;

            Transform menu = MenuItemUtility.FindDescendant(maPrefabInstance.transform, CrouchMenuName);
            if (menu == null)
            {
                warnings.Add(
                    "Could not find the '" + CrouchMenuName + "' menu. " +
                    poses.Count + " crouch pose(s) were not added.");
                return;
            }

            foreach (ResolvedCrouchPose pose in poses)
            {
                // 既存のしゃがみ項目と同じく保存する
                MenuItemUtility.CreateToggle(
                    menu, pose.Entry.ResolveDisplayName(), pose.Entry.icon,
                    CrouchPoseParameter, pose.Value, true);
            }

            Paginate(menu);
        }

        /// <summary>
        /// 溢れたぶんを送りページへ移す。
        ///
        /// 先に項目を全部足してから畳む。足しながらページを割ると、
        /// 「あと何個来るか」を知らないまま送りを作ることになり、
        /// 1個しか溢れていないのに送りページが生える場合が出る。
        ///
        /// 送りページのアイコンは親のものを借りる。
        /// 無地のまま並ぶより、同じ絵が続くほうが同じ話の続きだと分かる。
        /// </summary>
        private static void Paginate(Transform menu)
        {
            List<Transform> items = new List<Transform>();
            foreach (Transform child in menu) items.Add(child);

            // 末尾に添える項目が無いので、どのページも必ず1つは置ける
            List<int> pages = MenuItemUtility.SplitIntoPages(items.Count, 0, 0);
            MenuItemUtility.DistributeToPages(menu, items, pages, null, MenuItemUtility.GetIcon(menu));
        }
    }
}
