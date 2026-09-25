using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Avatars.ScriptableObjects;
using ModularAvatarMenuItem = nadena.dev.modular_avatar.core.ModularAvatarMenuItem;
using SubmenuSource = nadena.dev.modular_avatar.core.SubmenuSource;

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

        /// <summary>VRChatのメニュー1ページに入る項目数</summary>
        private const int MenuCapacity = 8;

        private const string NextPageName = SupineNames.Menus.NextPage;

        public static void Build(
            GameObject maPrefabInstance, IReadOnlyList<ResolvedCrouchPose> poses, List<string> warnings)
        {
            if (maPrefabInstance == null) return;
            if (poses == null || poses.Count == 0) return;

            Transform menu = FindDescendant(maPrefabInstance.transform, CrouchMenuName);
            if (menu == null)
            {
                warnings.Add(
                    "Could not find the '" + CrouchMenuName + "' menu. " +
                    poses.Count + " crouch pose(s) were not added.");
                return;
            }

            Texture2D icon = ResolveIcon(menu);

            foreach (ResolvedCrouchPose pose in poses)
            {
                CreateToggle(menu, pose);
            }

            Paginate(menu, icon);
        }

        /// <summary>
        /// 溢れたぶんを送りページへ移す。
        ///
        /// 先に項目を全部足してから畳む。足しながらページを割ると、
        /// 「あと何個来るか」を知らないまま送りを作ることになり、
        /// 1個しか溢れていないのに送りページが生える場合が出る。
        /// </summary>
        private static void Paginate(Transform page, Texture2D icon)
        {
            while (page.childCount > MenuCapacity)
            {
                // 送り自身が1枠使うので、このページに残せるのは MenuCapacity - 1 個
                List<Transform> overflow = new List<Transform>();
                for (int i = MenuCapacity - 1; i < page.childCount; i++)
                {
                    overflow.Add(page.GetChild(i));
                }

                Transform next = CreateSubMenu(page, NextPageName, icon);
                foreach (Transform child in overflow)
                {
                    child.SetParent(next, false);
                }

                next.SetSiblingIndex(MenuCapacity - 1);
                page = next;
            }
        }

        /// <summary>
        /// 送りページのアイコンは親のものを借りる。
        /// 無地のまま並ぶより、同じ絵が続くほうが同じ話の続きだと分かる。
        /// </summary>
        private static Texture2D ResolveIcon(Transform menu)
        {
            ModularAvatarMenuItem item = menu.GetComponent<ModularAvatarMenuItem>();
            return item != null && item.Control != null ? item.Control.icon : null;
        }

        private static Transform CreateSubMenu(Transform parent, string name, Texture2D icon)
        {
            GameObject go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create Crouch Pose Menu");
            go.transform.SetParent(parent, false);

            ModularAvatarMenuItem item = Undo.AddComponent<ModularAvatarMenuItem>(go);
            item.Control = new VRCExpressionsMenu.Control
            {
                name = name,
                icon = icon,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = string.Empty },
                subParameters = new VRCExpressionsMenu.Control.Parameter[0],
                labels = new VRCExpressionsMenu.Control.Label[0],
            };
            item.MenuSource = SubmenuSource.Children;

            EditorUtility.SetDirty(item);
            return go.transform;
        }

        private static void CreateToggle(Transform parent, ResolvedCrouchPose pose)
        {
            string displayName = pose.Entry.ResolveDisplayName();

            GameObject go = new GameObject(displayName);
            Undo.RegisterCreatedObjectUndo(go, "Create Crouch Pose Menu");
            go.transform.SetParent(parent, false);

            ModularAvatarMenuItem item = Undo.AddComponent<ModularAvatarMenuItem>(go);
            item.Control = new VRCExpressionsMenu.Control
            {
                name = displayName,
                icon = pose.Entry.icon,
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = CrouchPoseParameter },
                value = pose.Value,
                subParameters = new VRCExpressionsMenu.Control.Parameter[0],
                labels = new VRCExpressionsMenu.Control.Label[0],
            };

            // 既存のしゃがみ項目と同じ扱いにする。automaticValue を切らないと
            // せっかく割り当てた枠番号を Modular Avatar が振り直してしまう
            item.MenuSource = SubmenuSource.Children;
            item.isSynced = true;
            item.isSaved = true;
            item.isDefault = false;
            item.automaticValue = false;

            EditorUtility.SetDirty(item);
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
