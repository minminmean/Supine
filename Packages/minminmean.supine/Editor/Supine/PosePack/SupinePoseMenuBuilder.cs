using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Avatars.ScriptableObjects;
using Supine.Utilities;
using ModularAvatarMenuItem = nadena.dev.modular_avatar.core.ModularAvatarMenuItem;

namespace Supine.PosePack
{
    /// <summary>
    /// 差し込んだポーズのメニュー項目を、設置済みのMA Prefabへ生やす。
    ///
    /// Prefab側に項目を並べておく方式だと、パックの数だけPrefabを用意することになる。
    /// 項目はコントローラの配線と同じタイミングで作り、Prefabは受け皿だけを持つ。
    ///
    /// 置き場所はメニューのトップレベルで、**パックごとに1枚**サブメニューを作る。
    /// 共通の1枚へ全部まとめると、VRChatの上限8項目をすぐ超えて「Next」で
    /// ページを送ることになり、目的のポーズに辿り着くまでの手数が増える。
    ///
    /// パックのメニューは Supine Poses の直後に並べ、Crouch Poses・Misc・Foot Anchor は
    /// 常にその後ろへ送り直す。パックが増えてトップレベルが溢れたら、
    /// <see cref="PaginateRoot"/> がそれらを各ページの末尾に置いたまま「Next」で送る。
    /// </summary>
    internal static class SupinePoseMenuBuilder
    {
        private const string RootMenuName = SupineNames.Menus.Root;

        /// <summary>パックのメニューより後ろに置く項目。この順で末尾に並べる</summary>
        private static readonly string[] TrailingMenuNames =
            { SupineNames.Menus.CrouchPoses, SupineNames.Menus.Misc, SupineNames.Menus.FootAnchor };

        /// <summary>各パックのメニュー末尾に添える項目。既存のものを複製して使う</summary>
        private const string FootAnchorName = SupineNames.Menus.FootAnchor;

        /// <summary>姿勢の微調整を回すラジアル。Foot Anchor の手前に置く</summary>
        private const string PoseAdjustName = SupineNames.Menus.PoseAdjust;

        public static void Build(
            GameObject maPrefabInstance, IReadOnlyList<ResolvedPose> poses, List<string> warnings)
        {
            if (poses == null || poses.Count == 0) return;

            Transform posesRoot = MenuItemUtility.FindDescendant(maPrefabInstance.transform, RootMenuName);
            if (posesRoot == null)
            {
                warnings.Add(
                    "Could not find the '" + RootMenuName + "' menu in the Supine MA prefab. " +
                    "Pose pack menu items were not created.");
                return;
            }

            // 置き場所ごとにまとめる。既存のサブメニュー名を名乗れば、そこへ合流する
            List<string> folderOrder = new List<string>();
            Dictionary<string, List<ResolvedPose>> folders = new Dictionary<string, List<ResolvedPose>>();
            HashSet<string> adjustFolders = new HashSet<string>();
            Dictionary<string, Texture2D> folderIcons = new Dictionary<string, Texture2D>();

            foreach (ResolvedPose pose in poses)
            {
                string folder = ResolveFolderName(pose);
                if (!folders.TryGetValue(folder, out List<ResolvedPose> group))
                {
                    group = new List<ResolvedPose>();
                    folders.Add(folder, group);
                    folderOrder.Add(folder);
                }
                group.Add(pose);

                // 同じメニューへ合流したパックのうち、1つでも宣言していれば出す
                if (pose.Pack.poseAdjust) adjustFolders.Add(folder);

                // アイコンは先に名乗ったパックのものを使う
                if (pose.Pack.menuIcon != null && !folderIcons.ContainsKey(folder))
                {
                    folderIcons.Add(folder, pose.Pack.menuIcon);
                }
            }

            // 雛形は自分で作った複製を拾わないよう、1つも生やす前に確保しておく
            Transform footAnchor = MenuItemUtility.FindDescendant(maPrefabInstance.transform, FootAnchorName);
            if (footAnchor == null)
            {
                warnings.Add(
                    "Could not find the '" + FootAnchorName + "' menu item to copy. " +
                    "Pose pack menus were created without it.");
            }

            foreach (string folder in folderOrder)
            {
                folderIcons.TryGetValue(folder, out Texture2D icon);

                Transform submenu = MenuItemUtility.FindChild(posesRoot, folder) ??
                                    MenuItemUtility.CreateSubMenu(posesRoot, folder, icon);
                FillPages(
                    submenu, folders[folder], adjustFolders.Contains(folder), icon,
                    footAnchor != null ? footAnchor.gameObject : null, warnings);
            }

            // パックのメニューは末尾に生えるので、後ろに置く項目を順に末尾へ送り直す。
            // 結果は「Supine Poses / 各パック / Crouch Poses / Misc / Foot Anchor」になる
            foreach (string name in TrailingMenuNames)
            {
                Transform trailing = MenuItemUtility.FindChild(posesRoot, name);
                if (trailing != null) trailing.SetAsLastSibling();
            }
        }

