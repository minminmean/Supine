using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Supine.Utilities;

namespace Supine
{
    /// <summary>追加結果。警告は英語でコンソールに出す前提でここに積む</summary>
    internal sealed class SupineAddReport
    {
        public readonly List<string> Warnings = new List<string>();
        public IReadOnlyDictionary<string, string> RenamedStates = new Dictionary<string, string>();
        public bool Succeeded;

        public bool HasWarnings => Warnings.Count > 0;
    }

    /// <summary>
    /// ごろ寝システムのステート群を、既存Locomotionのコピーへ追加する。
    ///
    /// テンプレートのレイヤー0は「VRChat既定Locomotion + ごろ寝の追加分」という構造なので、
    /// 既定Locomotionとの差分だけを追加先へ移植すれば、既存のアニメーターを保ったまま合流できる。
    /// 差分の基準を既定Locomotionに置くことで、テンプレート固有のステート名を持たずに済み、
    /// 通常版とEX版のようにテンプレートが違っても同じロジックが成立する。
    ///
    /// 注意: 追加されているのはステートだけではない。テンプレートは Standing / Crouching / Prone の
    /// 既存遷移にも条件を足している（例: Crouching→Prone に VRCSupine == 0）。
    /// Crouching がごろ寝システムへの起点で、Prone はごろ寝の1ポーズ（VRCSupine == 0）として扱われるため、
    /// ステートを足すだけでは既存の Crouching→Prone が先に成立してごろ寝に入れない。
    ///
    /// 一方でジャンプ・落下まわりと JumpAndFall はごろ寝システムと無関係なので対象外にする。
    /// ジャンプの有効・無効はごろ寝システムのアニメーターを書き換えるためのオプションであって、
    /// 既存アニメーターの挙動を変えるためのものではない。
    /// </summary>
    internal sealed class SupineLocomotionAdder
    {
        /// <summary>
        /// ごろ寝システムの入口として使うステート（テンプレート側の名前）。
        /// ここからVRCSupineの値に応じて各ポーズへ振り分ける。
        /// </summary>
        internal const string EntryStateName = "Crouching";

        /// <summary>
        /// 既定の伏せポーズ（VRCSupine == 0）として扱うステート（テンプレート側の名前）。
        /// </summary>
        internal const string ProneStateName = "Prone";

        /// <summary>
        /// 「しゃがみから伏せへ降りる」遷移の目印になるパラメータ。VRChat標準のUpright。
        /// 入口ステートからこの条件（less than）で降りる先が、そのアニメーターでの伏せ状態にあたる。
        /// </summary>
        internal const string LieDownConditionParameter = "Upright";

        /// <summary>追加したステートを既存のステートに重ねないための余白</summary>
        private const float ClonePositionGap = 300f;

        /// <summary>Exit遷移どうしを突き合わせるための番兵</summary>
        private static readonly object ExitKey = new object();

        /// <summary>
        /// 既存の遷移へ持ち込まない条件パラメータ。
        /// この条件を持つテンプレート側の遷移そのものも追加しない。
        /// </summary>
        private static readonly HashSet<string> ExcludedConditionParameters =
            new HashSet<string> { "EnableJumpMotion" };

        private readonly AnimatorController _template;
        private readonly AnimatorController _destination;
        private readonly string _templateAssetPath;
        private readonly IReadOnlyDictionary<string, string> _stateNameOverrides;

        private readonly AnimatorCloneMap _map = new AnimatorCloneMap();
        private readonly SupineAddReport _report = new SupineAddReport();
        private readonly AnimatorCloner _cloner;

        private AnimatorStateMachine _templateRoot;
        private AnimatorStateMachine _destinationRoot;
        private Dictionary<string, AnimatorState> _destinationStates;
        private Vector3 _clonePositionOffset;

        /// <param name="template">ごろ寝システムのテンプレート。読むだけで書き換えない</param>
        /// <param name="destination">追加先。生成済みのコピーを渡すこと</param>
        /// <param name="stateNameOverrides">
        /// テンプレート側のステート名 → 追加先で対応させるステート名。
        /// 既成のアニメーターはステート名を変えていることが多いので、名前一致で見つからないものを
        /// ユーザーの指定で埋められるようにする。
        /// </param>
        public SupineLocomotionAdder(
            AnimatorController template,
            AnimatorController destination,
            IReadOnlyDictionary<string, string> stateNameOverrides)
        {
            _template           = template;
            _destination        = destination;
            _templateAssetPath  = AssetDatabase.GetAssetPath(template);
            _stateNameOverrides = stateNameOverrides;
            _cloner             = new AnimatorCloner(destination, _templateAssetPath, _map, _report.Warnings);
        }

