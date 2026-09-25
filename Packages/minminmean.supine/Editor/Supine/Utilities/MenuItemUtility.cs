using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Avatars.ScriptableObjects;
using ModularAvatarMenuItem = nadena.dev.modular_avatar.core.ModularAvatarMenuItem;
using SubmenuSource = nadena.dev.modular_avatar.core.SubmenuSource;

namespace Supine.Utilities
{
    /// <summary>
    /// SupineMA Prefab のメニュー項目（MA Menu Item）を作る・探す・ページに分ける。
    /// </summary>
    internal static class MenuItemUtility
    {
        /// <summary>VRChatの1メニューあたりの項目数上限</summary>
        public const int MenuCapacity = 8;

        private const string UndoName = "Create Supine Menu";

        /// <summary>
        /// 空のメニュー項目を1つ足す。種類ごとの設定は呼び出し側が Control に入れる。
        /// </summary>
        public static ModularAvatarMenuItem CreateItem(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, UndoName);
            go.transform.SetParent(parent, false);

            ModularAvatarMenuItem item = Undo.AddComponent<ModularAvatarMenuItem>(go);
            item.MenuSource = SubmenuSource.Children;
            return item;
        }

        public static Transform CreateSubMenu(Transform parent, string name, Texture2D icon)
        {
            ModularAvatarMenuItem item = CreateItem(parent, name);
            item.Control = new VRCExpressionsMenu.Control
            {
                name = name,
                icon = icon,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = string.Empty },
                subParameters = new VRCExpressionsMenu.Control.Parameter[0],
                labels = new VRCExpressionsMenu.Control.Label[0],
            };

            EditorUtility.SetDirty(item);
            return item.transform;
        }

        /// <summary>
        /// パラメータへ決まった値を書き込むトグル。
        ///
        /// automaticValue は必ず切る。入れたままだと、採番した値を Modular Avatar が振り直してしまう。
        /// </summary>
        public static Transform CreateToggle(
            Transform parent, string name, Texture2D icon, string parameter, float value, bool isSaved)
        {
            ModularAvatarMenuItem item = CreateItem(parent, name);
            item.Control = new VRCExpressionsMenu.Control
            {
                name = name,
                icon = icon,
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter },
                value = value,
                subParameters = new VRCExpressionsMenu.Control.Parameter[0],
                labels = new VRCExpressionsMenu.Control.Label[0],
            };

            item.isSynced = true;
            item.isSaved = isSaved;
            item.isDefault = false;
            item.automaticValue = false;

            EditorUtility.SetDirty(item);
            return item.transform;
        }

        /// <summary>
        /// 既存のメニュー項目をそのまま複製して足す。
        /// 作り直すとアイコンや同期の設定を書き写すことになり、元を直したときにずれる。
        /// </summary>
        public static void CopyItem(GameObject template, Transform parent)
        {
            GameObject copy = UnityEngine.Object.Instantiate(template, parent);
            Undo.RegisterCreatedObjectUndo(copy, UndoName);
            copy.name = template.name;
        }

        /// <summary>メニュー項目のアイコン。送りページは親のものを借りる</summary>
        public static Texture2D GetIcon(Transform menu)
        {
            ModularAvatarMenuItem item = menu.GetComponent<ModularAvatarMenuItem>();
            return item != null && item.Control != null ? item.Control.icon : null;
        }

        public static Transform FindChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
            }
            return null;
        }

        public static Transform FindDescendant(Transform root, string name)
        {
            foreach (Transform child in root)
            {
                if (child.name == name) return child;

                Transform found = FindDescendant(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// 項目をいくつずつページに載せるかを決める。
        ///
        /// どのページも末尾に trailingCount 個の項目を持ち、次のページがあるなら
        /// さらに送りの1枠を使う。1ページ目には firstPageUsed 個の動かせない項目が先に並ぶ。
        /// 全部を数え終えてから割り振るので、1個しか溢れないのに送りが生えることは無い。
        /// </summary>
        /// <returns>ページごとの個数。項目を1つも置けないページが出るなら null</returns>
        public static List<int> SplitIntoPages(int itemCount, int trailingCount, int firstPageUsed)
        {
            List<int> counts = new List<int>();
            int remaining = itemCount;
            int used = firstPageUsed;

            while (true)
            {
                int capacity = MenuCapacity - used - trailingCount;

                // 収まらないなら、送りのぶんもさらに1枠要る
                bool needsNextPage = remaining > capacity;
                if (needsNextPage) capacity--;

                if (capacity <= 0) return null;

                int count = Mathf.Min(capacity, remaining);
                counts.Add(count);
                remaining -= count;

                if (!needsNextPage) return counts;
                used = 0;
            }
        }

        /// <summary>
        /// <see cref="SplitIntoPages"/> の割り振りどおりに、項目を送りページへ移す。
        ///
        /// items は firstPage の子として並んでいること。先頭の pageCounts[0] 個はそのまま残す。
        /// 2ページ目以降は「項目 … / addTrailing が足す項目 / 送り」の順になる。
        /// 1ページ目の末尾の項目は呼び出し側が用意する。
        /// </summary>
        public static void DistributeToPages(
            Transform firstPage, IList<Transform> items, IList<int> pageCounts,
            Action<Transform> addTrailing, Texture2D icon)
        {
            Transform page = firstPage;
            int index = pageCounts.Count > 0 ? pageCounts[0] : items.Count;

            for (int p = 1; p < pageCounts.Count; p++)
            {
                page = CreateSubMenu(page, SupineNames.Menus.NextPage, icon);
                page.SetAsLastSibling();

                for (int i = 0; i < pageCounts[p]; i++)
                {
                    Undo.SetTransformParent(items[index], page, UndoName);
                    items[index].SetAsLastSibling();
                    index++;
                }

                addTrailing?.Invoke(page);
            }
        }
    }
}
