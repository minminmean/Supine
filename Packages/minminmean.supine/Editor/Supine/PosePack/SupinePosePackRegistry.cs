using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using Supine.Utilities;

namespace Supine.PosePack
{
    /// <summary>
    /// 採番まで済ませたポーズ1つ分。
    /// </summary>
    internal sealed class ResolvedPose
    {
        public SupinePosePack Pack;
        public SupinePoseEntry Entry;

        /// <summary>割り当てられた VRCSupine の値</summary>
        public int Value;

        /// <summary>生成するステート名。既存と衝突した場合は連番が付く</summary>
        public string StateName;
    }

    /// <summary>
    /// 採番まで済ませたしゃがみポーズ1つ分。
    /// </summary>
    internal sealed class ResolvedCrouchPose
    {
        public SupinePosePack Pack;
        public SupineCrouchEntry Entry;

        /// <summary>割り当てられた枠番号。CrouchPose の値はここから引く</summary>
        public int Index = -1;

        public float Value { get { return SupineCrouchValues.ToValue(Index); } }
    }

    /// <summary>
    /// プロジェクト内の SupinePosePack を集めて、VRCSupine の値を割り当てる。
    ///
    /// ごろ寝システム側から個々のパックを参照することはできない。
    /// パッケージ（Packages/）からアセット（Assets/）は参照できないため、
    /// 依存の向きは「パックが置かれる → こちらが拾う」の一方向に限られる。
    /// この一方向性が、有償パックを別配布にしても本体を無改修に保てる理由になっている。
    /// </summary>
    internal static class SupinePosePackRegistry
    {
        /// <summary>自動採番に使う範囲。手書きの番号（0〜8, 51〜56）とぶつからない位置から始める</summary>
        private const int GeneratedValueMin = 57;
        private const int GeneratedValueMax = 255;

        /// <summary>
        /// プロジェクト内のパックを集め、番号を割り当てて返す。
        /// </summary>
        /// <param name="controller">コピー済みのコントローラ。使用済みの番号をここから読む</param>
        /// <param name="warnings">利用者に見せる警告の追記先</param>
        public static List<ResolvedPose> Resolve(AnimatorController controller, List<string> warnings)
        {
            List<SupinePosePack> packs = CollectPacks(warnings);
            List<ResolvedPose> resolved = new List<ResolvedPose>();
            if (packs.Count == 0) return resolved;

            HashSet<int> usedValues = CollectUsedSupineValues(controller);
            HashSet<string> usedIds = new HashSet<string>();
            List<ResolvedPose> needsValue = new List<ResolvedPose>();

            foreach (SupinePosePack pack in packs)
            {
                if (pack.poses == null) continue;

                foreach (SupinePoseEntry entry in pack.poses)
                {
                    if (!Validate(pack, entry, usedIds, warnings)) continue;

                    ResolvedPose pose = new ResolvedPose { Pack = pack, Entry = entry, Value = 0 };

                    // 希望番号が空いていればそれを使う。埋まっていれば自動採番へ回す
                    if (entry.preferredValue > 0 && !usedValues.Contains(entry.preferredValue))
                    {
                        pose.Value = entry.preferredValue;
                        usedValues.Add(entry.preferredValue);
                    }
                    else
                    {
                        if (entry.preferredValue > 0)
                        {
                            warnings.Add(
                                "Pose " + Describe(pack, entry) + " wants VRCSupine " + entry.preferredValue +
                                ", but that value is already taken. A free value was assigned instead.");
                        }
                        needsValue.Add(pose);
                    }

                    resolved.Add(pose);
                }
            }

            AssignFreeValues(needsValue, usedValues, warnings);

            // 番号が取れなかったものは落とす
            resolved.RemoveAll(pose => pose.Value <= 0);
            return resolved;
        }