        /// <summary>
        /// すでにごろ寝システムが組み込まれたコントローラかどうか。
        ///
        /// ステート名は改名されうるが、補助レイヤーの構成はごろ寝システム固有なので、
        /// テンプレートのLocomotion以外のレイヤーが名前ごと揃っていれば組込済みと判定できる。
        /// </summary>
        internal static bool IsSupineCombined(AnimatorController template, AnimatorController target)
        {
            if (template == null || target == null || template.layers.Length <= 1) return false;

            HashSet<string> targetLayerNames = new HashSet<string>();
            foreach (AnimatorControllerLayer layer in target.layers)
            {
                targetLayerNames.Add(layer.name);
            }

            AnimatorControllerLayer[] templateLayers = template.layers;
            for (int i = 1; i < templateLayers.Length; i++)
            {
                if (!targetLayerNames.Contains(templateLayers[i].name)) return false;
            }
            return true;
        }

        /// <summary>
        /// 入口ステートにするステートを推測する。
        ///
        /// 1. 名前が一致するもの
        /// 2. デフォルトステート（＝立ち状態）から Upright less than で降りた先（＝しゃがみ状態）
        /// 3. しゃがみにあたるものが無ければデフォルトステートそのもの
        /// </summary>
        /// <returns>推測できなければ null</returns>
        internal static string InferEntryStateName(AnimatorController target)
        {
            if (target == null || target.layers.Length == 0 || target.layers[0].stateMachine == null) return null;

            AnimatorStateMachine root = target.layers[0].stateMachine;

            if (AnimatorStateUtility.BuildStateIndex(root).ContainsKey(EntryStateName)) return EntryStateName;

            AnimatorState defaultState = root.defaultState;
            if (defaultState == null) return null;

            List<AnimatorState> destinations = AnimatorStateUtility.CollectDestinationsByCondition(
                defaultState, LieDownConditionParameter, AnimatorConditionMode.Less);

            return destinations.Count > 0 ? destinations[0].name : defaultState.name;
        }

        /// <summary>
        /// 入口ステートから「伏せへ降りる」遷移の遷移先を、遷移の並び順のまま集める。
        /// 先頭がそのアニメーターでの伏せ状態、2つ目以降はごろ寝システムと競合する遷移先。
        /// </summary>
        internal static List<AnimatorState> CollectLieDownDestinations(
            AnimatorController target, string entryStateName)
        {
            if (target == null || string.IsNullOrEmpty(entryStateName) ||
                target.layers.Length == 0 || target.layers[0].stateMachine == null)
            {
                return new List<AnimatorState>();
            }

            Dictionary<string, AnimatorState> states =
                AnimatorStateUtility.BuildStateIndex(target.layers[0].stateMachine);

            if (!states.TryGetValue(entryStateName, out AnimatorState entry))
            {
                return new List<AnimatorState>();
            }

            return AnimatorStateUtility.CollectDestinationsByCondition(
                entry, LieDownConditionParameter, AnimatorConditionMode.Less);
        }

        /// <summary>
        /// 入口ステートから降りる先のうち、組み込んだ後も残るものの名前。
        /// 組込済みなら前回のごろ寝ステートは掃除で消えるので、ここから除く。
        /// </summary>
        internal static List<string> CollectLieDownDestinationNames(
            AnimatorController template, AnimatorController target, string entryStateName)
        {
            bool alreadyCombined = IsSupineCombined(template, target);

            List<string> names = new List<string>();
            foreach (AnimatorState destination in CollectLieDownDestinations(target, entryStateName))
            {
                if (alreadyCombined && IsRemovedOnReadd(template, destination.name)) continue;

                names.Add(destination.name);
            }
            return names;
        }

        /// <summary>
        /// 伏せ状態として指定したステート以外に降りる遷移があるか。
        /// あるとごろ寝システムの遷移より先に成立してしまい、ポーズへ入れなくなる。
        /// </summary>
        internal static bool HasConflictingLieDownDestination(
            IEnumerable<string> lieDownDestinationNames, string proneStateName)
        {
            foreach (string destinationName in lieDownDestinationNames)
            {
                if (destinationName != proneStateName) return true;
            }
            return false;
        }

