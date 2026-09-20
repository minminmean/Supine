using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Avatars.ScriptableObjects;
using ModularAvatarMenuItem = nadena.dev.modular_avatar.core.ModularAvatarMenuItem;
using SubmenuSource = nadena.dev.modular_avatar.core.SubmenuSource;

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
    /// ポーズ以外の項目は Misc にまとめてあるので、トップレベルにはポーズの
    /// メニューだけが並ぶ。Misc は常に末尾へ送り直す。
    /// </summary>
    internal static class SupinePoseMenuBuilder
    {
        /// <summary>パックのサブメニューを並べる先</summary>
        private const string RootMenuName = "Suimin";

        /// <summary>ポーズ以外をまとめた項目。常に末尾に置く</summary>
        private const string MiscMenuName = "Misc";

        /// <summary>VRChatの1メニューあたりの項目数上限</summary>
        private const int MenuCapacity = 8;

        /// <summary>ページが溢れたときに次ページへ送る項目の名前</summary>
        private const string NextPageName = "Next";

        /// <summary>各パックのメニュー末尾に添える項目。既存のものを複製して使う</summary>
        private const string FootAnchorName = "Foot Anchor";

        public static void Build(
            GameObject maPrefabInstance, IReadOnlyList<ResolvedPose> poses, List<string> warnings)
        {
            if (poses == null || poses.Count == 0) return;

            Transform posesRoot = FindDescendant(maPrefabInstance.transform, RootMenuName);
            if (posesRoot == null)
            {
                warnings.Add(
                    "Could not find the '" + RootMenuName + "' menu in the Supine MA prefab. " +
                    "Pose pack menu items were not created.");
                return;
            }

            // 置き場所ごとにまとめる。EX版のように既存のサブメニュー名を名乗れば、そこへ合流する
            List<string> folderOrder = new List<string>();
            Dictionary<string, List<ResolvedPose>> folders = new Dictionary<string, List<ResolvedPose>>();

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
            }

            // 雛形は自分で作った複製を拾わないよう、1つも生やす前に確保しておく
            GameObject footAnchor = FindDescendantObject(maPrefabInstance.transform, FootAnchorName);
            if (footAnchor == null)
            {
                warnings.Add(
                    "Could not find the '" + FootAnchorName + "' menu item to copy. " +
                    "Pose pack menus were created without it.");
            }

            foreach (string folder in folderOrder)
            {
                Transform submenu = FindChild(posesRoot, folder) ?? CreateSubMenu(posesRoot, folder);
                FillPages(submenu, folders[folder], footAnchor, warnings);
            }

            // パックを足したぶん Misc が押し出されるので、末尾へ送り直す
            Transform misc = FindChild(posesRoot, MiscMenuName);
            if (misc != null) misc.SetAsLastSibling();

            WarnIfOverCapacity(posesRoot, warnings);
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
        /// Foot Anchor はどのページの末尾にも置く。ページを送った先で使えないと、
        /// 足を固定するためだけに前のページへ戻ることになる。
        /// 並びは「ポーズ … / Foot Anchor / Next」。
        /// </summary>
        private static void FillPages(
            Transform page, List<ResolvedPose> poses, GameObject footAnchor, List<string> warnings)
        {
            int index = 0;

            while (true)
            {
                // 末尾の Foot Anchor のぶんを先に引く
                int capacity = MenuCapacity - page.childCount - 1;
                int remaining = poses.Count - index;

                // 収まらないなら、送りのぶんもさらに1枠要る
                bool needsNextPage = remaining > capacity;
                if (needsNextPage) capacity--;

                if (capacity <= 0)
                {
                    warnings.Add(
                        "The pose menu '" + page.name + "' is already full. " +
                        remaining + " pose(s) could not be added.");
                    return;
                }

                int count = Mathf.Min(capacity, remaining);
                for (int i = 0; i < count; i++)
                {
                    CreateToggle(page, poses[index + i]);
                }
                index += count;

                if (footAnchor != null) CopyItem(footAnchor, page);

                if (index >= poses.Count) return;

                page = CreateSubMenu(page, NextPageName);
            }
        }

        /// <summary>
        /// 既存のメニュー項目をそのまま複製して足す。
        /// 作り直すとアイコンや同期の設定を書き写すことになり、元を直したときにずれる。
        /// </summary>
        private static void CopyItem(GameObject template, Transform parent)
        {
            GameObject copy = Object.Instantiate(template, parent);
            Undo.RegisterCreatedObjectUndo(copy, "Create Supine Pose Menu");
            copy.name = template.name;
        }

        private static Transform CreateSubMenu(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create Supine Pose Menu");
            go.transform.SetParent(parent, false);

            ModularAvatarMenuItem item = Undo.AddComponent<ModularAvatarMenuItem>(go);
            item.Control = new VRCExpressionsMenu.Control
            {
                name = name,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = string.Empty },
                subParameters = new VRCExpressionsMenu.Control.Parameter[0],
                labels = new VRCExpressionsMenu.Control.Label[0],
            };
            item.MenuSource = SubmenuSource.Children;

            EditorUtility.SetDirty(item);
            return go.transform;
        }

        private static void CreateToggle(Transform parent, ResolvedPose pose)
        {
            string displayName = pose.Entry.ResolveDisplayName();

            GameObject go = new GameObject(displayName);
            Undo.RegisterCreatedObjectUndo(go, "Create Supine Pose Menu");
            go.transform.SetParent(parent, false);

            ModularAvatarMenuItem item = Undo.AddComponent<ModularAvatarMenuItem>(go);
            item.Control = new VRCExpressionsMenu.Control
            {
                name = displayName,
                icon = pose.Entry.icon,
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = SupinePoseInjector.PoseParameter },
                value = pose.Value,
                subParameters = new VRCExpressionsMenu.Control.Parameter[0],
                labels = new VRCExpressionsMenu.Control.Label[0],
            };

            // 既存のポーズ項目と同じ扱いにする。automaticValue を切らないと
            // せっかく採番した VRCSupine の値を Modular Avatar が振り直してしまう
            item.MenuSource = SubmenuSource.Children;
            item.isSynced = true;
            item.isSaved = true;
            item.isDefault = false;
            item.automaticValue = false;

            EditorUtility.SetDirty(item);
        }

        private static void WarnIfOverCapacity(Transform menu, List<string> warnings)
        {
            if (menu.childCount <= MenuCapacity) return;

            warnings.Add(
                "The '" + menu.name + "' menu now has " + menu.childCount +
                " items, which is over the VRChat limit of " + MenuCapacity + ".");
        }

        private static Transform FindChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
            }
            return null;
        }

        private static GameObject FindDescendantObject(Transform root, string name)
        {
            Transform found = FindDescendant(root, name);
            return found == null ? null : found.gameObject;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (Transform child in root)
            {
                if (child.name == name) return child;

                Transform found = FindDescendant(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