        /// <summary>
        /// しゃがみポーズを集め、枠番号を割り当てて返す。
        ///
        /// 寝ポーズと違い、こちらはステートを増やさない。テンプレートのツリーに
        /// 枝を足すだけなので、必要なのは枝の番号だけになる。
        /// </summary>
        /// <param name="template">テンプレートの根ツリー。埋まっている番号をここから読む</param>
        public static List<ResolvedCrouchPose> ResolveCrouch(BlendTree template, List<string> warnings)
        {
            List<SupinePosePack> packs = CollectPacks(warnings);
            List<ResolvedCrouchPose> resolved = new List<ResolvedCrouchPose>();
            if (packs.Count == 0) return resolved;

            HashSet<int> usedIndices = CollectUsedCrouchIndices(template);
            HashSet<string> usedIds = new HashSet<string>();
            List<ResolvedCrouchPose> needsIndex = new List<ResolvedCrouchPose>();

            foreach (SupinePosePack pack in packs)
            {
                if (pack.crouchPoses == null) continue;

                foreach (SupineCrouchEntry entry in pack.crouchPoses)
                {
                    if (!ValidateCrouch(pack, entry, usedIds, warnings)) continue;

                    ResolvedCrouchPose pose = new ResolvedCrouchPose { Pack = pack, Entry = entry };

                    // 0番は Default（そのアバターが元々そうだった姿）の指定席なので譲らない
                    if (entry.preferredValue > 0 && !usedIndices.Contains(entry.preferredValue))
                    {
                        pose.Index = entry.preferredValue;
                        usedIndices.Add(entry.preferredValue);
                    }
                    else
                    {
                        if (entry.preferredValue > 0)
                        {
                            warnings.Add(
                                "Crouch pose " + DescribeCrouch(pack, entry) + " wants slot " +
                                entry.preferredValue + ", but it is already taken. " +
                                "A free slot was assigned instead.");
                        }
                        needsIndex.Add(pose);
                    }

                    resolved.Add(pose);
                }
            }

            AssignFreeIndices(needsIndex, usedIndices, warnings);

            resolved.RemoveAll(pose => pose.Index < 0);
            return resolved;
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
            List<ResolvedCrouchPose> entries = new List<ResolvedCrouchPose>();
            HashSet<string> usedIds = new HashSet<string>();
            List<string> ignored = new List<string>();

            foreach (SupinePosePack pack in CollectPacks(ignored))
            {
                if (pack.crouchPoses == null) continue;

                foreach (SupineCrouchEntry entry in pack.crouchPoses)
                {
                    if (!ValidateCrouch(pack, entry, usedIds, ignored)) continue;

                    entries.Add(new ResolvedCrouchPose { Pack = pack, Entry = entry });
                }
            }

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

        private static void AssignFreeIndices(
            List<ResolvedCrouchPose> needsIndex, HashSet<int> usedIndices, List<string> warnings)
        {
            // 1から探す。0は Default の指定席
            int next = 1;

            foreach (ResolvedCrouchPose pose in needsIndex)
            {
                while (next <= SupineCrouchValues.MaxIndex && usedIndices.Contains(next)) next++;

                if (next > SupineCrouchValues.MaxIndex)
                {
                    warnings.Add(
                        "Ran out of crouch pose slots. " + DescribeCrouch(pose.Pack, pose.Entry) +
                        " was skipped. A synced float only reaches " +
                        (SupineCrouchValues.MaxIndex + 1) + " distinct values at this spacing.");
                    continue;
                }

                pose.Index = next;
                usedIndices.Add(next);
            }
        }

        private static bool ValidateCrouch(
            SupinePosePack pack, SupineCrouchEntry entry, HashSet<string> usedIds, List<string> warnings)
        {
            if (entry == null || string.IsNullOrEmpty(entry.id))
            {
                warnings.Add("Pack '" + pack.ResolvePackId() + "' has a crouch pose with no id. It was skipped.");
                return false;
            }

            if (entry.clip == null)
            {
                warnings.Add("Crouch pose " + DescribeCrouch(pack, entry) + " has no animation clip. It was skipped.");
                return false;
            }

            if (!usedIds.Add(pack.ResolvePackId() + "/" + entry.id))
            {
                warnings.Add(
                    "Pack '" + pack.ResolvePackId() + "' declares the crouch pose id '" + entry.id +
                    "' more than once. The duplicate was skipped.");
                return false;
            }

            return true;
        }

        private static string DescribeCrouch(SupinePosePack pack, SupineCrouchEntry entry)
        {
            return "'" + entry.id + "' in pack '" + pack.ResolvePackId() + "'";
        }

        /// <summary>
        /// パックを集めて、組込をやり直しても同じ順になるよう並べる。
        ///
        /// 並びが揺れると採番も揺れる。メニュー項目は isSaved のため、
        /// 採番がずれると前回保存された選択が別のポーズを指してしまう。
        /// </summary>
        private static List<SupinePosePack> CollectPacks(List<string> warnings)
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

        private static int ComparePacks(SupinePosePack left, SupinePosePack right)
        {
            int order = left.sortOrder.CompareTo(right.sortOrder);
            if (order != 0) return order;

            order = string.CompareOrdinal(left.ResolvePackId(), right.ResolvePackId());
            if (order != 0) return order;

            return string.CompareOrdinal(
                AssetDatabase.GetAssetPath(left), AssetDatabase.GetAssetPath(right));
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

        private static void AssignFreeValues(
            List<ResolvedPose> needsValue, HashSet<int> usedValues, List<string> warnings)
        {
            int next = GeneratedValueMin;

            foreach (ResolvedPose pose in needsValue)
            {
                while (next <= GeneratedValueMax && usedValues.Contains(next)) next++;

                if (next > GeneratedValueMax)
                {
                    warnings.Add(
                        "Ran out of VRCSupine values. Pose " + Describe(pose.Pack, pose.Entry) + " was skipped.");
                    continue;
                }

                pose.Value = next;
                usedValues.Add(next);
            }
        }

        private static bool Validate(
            SupinePosePack pack, SupinePoseEntry entry, HashSet<string> usedIds, List<string> warnings)
        {
            if (entry == null || string.IsNullOrEmpty(entry.id))
            {
                warnings.Add("Pack '" + pack.ResolvePackId() + "' has a pose with no id. It was skipped.");
                return false;
            }

            if (entry.clip == null)
            {
                warnings.Add("Pose " + Describe(pack, entry) + " has no animation clip. It was skipped.");
                return false;
            }

            // 同じidが二度出てくると、生成物のどちらがどれだか追えなくなる
            if (!usedIds.Add(pack.ResolvePackId() + "/" + entry.id))
            {
                warnings.Add(
                    "Pack '" + pack.ResolvePackId() + "' declares the pose id '" + entry.id +
                    "' more than once. The duplicate was skipped.");
                return false;
            }

            // 0や1のまま出荷されると「しゃがんだ瞬間に解除される」「二度と解除できない」ポーズになる
            if (entry.uprightThreshold <= 0f || entry.uprightThreshold >= 1f)
            {
                warnings.Add(
                    "Pose " + Describe(pack, entry) + " has an out-of-range upright threshold (" +
                    entry.uprightThreshold.ToString("0.###") + "). It was skipped.");
                return false;
            }

            return true;
        }

        private static string Describe(SupinePosePack pack, SupinePoseEntry entry)
        {
            return "'" + entry.id + "' in pack '" + pack.ResolvePackId() + "'";
        }
    }
}
