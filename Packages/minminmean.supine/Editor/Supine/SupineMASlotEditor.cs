using System;
using UnityEditor;
using UnityEngine;
using Supine.Utilities;

namespace Supine
{
    /// <summary>
    /// SupineMASlot のインスペクタ。組込時の設定を読み取り専用で見せる。
    ///
    /// 項目名は組込ウィンドウと同じ文言を使い、並びもウィンドウに揃える。
    /// ウィンドウと見比べて、同じ設定を入れ直せるようにするため。
    /// </summary>
    [CustomEditor(typeof(SupineMASlot))]
    internal sealed class SupineMASlotEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            LocalizeDictionary dict = JsonHelper.GetLocalizedTexts(SupineCombinerEditor.SavedLanguage);
            SupineCombineRecord record = ((SupineMASlot)target).record;

            EditorGUILayout.LabelField(dict.record_header, EditorStyles.boldLabel);

            if (record == null || !record.HasRecord)
            {
                EditorGUILayout.HelpBox(dict.record_missing, MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(dict.record_version, record.version);
            EditorGUILayout.LabelField(dict.record_combined_at, record.combinedAt);

            EditorGUILayout.Space();

            EditorGUILayout.LabelField(dict.combine_mode, ModeLabel(record.mode, dict));

            using (new EditorGUI.IndentLevelScope())
            {
                if (record.mode == SupineCombineMode.Add.ToString())
                {
                    DrawAddRecord(record, dict);
                }
                else
                {
                    DrawStandardRecord(record, dict);
                }
            }

            EditorGUILayout.Space();

            EditorGUILayout.LabelField(dict.sit1, EnumLabel<SittingPose>(record.sittingPose1, p => SittingPoseTable.GetLabel(p, dict)));
            EditorGUILayout.LabelField(dict.sit2, EnumLabel<SittingPose>(record.sittingPose2, p => SittingPoseTable.GetLabel(p, dict)));

            DrawCrouchRecord(record, dict);

            EditorGUILayout.Space();

            EditorGUILayout.LabelField(
                dict.record_pose_packs,
                record.posePacks == null || record.posePacks.Count == 0
                    ? dict.record_none
                    : string.Join(", ", record.posePacks));
        }

        private static void DrawStandardRecord(SupineCombineRecord record, LocalizeDictionary dict)
        {
            DrawToggle(dict.inherit_original, record.inheritOriginalAnimation);

            if (record.inheritOriginalAnimation)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawAnimator(dict.record_source_animator, record.sourceAnimator);
                    EditorGUILayout.LabelField(dict.inherit_standing_state, StateLabel(record.inheritStandingState, dict));
                    EditorGUILayout.LabelField(dict.inherit_crouching_state, StateLabel(record.inheritCrouchingState, dict));
                    EditorGUILayout.LabelField(dict.inherit_prone_state, StateLabel(record.inheritProneState, dict));
                }
            }

            DrawToggle(dict.disable_jump_motion, record.disableJumpMotion);

            using (new EditorGUI.IndentLevelScope())
            {
                DrawToggle(dict.enable_jump_at_desktop, record.enableJumpAtDesktop);
            }
        }

        private static void DrawAddRecord(SupineCombineRecord record, LocalizeDictionary dict)
        {
            DrawAnimator(dict.add_target, record.sourceAnimator);

            if (!record.addTargetSpecified)
            {
                EditorGUILayout.LabelField(" ", dict.record_add_target_auto, EditorStyles.miniLabel);
            }

            EditorGUILayout.LabelField(dict.entry_state, StateLabel(record.entryState, dict));
            EditorGUILayout.LabelField(dict.prone_state, StateLabel(record.proneState, dict));
        }

        /// <summary>
        /// 既存のしゃがみ切り替えを優先したときは、このツールのしゃがみは組み込まれていない。
        /// 既定のポーズや滑りの設定は意味を持たないので出さない。
        /// </summary>
        private static void DrawCrouchRecord(SupineCombineRecord record, LocalizeDictionary dict)
        {
            if (record.keepExistingCrouchPose)
            {
                DrawToggle(dict.crouch_keep_existing, true);
                return;
            }

            // パックのしゃがみは表示名で残しているので、列挙体に読めなければそのまま出す
            EditorGUILayout.LabelField(
                dict.crouch_pose,
                string.IsNullOrEmpty(record.defaultCrouchPose)
                    ? dict.record_none
                    : EnumLabel<CrouchPose>(record.defaultCrouchPose, p => CrouchPoseTable.GetLabel(p, dict)));

            DrawToggle(dict.crouch_slide, record.crouchSlide);
        }

        private static void DrawToggle(string label, bool value)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ToggleLeft(label, value);
            }
        }

        private static void DrawAnimator(string label, RuntimeAnimatorController animator)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(label, animator, typeof(RuntimeAnimatorController), false);
            }
        }

        private static string ModeLabel(string mode, LocalizeDictionary dict)
        {
            return EnumLabel<SupineCombineMode>(mode, m => SupineCombineModeTable.GetLabel(m, dict));
        }

        /// <summary>空文字は「対応するステートを持たせない」指定</summary>
        private static string StateLabel(string stateName, LocalizeDictionary dict)
        {
            return string.IsNullOrEmpty(stateName) ? dict.state_none : stateName;
        }

        /// <summary>
        /// 名前で残した列挙体を表示用の文言にする。
        /// 後の版で項目が消えるなどして読めなければ、残っている名前をそのまま出す。
        /// </summary>
        private static string EnumLabel<T>(string name, Func<T, string> toLabel) where T : struct
        {
            if (!string.IsNullOrEmpty(name) && Enum.TryParse(name, out T value) && Enum.IsDefined(typeof(T), value))
            {
                return toLabel(value);
            }
            return name;
        }
    }
}
