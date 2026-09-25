using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using Supine.Utilities;

namespace Supine.PosePack
{
    /// <summary>
    /// パックから拾った定義1つ分と、割り当てた番号。
    /// </summary>
    internal abstract class ResolvedEntry
    {
        public SupinePosePack Pack;

        /// <summary>割り当てた番号。まだ割り当てていなければ -1</summary>
        public int Slot = -1;

        public abstract SupinePoseEntryBase BaseEntry { get; }

        /// <summary>パックを跨いで一意な識別子（"packId/id"）</summary>
        public string Key => SupinePosePackRegistry.MakeKey(Pack, BaseEntry);
    }

    internal abstract class ResolvedEntry<TEntry> : ResolvedEntry where TEntry : SupinePoseEntryBase
    {
        public TEntry Entry;

        public override SupinePoseEntryBase BaseEntry => Entry;
    }

    /// <summary>
    /// 採番まで済ませた寝ポーズ1つ分。
    /// </summary>
    internal sealed class ResolvedPose : ResolvedEntry<SupinePoseEntry>
    {
        /// <summary>割り当てられた VRCSupine の値</summary>
        public int Value => Slot;

        /// <summary>生成するステート名。既存と衝突した場合は連番が付く</summary>
        public string StateName;
    }

    /// <summary>
    /// 採番まで済ませたしゃがみポーズ1つ分。
    /// </summary>
    internal sealed class ResolvedCrouchPose : ResolvedEntry<SupineCrouchEntry>
    {
        /// <summary>割り当てられた枠番号から引いた CrouchPose の値</summary>
        public float Value => SupineCrouchValues.ToValue(Slot);
    }

    /// <summary>
    /// プロジェクト内の SupinePosePack を集めて、番号を割り当てる。
    ///
    /// ごろ寝システム側から個々のパックを参照することはできない。
    /// パッケージ（Packages/）からアセット（Assets/）は参照できないため、
    /// 依存の向きは「パックが置かれる → こちらが拾う」の一方向に限られる。
    /// この一方向性が、有償パックを別配布にしても本体を無改修に保てる理由になっている。
    ///
    /// 寝ポーズとしゃがみポーズは番号の意味（VRCSupine の値か、ツリーの枠か）と
    /// 範囲が違うだけで、検証と採番の手順は同じ。違いは <see cref="SlotRule"/> に閉じ込める。
    /// </summary>
    internal static class SupinePosePackRegistry
    {
        /// <summary>
        /// 寝ポーズの番号。自動採番は手書きの番号（0〜8, 51〜56）とぶつからない位置から始める。
        /// VRCSupine は int の同期パラメータなので 255 まで。
        /// </summary>
        private static readonly SlotRule PoseRule = new SlotRule
        {
            Noun = "pose",
            SlotName = "VRCSupine value",
            AutoMin = 57,
            Max = 255,
        };

        /// <summary>
        /// しゃがみポーズの枠番号。0番は Default（そのアバターが元々そうだった姿）の指定席なので、
        /// 1から探す。上限は同期 float の値域に収まる枠の数で決まる。
        /// </summary>
        private static readonly SlotRule CrouchRule = new SlotRule
        {
            Noun = "crouch pose",
            SlotName = "crouch pose slot",
            AutoMin = 1,
            Max = SupineCrouchValues.MaxIndex,
            ExhaustedNote =
                " A synced float only reaches " + (SupineCrouchValues.MaxIndex + 1) +
                " distinct values at this spacing.",
        };