        /// <summary>
        /// 組み込み直すときに掃除されるステートか。
        /// テンプレートのレイヤー0にあり、かつ既定Locomotion由来でないもの＝ごろ寝が足した分。
        /// </summary>
        private static bool IsRemovedOnReadd(AnimatorController template, string stateName)
        {
            if (DefaultLocomotionTable.IsDefaultStateName(stateName)) return false;

            foreach (ChildAnimatorState child in template.layers[0].stateMachine.states)
            {
                if (child.state != null && child.state.name == stateName) return true;
            }
            return false;
        }

        /// <summary>
        /// オプションの指定から、テンプレート側の名前と追加先の名前の対応表を作る。
        /// 空文字は「対応するステートを持たせない」という指定なので、そのまま入れる。
        /// </summary>
        internal static Dictionary<string, string> BuildStateNameOverrides(SupineCombineOptions options)
        {
            Dictionary<string, string> overrides = new Dictionary<string, string>();

            if (options.entryStateName != null)
            {
                overrides[EntryStateName] = options.entryStateName;
            }
            if (options.proneStateName != null)
            {
                overrides[ProneStateName] = options.proneStateName;
            }

            return overrides;
        }

        /// <summary>
        /// テンプレート側のステート名に対して、追加先で使うステート名を決める。
        ///
        /// 指定が無ければテンプレートと同じ名前で探し、空文字なら「対応するステートを持たせない」。
        /// 検証と生成でこの解釈がずれると、警告と実際の結果が食い違うので必ずここを通す。
        /// </summary>
        /// <returns>対応先を持たせない場合は false</returns>
        internal static bool TryResolveStateName(
            IReadOnlyDictionary<string, string> stateNameOverrides,
            string templateStateName,
            out string destinationStateName)
        {
            if (stateNameOverrides == null ||
                !stateNameOverrides.TryGetValue(templateStateName, out destinationStateName))
            {
                destinationStateName = templateStateName;
                return true;
            }

            if (destinationStateName.Length == 0)
            {
                destinationStateName = null;
                return false;
            }
            return true;
        }

        public SupineAddReport Add()
        {
            if (!Prepare()) return _report;

            // クローンしたステートを揃える先を、ステートを足す前に見ておく
            bool? destinationWriteDefaults = AnalyzeWriteDefaults(_destinationRoot);

            // 遷移を張る前にパラメータを揃えておく
            AnimatorParameterUtility.MergeParameters(_destination, _template, _report.Warnings);

            int clonedStatesBefore = _map.ClonedStates.Count;
            MapAndCloneLayerZero();
            int clonedStatesAfter = _map.ClonedStates.Count;

            CloneAuxiliaryLayers();

            _cloner.CloneTransitions(LeadsToUntouchedNode);
            RemoveConflictingLieDownTransitions();
            MergeAnchorTransitions();

            ApplyWriteDefaults(destinationWriteDefaults, clonedStatesBefore, clonedStatesAfter);
            CollectRenameWarnings();

            _report.RenamedStates = _map.RenamedStates;
            _report.Succeeded     = true;

            EditorUtility.SetDirty(_destination);
            AssetDatabase.SaveAssets();

            return _report;
        }

        private bool Prepare()
        {
            if (_template == null || _template.layers.Length == 0 || _template.layers[0].stateMachine == null)
            {
                _report.Warnings.Add("The Supine template controller has no locomotion layer.");
                return false;
            }

            if (_destination == null || _destination.layers.Length == 0 ||
                _destination.layers[0].stateMachine == null)
            {
                _report.Warnings.Add("The target animator has no layer to add the Supine states to.");
                return false;
            }

            _templateRoot    = _template.layers[0].stateMachine;
            _destinationRoot = _destination.layers[0].stateMachine;

            // 組込済みなら、先に前回の分を落としてから作り直す
            RemoveExistingSupineParts();

            _destinationStates   = AnimatorStateUtility.BuildStateIndex(_destinationRoot);
            _clonePositionOffset = CalculateClonePositionOffset();

            return true;
        }

