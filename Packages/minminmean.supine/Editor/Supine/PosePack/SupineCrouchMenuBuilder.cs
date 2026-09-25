using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Supine.Utilities;
using ModularAvatarMenuItem = nadena.dev.modular_avatar.core.ModularAvatarMenuItem;
using ModularAvatarParameters = nadena.dev.modular_avatar.core.ModularAvatarParameters;
using ParameterConfig = nadena.dev.modular_avatar.core.ParameterConfig;

namespace Supine.PosePack
{
    /// <summary>
    /// SupineMA Prefab のしゃがみポーズまわりを整える。
    /// パックが足したしゃがみの項目、既定のしゃがみの印、既存を優先したときの後片付け。
    ///
    /// パックのしゃがみは Crouch Poses サブメニューへ並べる。
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

        /// <summary>
        /// 組み込んだしゃがみに合わせてメニューを整える。
        /// </summary>
        public static void Build(
            GameObject maPrefabInstance, SupineCrouchInjection injection, List<string> warnings)
        {
            if (maPrefabInstance == null) return;

            AddPackPoses(maPrefabInstance, injection.PackPoses, warnings);

            // 送りページまで出来上がってから印を付ける。項目はどのページにも居うる
            SetMenuParameterDefault(maPrefabInstance, injection.DefaultValue);
            MarkDefaultMenuItem(maPrefabInstance, injection.DefaultValue);
        }

        /// <summary>
        /// 既存のしゃがみ切り替えを活かすなら、こちらのメニューもパラメータも要らない。
        /// 残すと「押しても何も起きない項目」が並び、同期枠も8bit無駄になる。
        /// </summary>
        public static void Remove(GameObject maPrefabInstance)
        {
            if (maPrefabInstance == null) return;

            RemoveMenu(maPrefabInstance);
            RemoveParameter(maPrefabInstance);
        }

        private static void AddPackPoses(
            GameObject maPrefabInstance, IReadOnlyList<ResolvedCrouchPose> poses, List<string> warnings)
        {
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

        private static void SetMenuParameterDefault(GameObject maPrefabInstance, float value)
        {
            ModularAvatarParameters parameters = FindParameters(maPrefabInstance);
            if (parameters == null) return;

            for (int i = 0; i < parameters.parameters.Count; i++)
            {
                if (parameters.parameters[i].nameOrPrefix != CrouchPoseParameter) continue;

                ParameterConfig config = parameters.parameters[i];
                config.defaultValue = value;
                config.hasExplicitDefaultValue = true;
                parameters.parameters[i] = config;

                EditorUtility.SetDirty(parameters);
                return;
            }
        }

        /// <summary>
        /// メニュー上でも既定のポーズに印を移す。
        ///
        /// 並びの何番目かではなく、項目が書き込む値で合わせる。
        /// 項目が増えてページ送りが出ると、番号と枠番号は一致しなくなる。
        /// </summary>
        private static void MarkDefaultMenuItem(GameObject maPrefabInstance, float value)
        {
            Transform menu = MenuItemUtility.FindDescendant(maPrefabInstance.transform, CrouchMenuName);
            if (menu == null) return;

            foreach (ModularAvatarMenuItem item in menu.GetComponentsInChildren<ModularAvatarMenuItem>(true))
            {
                if (item.Control == null) continue;
                if (item.Control.parameter == null) continue;
                if (item.Control.parameter.name != CrouchPoseParameter) continue;

                item.isDefault = SupineCrouchValues.Approximately(item.Control.value, value);
                EditorUtility.SetDirty(item);
            }
        }

        private static void RemoveMenu(GameObject maPrefabInstance)
        {
            Transform menu = MenuItemUtility.FindDescendant(maPrefabInstance.transform, CrouchMenuName);
            if (menu == null) return;

            Undo.DestroyObjectImmediate(menu.gameObject);
        }

        private static void RemoveParameter(GameObject maPrefabInstance)
        {
            ModularAvatarParameters parameters = FindParameters(maPrefabInstance);
            if (parameters == null) return;

            for (int i = parameters.parameters.Count - 1; i >= 0; i--)
            {
                if (parameters.parameters[i].nameOrPrefix != CrouchPoseParameter) continue;

                Undo.RecordObject(parameters, "Remove Crouch Pose Parameter");
                parameters.parameters.RemoveAt(i);
                EditorUtility.SetDirty(parameters);
            }
        }

        private static ModularAvatarParameters FindParameters(GameObject maPrefabInstance)
        {
            return maPrefabInstance == null
                ? null
                : maPrefabInstance.GetComponentInChildren<ModularAvatarParameters>(true);
        }
    }
}