        /// <summary>
        /// パックを集めて、組込をやり直しても同じ順になるよう並べる。
        ///
        /// 並びが揺れると採番も揺れる。メニュー項目は isSaved のため、
        /// 採番がずれると前回保存された選択が別のポーズを指してしまう。
        ///
        /// AssetDatabase を舐めるので、組込1回につき1度だけ呼んで結果を使い回す。
        /// </summary>
        public static List<SupinePosePack> CollectPacks(List<string> warnings)
        {
            List<SupinePosePack> packs = new List<SupinePosePack>();
            string currentVersion = SupinePackageVersion.Current;

            foreach (string guid in AssetDatabase.FindAssets("t:SupinePosePack"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                SupinePosePack pack = AssetDatabase.LoadAssetAtPath<SupinePosePack>(path);
                if (pack == null) continue;

                // 仕組みが変わったあとで古い本体に新しいパックを入れると、黙って壊れる。
                // 組込の時点で気付けるよう、足りなければ丸ごと外して知らせる
                if (!SupinePackageVersion.IsAtLeast(currentVersion, pack.minimumSupineVersion))
                {
                    warnings.Add(
                        "Pack '" + pack.ResolvePackId() + "' requires Supine " + pack.minimumSupineVersion +
                        " or later, but " + currentVersion + " is installed. The pack was skipped.");
                    continue;
                }

                packs.Add(pack);
            }

            packs.Sort(ComparePacks);
            return packs;
        }

        /// <summary>
        /// 寝ポーズを集め、VRCSupine の値を割り当てて返す。
        /// </summary>
        /// <param name="controller">コピー済みのコントローラ。使用済みの番号をここから読む</param>
        /// <param name="warnings">利用者に見せる警告の追記先</param>
        public static List<ResolvedPose> Resolve(
            IReadOnlyList<SupinePosePack> packs, AnimatorController controller, List<string> warnings)
        {
            List<ResolvedPose> poses = ListEntries<SupinePoseEntry, ResolvedPose>(
                packs, pack => pack.poses, PoseRule, ValidateUpright, warnings);
            if (poses.Count == 0) return poses;

            return Allocate(poses, CollectUsedSupineValues(controller), PoseRule, warnings);
        }

        /// <summary>
        /// しゃがみポーズを集め、枠番号を割り当てて返す。
        ///
        /// 寝ポーズと違い、こちらはステートを増やさない。テンプレートのツリーに
        /// 枝を足すだけなので、必要なのは枝の番号だけになる。
        /// </summary>
        /// <param name="template">テンプレートの根ツリー。埋まっている番号をここから読む</param>
        public static List<ResolvedCrouchPose> ResolveCrouch(
            IReadOnlyList<SupinePosePack> packs, BlendTree template, List<string> warnings)
        {
            List<ResolvedCrouchPose> poses = ListEntries<SupineCrouchEntry, ResolvedCrouchPose>(
                packs, pack => pack.crouchPoses, CrouchRule, null, warnings);
            if (poses.Count == 0) return poses;

            return Allocate(poses, CollectUsedCrouchIndices(template), CrouchRule, warnings);
        }

        /// <summary>
        /// しゃがみポーズを並び順だけ確定させて返す。採番はしない。
        ///
        /// 組込ウィンドウが「既定にするポーズ」の選択肢を並べるためのもの。
        /// 枠番号はテンプレートのツリーを見ないと決まらないが、
        /// 選択肢を出すだけならパックの中身が分かれば足りる。
        /// </summary>
        public static List<ResolvedCrouchPose> ListCrouchEntries()
        {
            List<string> ignored = new List<string>();
            return ListEntries<SupineCrouchEntry, ResolvedCrouchPose>(
                CollectPacks(ignored), pack => pack.crouchPoses, CrouchRule, null, ignored);
        }

        /// <summary>
        /// パックを跨いで一意な識別子。既定のしゃがみの指定を覚えておくのにも使う。
        /// </summary>
        public static string MakeKey(SupinePosePack pack, SupinePoseEntryBase entry)
        {
            return pack.ResolvePackId() + "/" + entry.id;
        }

        /// <summary>
        /// 使える定義をパックの順・宣言順に並べる。使えないものは警告して外す。
        /// </summary>
        private static List<TResolved> ListEntries<TEntry, TResolved>(
            IReadOnlyList<SupinePosePack> packs,
            Func<SupinePosePack, TEntry[]> select,
            SlotRule rule,
            Func<SupinePosePack, TEntry, List<string>, bool> extraValidation,
            List<string> warnings)
            where TEntry : SupinePoseEntryBase
            where TResolved : ResolvedEntry<TEntry>, new()
        {
            List<TResolved> resolved = new List<TResolved>();
            HashSet<string> usedKeys = new HashSet<string>();

            foreach (SupinePosePack pack in packs)
            {
                TEntry[] entries = select(pack);
                if (entries == null) continue;

                foreach (TEntry entry in entries)
                {
                    if (!Validate(pack, entry, rule, usedKeys, warnings)) continue;
                    if (extraValidation != null && !extraValidation(pack, entry, warnings)) continue;

                    resolved.Add(new TResolved { Pack = pack, Entry = entry });
                }
            }

            return resolved;
        }

        private static bool Validate(
            SupinePosePack pack, SupinePoseEntryBase entry, SlotRule rule,
            HashSet<string> usedKeys, List<string> warnings)
        {
            if (entry == null || string.IsNullOrEmpty(entry.id))
            {
                warnings.Add("Pack '" + pack.ResolvePackId() + "' has a " + rule.Noun + " with no id. It was skipped.");
                return false;
            }

            if (entry.clip == null)
            {
                warnings.Add(rule.Describe(pack, entry) + " has no animation clip. It was skipped.");
                return false;
            }

            // 同じidが二度出てくると、生成物のどちらがどれだか追えなくなる
            if (!usedKeys.Add(MakeKey(pack, entry)))
            {
                warnings.Add(
                    "Pack '" + pack.ResolvePackId() + "' declares the " + rule.Noun + " id '" + entry.id +
                    "' more than once. The duplicate was skipped.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 0や1のまま出荷されると「しゃがんだ瞬間に解除される」「二度と解除できない」ポーズになる。
        /// </summary>
        private static bool ValidateUpright(SupinePosePack pack, SupinePoseEntry entry, List<string> warnings)
        {
            if (entry.uprightThreshold > 0f && entry.uprightThreshold < 1f) return true;

            warnings.Add(
                PoseRule.Describe(pack, entry) + " has an out-of-range upright threshold (" +
                entry.uprightThreshold.ToString("0.###") + "). It was skipped.");
            return false;
        }

        /// <summary>
        /// 番号を割り当てる。希望番号が空いていればそれを使い、
        /// 埋まっていれば後回しにして、残りを AutoMin から順に空きで埋める。
        /// 番号が取れなかったものは外して返す。
        /// </summary>
        private static List<TResolved> Allocate<TResolved>(
            List<TResolved> entries, HashSet<int> used, SlotRule rule, List<string> warnings)
            where TResolved : ResolvedEntry
        {
            List<TResolved> needsSlot = new List<TResolved>();

            foreach (TResolved entry in entries)
            {
                int preferred = entry.BaseEntry.preferredValue;

                if (preferred <= 0)
                {
                    needsSlot.Add(entry);
                }
                else if (preferred > rule.Max)
                {
                    warnings.Add(
                        rule.Describe(entry.Pack, entry.BaseEntry) + " wants " + rule.SlotName + " " + preferred +
                        ", but the highest usable one is " + rule.Max + ". A free one was assigned instead.");
                    needsSlot.Add(entry);
                }
                else if (!used.Add(preferred))
                {
                    warnings.Add(
                        rule.Describe(entry.Pack, entry.BaseEntry) + " wants " + rule.SlotName + " " + preferred +
                        ", but it is already taken. A free one was assigned instead.");
                    needsSlot.Add(entry);
                }
                else
                {
                    entry.Slot = preferred;
                }
            }

            int next = rule.AutoMin;
            foreach (TResolved entry in needsSlot)
            {
                while (next <= rule.Max && used.Contains(next)) next++;

                if (next > rule.Max)
                {
                    warnings.Add(
                        "Ran out of " + rule.SlotName + "s. " + rule.Describe(entry.Pack, entry.BaseEntry) +
                        " was skipped." + rule.ExhaustedNote);
                    continue;
                }

                entry.Slot = next;
                used.Add(next);
            }

            entries.RemoveAll(entry => entry.Slot < 0);
            return entries;
        }

        /// <summary>
        /// テンプレートのツリーが既に使っている枠番号。
        ///
        /// 個数ではなく閾値から引く。枝の並びと番号は本来別物で、
        /// 個数で数えると並び替えたときに黙ってずれる。
        /// </summary>
        private static HashSet<int> CollectUsedCrouchIndices(BlendTree template)
        {
            HashSet<int> indices = new HashSet<int>();
            if (template == null) return indices;

            foreach (ChildMotion child in template.children)
            {
                int index = SupineCrouchValues.ToIndex(child.threshold);
                if (index >= 0) indices.Add(index);
            }
            return indices;
        }

        /// <summary>
        /// コントローラが既に使っている VRCSupine の値を集める。
        ///
        /// 使用済みの番号を定数表で持つと、テンプレート側にポーズが増えたときに衝突する。
        /// コントローラ自身を唯一の情報源にして、テンプレートの増減に黙って追従させる。
        /// </summary>
        private static HashSet<int> CollectUsedSupineValues(AnimatorController controller)
        {
            HashSet<int> values = new HashSet<int>();
            if (controller == null) return values;

            foreach (AnimatorControllerLayer layer in controller.layers)
            {
                if (layer.stateMachine == null) continue;

                foreach (AnimatorState state in AnimatorStateUtility.CollectStates(layer.stateMachine))
                {
                    foreach (AnimatorStateTransition transition in state.transitions)
                    {
                        CollectUsedSupineValues(transition, values);
                    }
                }

                foreach (AnimatorStateTransition transition in layer.stateMachine.anyStateTransitions)
                {
                    CollectUsedSupineValues(transition, values);
                }
            }

            return values;
        }

        /// <summary>
        /// 番号を押さえているのはポーズそのものなので、ポーズへ向かう遷移だけを数える。
        ///
        /// 条件に VRCSupine が出てくる遷移を全部数えると、振り分けの行まで拾ってしまう。
        /// 振り分けの行は番号の使用者ではないので、使用中と見なすと
        /// パックが希望した番号が理由もなく弾かれる。
        ///
        /// ポーズのステートはモーションを持ち、振り分け先（Prepare Animation /
        /// Prepare Tracking / Set Current Pose など）は持たない。そこで見分ける。
        /// </summary>
        private static void CollectUsedSupineValues(AnimatorStateTransition transition, HashSet<int> values)
        {
            if (transition.destinationState == null) return;
            if (transition.destinationState.motion == null) return;

            foreach (AnimatorCondition condition in transition.conditions)
            {
                if (condition.parameter != SupineNames.Parameters.Pose) continue;
                if (condition.mode != AnimatorConditionMode.Equals &&
                    condition.mode != AnimatorConditionMode.NotEqual) continue;

                values.Add(Mathf.RoundToInt(condition.threshold));
            }
        }

        private static int ComparePacks(SupinePosePack left, SupinePosePack right)
        {
            int order = left.sortOrder.CompareTo(right.sortOrder);
            if (order != 0) return order;

            order = string.CompareOrdinal(left.ResolvePackId(), right.ResolvePackId());
            if (order != 0) return order;

            return string.CompareOrdinal(
                AssetDatabase.GetAssetPath(left), AssetDatabase.GetAssetPath(right));
        }


        private sealed class SlotRule
        {
            /// <summary>警告に出す呼び名（"pose" / "crouch pose"）</summary>
            public string Noun;

            /// <summary>番号の呼び名（"VRCSupine value" / "crouch pose slot"）</summary>
            public string SlotName;

            /// <summary>自動採番を始める番号</summary>
            public int AutoMin;

            /// <summary>使える番号の上限。希望番号もこれを超えられない</summary>
            public int Max;

            /// <summary>番号が尽きたときの警告に添える補足</summary>
            public string ExhaustedNote = string.Empty;

            public string Describe(SupinePosePack pack, SupinePoseEntryBase entry)
            {
                return char.ToUpperInvariant(Noun[0]) + Noun.Substring(1) +
                       " '" + entry.id + "' in pack '" + pack.ResolvePackId() + "'";
            }
        }
    }
}