        /// <summary>
        /// トップレベルが上限を超えたら「Next」でページを送る。
        ///
        /// Crouch Poses・Misc・Foot Anchor はどのページの末尾にも置く。
        /// 並びは1ページ目が「Supine Poses / パック … / Crouch Poses / Misc / Foot Anchor / Next」、
        /// 2ページ目以降が「パック … / Crouch Poses / Misc / Foot Anchor」。
        ///
        /// 末尾の項目は複製して配るので、中身が出来上がってから呼ぶこと。
        /// しゃがみの組込より前に畳むと、Crouch Poses の複製にパックのしゃがみが入らず、
        /// 既存の切り替えを活かす場合も1枚目しか消えない。
        /// </summary>
        public static void PaginateRoot(GameObject maPrefabInstance, List<string> warnings)
        {
            Transform posesRoot = MenuItemUtility.FindDescendant(maPrefabInstance.transform, RootMenuName);
            if (posesRoot == null || posesRoot.childCount <= MenuItemUtility.MenuCapacity) return;

            List<Transform> trailing = new List<Transform>();
            foreach (string name in TrailingMenuNames)
            {
                Transform item = MenuItemUtility.FindChild(posesRoot, name);
                if (item != null) trailing.Add(item);
            }

            List<Transform> body = new List<Transform>();
            foreach (Transform child in posesRoot)
            {
                if (!trailing.Contains(child)) body.Add(child);
            }

            // 末尾の項目と送りを置いたら1枠も残らないなら、ページを送っても進まない
            List<int> pages = MenuItemUtility.SplitIntoPages(body.Count, trailing.Count, 0);
            if (pages == null)
            {
                WarnIfOverCapacity(posesRoot, warnings);
                return;
            }

            // 1ページ目には元の末尾の項目が並んでいるので、送った先のページにだけ複製する
            MenuItemUtility.DistributeToPages(
                posesRoot, body, pages,
                page =>
                {
                    foreach (Transform item in trailing) MenuItemUtility.CopyItem(item.gameObject, page);
                },
                MenuItemUtility.GetIcon(posesRoot));
        }

        /// <summary>
        /// このポーズを入れるサブメニューの名前。
        /// 指定が無ければパック名をそのまま使う。同じ名前を名乗ったパック同士は合流する。
        /// </summary>
        private static string ResolveFolderName(ResolvedPose pose)
        {
            string folder = pose.Pack.menuFolderName;
            return string.IsNullOrEmpty(folder) ? pose.Pack.ResolvePackId() : folder;
        }

        /// <summary>
        /// サブメニューへ項目を並べる。上限を超える分は「Next」で次ページへ送る。
        /// 黙って切り捨てると、買ったポーズがメニューに出てこないという形で現れる。
        ///
        /// Pose Adjust と Foot Anchor はどのページの末尾にも置く。ページを送った先で
        /// 使えないと、足を固定するためだけに前のページへ戻ることになる。
        /// 並びは「ポーズ … / Pose Adjust / Foot Anchor / Next」。
        /// </summary>
        private static void FillPages(
            Transform submenu, List<ResolvedPose> poses,
            bool withAdjust, Texture2D icon, GameObject footAnchor, List<string> warnings)
        {
            // 末尾に必ず付く項目の数。合流先のサブメニューに元からある項目は動かさない
            int trailing = (withAdjust ? 1 : 0) + (footAnchor != null ? 1 : 0);
            List<int> pages = MenuItemUtility.SplitIntoPages(poses.Count, trailing, submenu.childCount);
            if (pages == null)
            {
                warnings.Add(
                    "The pose menu '" + submenu.name + "' is already full. " +
                    poses.Count + " pose(s) could not be added.");
                return;
            }

            Action<Transform> addTrailing = page =>
            {
                if (withAdjust) CreatePoseAdjust(page, icon);
                if (footAnchor != null) MenuItemUtility.CopyItem(footAnchor, page);
            };

            List<Transform> toggles = new List<Transform>();
            foreach (ResolvedPose pose in poses)
            {
                // 既存のポーズ項目と同じく保存しない
                toggles.Add(MenuItemUtility.CreateToggle(
                    submenu, pose.Entry.ResolveDisplayName(), pose.Entry.icon,
                    SupineNames.Parameters.Pose, pose.Value, false));
            }
            addTrailing(submenu);

            MenuItemUtility.DistributeToPages(submenu, toggles, pages, addTrailing, icon);
        }

        /// <summary>
        /// 姿勢の微調整を回すラジアル。
        /// ポーズのクリップに2つ以上キーがあると、その間をこの軸でスクラブできる。
        /// </summary>
        private static void CreatePoseAdjust(Transform parent, Texture2D icon)
        {
            ModularAvatarMenuItem item = MenuItemUtility.CreateItem(parent, PoseAdjustName);
            item.Control = new VRCExpressionsMenu.Control
            {
                name = PoseAdjustName,
                icon = icon,
                type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,

                // 開いている間だけ立つフラグが本体で、回す軸は subParameters 側
                parameter = new VRCExpressionsMenu.Control.Parameter
                    { name = SupineNames.Parameters.PoseAdjusting },
                value = 1f,
                subParameters = new[]
                {
                    new VRCExpressionsMenu.Control.Parameter { name = SupineNames.Parameters.PoseAdjust },
                },
                labels = new VRCExpressionsMenu.Control.Label[0],
            };

            // 同期と保存は MA Parameters の宣言が優先されるが、紛らわしくないよう揃えておく（保存しない）
            item.isSynced = true;
            item.isSaved = false;
            item.isDefault = false;
            item.automaticValue = true;

            EditorUtility.SetDirty(item);
        }

        private static void WarnIfOverCapacity(Transform menu, List<string> warnings)
        {
            if (menu.childCount <= MenuItemUtility.MenuCapacity) return;

            warnings.Add(
                "The '" + menu.name + "' menu now has " + menu.childCount +
                " items, which is over the VRChat limit of " + MenuItemUtility.MenuCapacity + ".");
        }
    }
}
