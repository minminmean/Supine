using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using Supine.Utilities;

namespace Supine
{
    /// <summary>
    /// 組込ウィンドウのうち、既存アニメーターのステートを選ばせる欄。
    /// 従来モードの継承元と、追加モードの入口・伏せの2か所で同じ仕組みを使う。
    /// </summary>
    public sealed partial class SupineCombinerEditor
    {
        // 追加先アニメーターから読み取った内容。毎フレーム作り直すと重いのでキャッシュする
        private AnimatorController _cachedAddTarget;
        private string[] _addTargetStateNames = new string[0];
        private bool _addTargetAlreadyCombined;
        private string _cachedLieDownEntryStateName;
        private List<string> _lieDownDestinationNames = new List<string>();

        // 継承元アニメーターのステート一覧
        private AnimatorController _cachedInheritSource;
        private string[] _inheritSourceStateNames = new string[0];

        private int _addTargetGeneration;
        private int _inheritSourceGeneration;

        /// <summary>
        /// どの既存ステートからモーションを引き継ぐかの選択。
        /// 名前が一致するものを初期選択にし、一致しなければユーザーに選ばせる。
        /// </summary>
        private void DrawInheritSourceStates(LocalizeDictionary localizeDict)
        {
            RefreshInheritSourceStates(BaseAnimatorResolver.FindBaseLayerController(_avatarDescriptor));

            EditorGUI.indentLevel++;

            using (new EditorGUI.DisabledGroupScope(
                !_options.shouldInheritOriginalAnimation || _inheritSourceStateNames.Length == 0))
            {
                string[] display = BuildDisplayNames(_inheritSourceStateNames, localizeDict);

                foreach (string templateStateName in InheritedStateTable.TemplateStateNames)
                {
                    string picked = DrawStateNamePopup(
                        InheritedStateTable.GetLabel(templateStateName, localizeDict),
                        InheritedStateTable.GetSourceStateName(_options, templateStateName),
                        _inheritSourceStateNames, display);

                    InheritedStateTable.SetSourceStateName(ref _options, templateStateName, picked);
                }

                EditorGUILayout.HelpBox(localizeDict.inherit_state_help, MessageType.Info);
            }

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// 継承元アニメーターのステート名一覧を作り直す。名前が一致するものを初期選択にする。
        /// </summary>
        private void RefreshInheritSourceStates(AnimatorController source)
        {
            bool sourceChanged = source != _cachedInheritSource;
            if (!sourceChanged && _inheritSourceGeneration == _refreshGeneration) return;

            _inheritSourceGeneration = _refreshGeneration;
            _cachedInheritSource = source;
            _inheritSourceStateNames = BuildStateNames(source);

            // 名前一致だけだと、ステート名を変えているアバターで軒並み「なし」になる。
            // 追加モードと同じく、遷移の構造からも推測する
            Dictionary<string, string> inferred =
                SupineLocomotionAdder.InferInheritSourceStateNames(source);

            foreach (string templateStateName in InheritedStateTable.TemplateStateNames)
            {
                string current = InheritedStateTable.GetSourceStateName(_options, templateStateName);

                // 中身だけ変わった場合は、選んでいたステートが消えていたときだけ選び直す
                if (!sourceChanged && current != null &&
                    (current.Length == 0 || DisplayIndexOf(_inheritSourceStateNames, current) > 0)) continue;

                // 推測できたものを初期選択に、できなければ「なし」（＝ごろ寝システムのアニメーション）
                InheritedStateTable.SetSourceStateName(
                    ref _options, templateStateName,
                    AutoSelectStateName(
                        _inheritSourceStateNames,
                        inferred.TryGetValue(templateStateName, out string guess) ? guess : templateStateName));
            }
        }

        /// <summary>
        /// 追加先の解決結果と、どのステートをごろ寝システムに使うかの選択。
        ///
        /// 既成のアニメーターはステート名を変えていることが多く、名前一致だけでは拾えない。
        /// 追加先のステート名を一覧で出し、名前が一致するものを初期選択にしたうえで、
        /// 一致しない場合はユーザーが選び直せるようにする。
        /// </summary>
        private void DrawAddTargetStates(LocalizeDictionary localizeDict)
        {
            BaseAnimatorResolution resolution =
                BaseAnimatorResolver.Resolve(_avatarDescriptor, _options.addTargetOverride);

            DrawAddTargetHelp(localizeDict, resolution);
            RefreshAddTargetStates(resolution.controller);

            EditorGUI.indentLevel++;

            using (new EditorGUI.DisabledGroupScope(_addTargetStateNames.Length == 0))
            {
                string[] display = BuildDisplayNames(_addTargetStateNames, localizeDict);

                string previousEntry = _options.entryStateName;
                _options.entryStateName = DrawStateNamePopup(
                    localizeDict.entry_state, _options.entryStateName, _addTargetStateNames, display);

                // 入口が変わったら、そこから伏せへ降りる先を推測して選び直す
                if (_options.entryStateName != previousEntry)
                {
                    _options.proneStateName =
                        InferProneStateName(resolution.controller, _options.entryStateName);
                }

                _options.proneStateName = DrawStateNamePopup(
                    localizeDict.prone_state, _options.proneStateName, _addTargetStateNames, display);

                EditorGUILayout.HelpBox(localizeDict.add_state_help, MessageType.Info);

                RefreshLieDownDestinations(resolution.controller, false);

                if (SupineLocomotionAdder.HasConflictingLieDownDestination(
                        _lieDownDestinationNames, _options.proneStateName))
                {
                    EditorGUILayout.HelpBox(localizeDict.add_state_conflict, MessageType.Warning);
                }

                if (_addTargetAlreadyCombined)
                {
                    EditorGUILayout.HelpBox(localizeDict.add_state_already_combined, MessageType.Warning);
                }
            }

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// 追加先のステート名一覧を作り直す。名前が一致するものを初期選択にする。
        /// 一致しなければ「なし」にして、ユーザーに選ばせる。
        /// </summary>
        private void RefreshAddTargetStates(AnimatorController target)
        {
            bool targetChanged = target != _cachedAddTarget;
            if (!targetChanged && _addTargetGeneration == _refreshGeneration) return;

            _addTargetGeneration = _refreshGeneration;
            _cachedAddTarget = target;
            _addTargetStateNames = BuildStateNames(target);
            _addTargetAlreadyCombined =
                SupineLocomotionAdder.IsSupineCombined(Template.LoadController(), target);

            // 追加先が変わったなら選び直す。中身だけ変わった場合は、
            // 選んでいたステートが消えていたときだけ選び直して、手動の指定を無駄に壊さない
            if (targetChanged || !IsSelectableStateName(_options.entryStateName))
            {
                _options.entryStateName = AutoSelectStateName(
                    _addTargetStateNames, SupineLocomotionAdder.InferEntryStateName(target));
            }
            if (targetChanged || !IsSelectableStateName(_options.proneStateName))
            {
                _options.proneStateName = InferProneStateName(target, _options.entryStateName);
            }

            RefreshLieDownDestinations(target, true);
        }

        /// <summary>一覧から選べる状態か。「なし」も選択として有効</summary>
        private bool IsSelectableStateName(string stateName)
        {
            return stateName != null && (stateName.Length == 0 ||
                DisplayIndexOf(_addTargetStateNames, stateName) > 0);
        }

        /// <summary>
        /// 入口ステートから降りる先を控え直す。競合の判定を毎フレーム走査しないためのキャッシュ。
        /// </summary>
        private void RefreshLieDownDestinations(AnimatorController target, bool force)
        {
            if (!force && _options.entryStateName == _cachedLieDownEntryStateName) return;

            _cachedLieDownEntryStateName = _options.entryStateName;
            _lieDownDestinationNames = SupineLocomotionAdder.CollectLieDownDestinationNames(
                Template.LoadController(), target, _options.entryStateName);
        }

        /// <summary>
        /// 伏せ状態にあたるステートを推測する。
        ///
        /// 入口ステートから Upright less than で降りる先があれば、それがそのアニメーターの伏せ状態。
        /// ステート名を変えていても拾えるので、名前一致より優先する。
        /// </summary>
        private string InferProneStateName(AnimatorController target, string entryStateName)
        {
            List<AnimatorState> destinations =
                SupineLocomotionAdder.CollectLieDownDestinations(target, entryStateName);

            if (destinations.Count > 0) return destinations[0].name;

            return AutoSelectStateName(_addTargetStateNames, SupineLocomotionAdder.ProneStateName);
        }

        /// <summary>
        /// ステート名のPopupを1つ描く。
        /// </summary>
        /// <returns>選ばれたステート名。「なし」なら空文字</returns>
        private static string DrawStateNamePopup(
            string label, string current, string[] stateNames, string[] displayNames)
        {
            int index = DisplayIndexOf(stateNames, current);
            int picked = EditorGUILayout.Popup(label, index, displayNames);

            return picked == index ? current : StateNameAtDisplayIndex(stateNames, picked);
        }

        private static string[] BuildStateNames(AnimatorController controller)
        {
            if (controller == null || controller.layers.Length == 0 ||
                controller.layers[0].stateMachine == null)
            {
                return new string[0];
            }

            // 追加処理と同じ索引から作る。ここで選んだ名前がそのまま向こうで引ける
            Dictionary<string, AnimatorState> index =
                AnimatorStateUtility.BuildStateIndex(controller.layers[0].stateMachine);

            string[] names = new string[index.Count];
            index.Keys.CopyTo(names, 0);
            return names;
        }

        /// <summary>
        /// Popupに出す並び。先頭に「なし」を足す。
        /// 表示名は言語で変わるので、キャッシュせず毎回組み立てる。
        /// </summary>
        private static string[] BuildDisplayNames(string[] stateNames, LocalizeDictionary localizeDict)
        {
            string[] displayNames = new string[stateNames.Length + 1];
            displayNames[0] = localizeDict.state_none;
            stateNames.CopyTo(displayNames, 1);
            return displayNames;
        }

        /// <summary>名前が一覧にあればそれを、無ければ「なし」を初期選択にする</summary>
        private static string AutoSelectStateName(string[] stateNames, string name)
        {
            return DisplayIndexOf(stateNames, name) > 0 ? name : string.Empty;
        }

        /// <summary>Popupでの位置。空文字は「なし」で先頭、未指定(null)は空欄の-1</summary>
        private static int DisplayIndexOf(string[] stateNames, string name)
        {
            if (name == null) return -1;
            if (name.Length == 0) return 0;

            for (int i = 0; i < stateNames.Length; i++)
            {
                if (stateNames[i] == name) return i + 1;
            }
            return -1;
        }

        private static string StateNameAtDisplayIndex(string[] stateNames, int displayIndex)
        {
            if (displayIndex <= 0 || displayIndex > stateNames.Length) return string.Empty;
            return stateNames[displayIndex - 1];
        }

        /// <summary>
        /// 自動取得の結果を実名で見せる。手動指定時はフィールドが答えなので出さない。
        /// </summary>
        private void DrawAddTargetHelp(LocalizeDictionary localizeDict, BaseAnimatorResolution resolution)
        {
            if (_options.addTargetOverride != null) return;
            if (_avatarDescriptor == null) return;

            switch (resolution.source)
            {
                case BaseAnimatorSource.AvatarDescriptor:
                    EditorGUILayout.HelpBox(
                        string.Format(localizeDict.add_target_resolved, resolution.controller.name),
                        MessageType.None);
                    break;

                case BaseAnimatorSource.VrcDefault:
                    EditorGUILayout.HelpBox(localizeDict.add_target_vrc_default, MessageType.Info);
                    break;

                default:
                    EditorGUILayout.HelpBox(localizeDict.check_failure_add_target_message, MessageType.Warning);
                    break;
            }
        }
    }
}