        /// <summary>
        /// 追加したステートを既存のステートの右下に固めて置くためのずらし量。
        /// 既存のグラフと重なると、どれが足された分なのか見分けがつかなくなる。
        /// </summary>
        private Vector3 CalculateClonePositionOffset()
        {
            Vector3 destinationBottomRight = CalculateBottomRight(_destinationRoot);
            Vector3 templateTopLeft = CalculateTopLeft(_templateRoot);

            return new Vector3(
                destinationBottomRight.x + ClonePositionGap - templateTopLeft.x,
                destinationBottomRight.y + ClonePositionGap - templateTopLeft.y,
                0f);
        }

        private static Vector3 CalculateBottomRight(AnimatorStateMachine stateMachine)
        {
            Vector3 bottomRight = Vector3.Max(
                stateMachine.anyStatePosition,
                Vector3.Max(stateMachine.entryPosition, stateMachine.exitPosition));

            foreach (ChildAnimatorState child in stateMachine.states)
            {
                bottomRight = Vector3.Max(bottomRight, child.position);
            }
            foreach (ChildAnimatorStateMachine child in stateMachine.stateMachines)
            {
                bottomRight = Vector3.Max(bottomRight, child.position);
            }
            return bottomRight;
        }

        private static Vector3 CalculateTopLeft(AnimatorStateMachine stateMachine)
        {
            Vector3 topLeft = Vector3.Min(
                stateMachine.anyStatePosition,
                Vector3.Min(stateMachine.entryPosition, stateMachine.exitPosition));

            foreach (ChildAnimatorState child in stateMachine.states)
            {
                topLeft = Vector3.Min(topLeft, child.position);
            }
            foreach (ChildAnimatorStateMachine child in stateMachine.stateMachines)
            {
                topLeft = Vector3.Min(topLeft, child.position);
            }
            return topLeft;
        }

        // ------------------------------------------------------------
        // 組込済みの分の掃除
        // ------------------------------------------------------------

        /// <summary>
        /// すでに組み込まれているごろ寝システム由来の部分を落とす。
        ///
        /// 二重に足すと同名ステートが並んで壊れるので、組み直す前に前回の分を取り除く。
        /// 落とすのはごろ寝が足した分だけで、Standing / Crouching / Prone にあたる
        /// 既定Locomotion由来のステートはロコモーションの土台なので残す。
        /// パラメータは既存のレイヤーが参照している可能性があるため触らない
        /// （どのみち同じ名前・型で足し直される）。
        ///
        /// 落とせるのは自分のテンプレートに在るステート名だけなので、
        /// EX版で組み込んだものを通常版で組み直すような場合、EX固有のステートは消し残る。
        /// 遷移は切れて到達不能になるだけなので、警告で伝えるに留めている
        /// （テンプレートに無い名前まで落とすと、ユーザーが足したステートまで巻き込む）。
        ///
        /// 触るのは生成済みのコピーだけで、元のアニメーターのアセットには手を入れない。
        /// </summary>
        private void RemoveExistingSupineParts()
        {
            if (!IsSupineCombined(_template, _destination)) return;

            RemoveSupineLayers();
            RemoveSupineStates();

            _report.Warnings.Add(
                "The target animator already contained Supine. " +
                "Removed the previous Supine states and layers before adding it again. " +
                "States this variant does not know about are left behind unused.");
        }

        private void RemoveSupineLayers()
        {
            HashSet<string> supineLayerNames = new HashSet<string>();
            AnimatorControllerLayer[] templateLayers = _template.layers;
            for (int i = 1; i < templateLayers.Length; i++)
            {
                supineLayerNames.Add(templateLayers[i].name);
            }

            // インデックスがずれるので後ろから消す
            AnimatorControllerLayer[] layers = _destination.layers;
            for (int i = layers.Length - 1; i >= 1; i--)
            {
                if (!supineLayerNames.Contains(layers[i].name)) continue;
                _destination.RemoveLayer(i);
            }
        }

        private void RemoveSupineStates()
        {
            HashSet<string> supineStateNames = new HashSet<string>();
            foreach (ChildAnimatorState child in _templateRoot.states)
            {
                if (child.state == null) continue;
                if (DefaultLocomotionTable.IsDefaultStateName(child.state.name)) continue;

                supineStateNames.Add(child.state.name);
            }

            // RemoveStateが配列を組み替えるので、消す対象を先に控えてから消す
            List<AnimatorState> removed = new List<AnimatorState>();
            foreach (ChildAnimatorState child in _destinationRoot.states)
            {
                if (child.state == null) continue;
                if (!supineStateNames.Contains(child.state.name)) continue;

                removed.Add(child.state);
            }

            foreach (AnimatorState state in removed)
            {
                _destinationRoot.RemoveState(state);
            }
        }

        // ------------------------------------------------------------
        // ノードの対応付けとクローン
        // ------------------------------------------------------------

        private void MapAndCloneLayerZero()
        {
            _map.RegisterAnchorStateMachine(_templateRoot, _destinationRoot);
            MapStateMachine(_templateRoot, _destinationRoot);
        }

        /// <summary>
        /// テンプレート側のノードを、追加先の対応ノードへ割り当てる。
        ///
        /// 既定Locomotion由来のステート（Standing / Crouching / Prone）は追加せず、
        /// 追加先の既存ステートへ紐づける。motionやbehaviourはそのまま使い、
        /// 遷移だけを後段のMergeAnchorTransitionsで繋ぎ直す。
        /// 既定Locomotion由来のサブステートマシン（JumpAndFall）は丸ごと対象外にする。
        /// </summary>
        private void MapStateMachine(AnimatorStateMachine templateStateMachine, AnimatorStateMachine destinationStateMachine)
        {
            foreach (ChildAnimatorState child in templateStateMachine.states)
            {
                AnimatorState templateState = child.state;
                if (templateState == null) continue;

                if (DefaultLocomotionTable.IsDefaultStateName(templateState.name))
                {
                    if (TryResolveAnchor(templateState.name, out AnimatorState anchor))
                    {
                        _map.RegisterAnchorState(templateState, anchor);
                    }
                    continue;
                }

                AnimatorState cloned = _cloner.CloneState(
                    templateState, destinationStateMachine, child.position + _clonePositionOffset);
                _map.RegisterClonedState(templateState, cloned);
            }

            foreach (ChildAnimatorStateMachine child in templateStateMachine.stateMachines)
            {
                AnimatorStateMachine templateChild = child.stateMachine;
                if (templateChild == null) continue;

                if (DefaultLocomotionTable.IsDefaultStateMachineName(templateChild.name)) continue;

                _cloner.CloneStateMachine(
                    templateChild, destinationStateMachine, child.position + _clonePositionOffset);
            }
        }

        /// <summary>
        /// テンプレート側のステートに対応する、追加先の既存ステートを引く。
        ///
        /// 指定があればそれを優先し、指定が無ければ同じ名前で探す。
        /// 空文字が指定されている場合は「そのステートは持たせない」という意思表示なので、
        /// 対応先なしとして扱う。行き先を失った遷移はそのまま張られずに終わる。
        /// </summary>
        private bool TryResolveAnchor(string templateStateName, out AnimatorState anchor)
        {
            if (!TryResolveStateName(_stateNameOverrides, templateStateName, out string destinationName))
            {
                anchor = null;
                return false;
            }

            return _destinationStates.TryGetValue(destinationName, out anchor);
        }

        /// <summary>
        /// テンプレートのレイヤー1以降（ごろ寝の補助レイヤー）を末尾に追加する。
        /// </summary>
        private void CloneAuxiliaryLayers()
        {
            AnimatorControllerLayer[] templateLayers = _template.layers;
            for (int i = 1; i < templateLayers.Length; i++)
            {
                _cloner.CloneLayer(templateLayers[i]);
            }
        }

        /// <summary>
        /// 入口ステートから伏せへ降りる遷移のうち、伏せ状態として使うステート以外への遷移を削除する。
        ///
        /// ごろ寝システムはこの後、入口ステートに Upright と VRCSupine を条件とする遷移を足す。
        /// 伏せ状態以外へ降りる遷移が残っていると、それが先に成立してポーズへ入れなくなる。
        /// マージで遷移を足す前に片付けておく。
        /// </summary>
        private void RemoveConflictingLieDownTransitions()
        {
            AnimatorState entry = null;
            AnimatorState prone = null;

            foreach (KeyValuePair<AnimatorState, AnimatorState> pair in _map.AnchorStates)
            {
                if (pair.Key.name == EntryStateName) entry = pair.Value;
                if (pair.Key.name == ProneStateName) prone = pair.Value;
            }

            if (entry == null) return;

            List<AnimatorStateTransition> kept = new List<AnimatorStateTransition>();
            List<AnimatorStateTransition> removed = new List<AnimatorStateTransition>();

            foreach (AnimatorStateTransition transition in entry.transitions)
            {
                if (transition.destinationState != null && transition.destinationState != prone &&
                    HasLieDownCondition(transition))
                {
                    removed.Add(transition);
                    continue;
                }
                kept.Add(transition);
            }

            if (removed.Count == 0) return;

            entry.transitions = kept.ToArray();

            foreach (AnimatorStateTransition transition in removed)
            {
                _report.Warnings.Add(
                    "Removed the transition from '" + entry.name + "' to '" +
                    transition.destinationState.name + "' because it would take priority over the Supine poses.");

                // 配列から外した遷移はサブアセットとして残ってしまうので消しておく
                UnityEngine.Object.DestroyImmediate(transition, true);
            }
        }

        private static bool HasLieDownCondition(AnimatorStateTransition transition)
        {
            foreach (AnimatorCondition condition in transition.conditions)
            {
                if (condition.parameter == LieDownConditionParameter &&
                    condition.mode == AnimatorConditionMode.Less)
                {
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------
        // 流用したノードの遷移マージ
        // ------------------------------------------------------------

        private void MergeAnchorTransitions()
        {
            foreach (KeyValuePair<AnimatorState, AnimatorState> pair in _map.AnchorStates)
            {
                List<AnimatorStateTransition> unmatched =
                    PairAndMergeConditions(pair.Key.transitions, pair.Value.transitions);

                foreach (AnimatorStateTransition transition in unmatched)
                {
                    _cloner.CloneStateTransition(transition, pair.Value);
                }
            }

            foreach (KeyValuePair<AnimatorStateMachine, AnimatorStateMachine> pair in _map.AnchorStateMachines)
            {
                List<AnimatorStateTransition> unmatched =
                    PairAndMergeConditions(pair.Key.anyStateTransitions, pair.Value.anyStateTransitions);

                foreach (AnimatorStateTransition transition in unmatched)
                {
                    _cloner.CloneAnyStateTransition(transition, pair.Value);
                }
            }
        }

        /// <summary>
        /// テンプレート側と追加先側の遷移を遷移先で突き合わせ、対になったものへ条件をマージする。
        /// 対象外にした条件を持つテンプレート側の遷移は、突き合わせにも追加にも回さない。
        /// </summary>
        /// <returns>対にならなかったテンプレート側の遷移（＝丸ごと足すべきもの）</returns>
        private List<AnimatorStateTransition> PairAndMergeConditions(
            AnimatorStateTransition[] templateTransitions, AnimatorStateTransition[] destinationTransitions)
        {
            Dictionary<object, Queue<AnimatorStateTransition>> byDestination =
                new Dictionary<object, Queue<AnimatorStateTransition>>();

            foreach (AnimatorStateTransition transition in destinationTransitions)
            {
                object key = DestinationKeyOf(transition);
                if (key == null) continue;

                if (!byDestination.TryGetValue(key, out Queue<AnimatorStateTransition> queue))
                {
                    queue = new Queue<AnimatorStateTransition>();
                    byDestination.Add(key, queue);
                }
                queue.Enqueue(transition);
            }

            List<AnimatorStateTransition> unmatched = new List<AnimatorStateTransition>();

            foreach (AnimatorStateTransition transition in templateTransitions)
            {
                // ジャンプ・落下まわりの遷移は、既存の挙動をそのまま残すため一切持ち込まない。
                // 遷移先の JumpAndFall もクローンしていないので、ここで弾いておかないと
                // 「遷移先を解決できない」という無意味な警告になる
                if (HasExcludedCondition(transition)) continue;

                object key = ResolvedDestinationKeyOf(transition);
                if (key != null &&
                    byDestination.TryGetValue(key, out Queue<AnimatorStateTransition> queue) && queue.Count > 0)
                {
                    MergeConditions(transition, queue.Dequeue());
                    continue;
                }

                // 既定Locomotion由来のノード行きで対応が取れないものは、そもそも触らない部分への遷移。
                // 追加先には相当する遷移が元からあるはずなので、警告を出さずに見送る
                if (key == null && LeadsToUntouchedNode(transition)) continue;

                unmatched.Add(transition);
            }

            return unmatched;
        }

        /// <summary>遷移先が既定Locomotion由来のノード（＝今回追加していない部分）かどうか</summary>
        private static bool LeadsToUntouchedNode(AnimatorStateTransition transition)
        {
            if (transition.destinationState != null)
            {
                return DefaultLocomotionTable.IsDefaultStateName(transition.destinationState.name);
            }
            if (transition.destinationStateMachine != null)
            {
                return DefaultLocomotionTable.IsDefaultStateMachineName(transition.destinationStateMachine.name);
            }
            return false;
        }

        private static bool HasExcludedCondition(AnimatorStateTransition transition)
        {
            foreach (AnimatorCondition condition in transition.conditions)
            {
                if (ExcludedConditionParameters.Contains(condition.parameter)) return true;
            }
            return false;
        }

        /// <summary>
        /// テンプレート側の条件のうち、追加先の遷移にまだ出てこないパラメータのものだけを足す。
        /// 閾値をカスタムしている既存の条件（Upright など）を壊さずに、
        /// VRCSupine や VRCLockPose といったごろ寝側の条件だけを乗せられる。
        /// </summary>
        private static void MergeConditions(
            AnimatorStateTransition template, AnimatorStateTransition destination)
        {
            AnimatorCondition[] destinationConditions = destination.conditions;

            HashSet<string> parameters = new HashSet<string>();
            foreach (AnimatorCondition condition in destinationConditions)
            {
                parameters.Add(condition.parameter);
            }

            List<AnimatorCondition> merged = new List<AnimatorCondition>(destinationConditions);
            bool changed = false;

            foreach (AnimatorCondition condition in template.conditions)
            {
                if (parameters.Contains(condition.parameter)) continue;
                if (ExcludedConditionParameters.Contains(condition.parameter)) continue;

                merged.Add(condition);
                parameters.Add(condition.parameter);
                changed = true;
            }

            if (changed)
            {
                destination.conditions = merged.ToArray();
            }
        }

        /// <summary>追加先の遷移の遷移先をキーにする</summary>
        private static object DestinationKeyOf(AnimatorStateTransition transition)
        {
            if (transition.isExit) return ExitKey;
            if (transition.destinationStateMachine != null) return transition.destinationStateMachine;
            if (transition.destinationState != null) return transition.destinationState;
            return null;
        }

        /// <summary>テンプレート側の遷移先を、追加先のノードへ読み替えてキーにする</summary>
        private object ResolvedDestinationKeyOf(AnimatorStateTransition transition)
        {
            if (transition.isExit) return ExitKey;

            if (transition.destinationStateMachine != null)
            {
                return _map.TryResolve(transition.destinationStateMachine, out AnimatorStateMachine stateMachine)
                    ? stateMachine
                    : null;
            }

            if (transition.destinationState != null)
            {
                return _map.TryResolve(transition.destinationState, out AnimatorState state) ? state : null;
            }

            return null;
        }

        // ------------------------------------------------------------
        // 後始末
        // ------------------------------------------------------------

        /// <summary>
        /// レイヤー0のWrite Defaultsが揃っているかを見る。
        /// </summary>
        /// <returns>全て同じならその値。混在していればnull</returns>
        private static bool? AnalyzeWriteDefaults(AnimatorStateMachine root)
        {
            List<AnimatorState> states = AnimatorStateUtility.CollectStates(root);
            if (states.Count == 0) return null;

            bool first = states[0].writeDefaultValues;
            foreach (AnimatorState state in states)
            {
                if (state.writeDefaultValues != first) return null;
            }
            return first;
        }

        /// <summary>
        /// 追加したレイヤー0のステートを、追加先のWrite Defaultsに合わせる。
        /// 補助レイヤーはmotionを持たないため触らない。
        /// </summary>
        private void ApplyWriteDefaults(bool? destinationWriteDefaults, int fromIndex, int toIndex)
        {
            if (!destinationWriteDefaults.HasValue)
            {
                _report.Warnings.Add(
                    "The target animator mixes Write Defaults on and off in its first layer. " +
                    "Added the Supine states with Write Defaults off.");
                return;
            }

            for (int i = fromIndex; i < toIndex; i++)
            {
                _map.ClonedStates[i].Value.writeDefaultValues = destinationWriteDefaults.Value;
            }
        }

        private void CollectRenameWarnings()
        {
            foreach (KeyValuePair<string, string> renamed in _map.RenamedStates)
            {
                _report.Warnings.Add(
                    "Renamed the added state '" + renamed.Key + "' to '" + renamed.Value +
                    "' because the target animator already has a state with that name.");
            }
        }
    }
}
