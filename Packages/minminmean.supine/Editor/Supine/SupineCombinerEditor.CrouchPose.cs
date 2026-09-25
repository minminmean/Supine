using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using Supine.Utilities;

namespace Supine
{
    /// <summary>
    /// 組込ウィンドウのうち、しゃがみポーズまわりの欄。
    /// 既存の切り替えとの衝突の確認と、既定にするしゃがみの選択。
    /// </summary>
    public sealed partial class SupineCombinerEditor
    {
        // 既存のしゃがみが入れ子のブレンドツリーかどうか。
        // 毎フレーム走査すると重いので、判定に効く入力が変わったときだけ引き直す
        private BlendTree _existingCrouchTree;
        private string _existingCrouchSignature;

        /// <summary>パックが足すしゃがみポーズ。選択肢を並べるためだけに持つ</summary>
        private List<PosePack.ResolvedCrouchPose> _crouchPackPoses =
            new List<PosePack.ResolvedCrouchPose>();

        private int _crouchPackGeneration;

        /// <summary>
        /// 既存のしゃがみポーズ切り替えとぶつかるときだけ、どちらを活かすか選ばせる。
        ///
        /// 他のツールも同じ手法（しゃがみステートに入れ子のブレンドツリーを差す）を
        /// 使っていることがある。黙って上書きすると相手の切り替えを乗っ取るので、
        /// 入れ子を見つけたときだけ選択肢を出す。
        /// ぶつかっていないときに出しても意味が分からないだけなので、普段は隠す。
        /// </summary>
        private void DrawCrouchPoseConflict(LocalizeDictionary localizeDict)
        {
            RefreshExistingCrouchTree();
            if (_existingCrouchTree == null) return;

            EditorGUILayout.HelpBox(localizeDict.crouch_conflict, MessageType.Warning);

            _options.keepExistingCrouchPose = EditorGUILayout.ToggleLeft(
                localizeDict.crouch_keep_existing, _options.keepExistingCrouchPose);
        }

        /// <summary>
        /// 既定にするしゃがみポーズ。CrouchPose パラメータの初期値になる。
        /// 既存を優先する選択をしているときは、そもそも組み込まないので伏せる。
        /// </summary>
        private void DrawCrouchPose(LocalizeDictionary localizeDict)
        {
            if (_options.keepExistingCrouchPose) return;

            RefreshCrouchPackPoses();

            string[] builtIn = CrouchPoseTable.GetLabels(localizeDict);
            string[] labels = new string[builtIn.Length + _crouchPackPoses.Count];
            builtIn.CopyTo(labels, 0);

            for (int i = 0; i < _crouchPackPoses.Count; i++)
            {
                labels[builtIn.Length + i] = _crouchPackPoses[i].Entry.ResolveDisplayName();
            }

            int selected = ResolveCrouchSelection(builtIn.Length);
            int chosen = EditorGUILayout.Popup(localizeDict.crouch_pose, selected, labels);
            if (chosen == selected) return;

            if (chosen < builtIn.Length)
            {
                _options.defaultCrouchPose = CrouchPoseTable.FromIndex(chosen);
                _options.defaultCrouchPoseKey = string.Empty;
            }
            else
            {
                PosePack.ResolvedCrouchPose pose = _crouchPackPoses[chosen - builtIn.Length];
                _options.defaultCrouchPoseKey = pose.Key;
            }
        }

        /// <summary>
        /// 選択中の項目が選択肢の何番目かを引く。
        ///
        /// パックを外したあとは、覚えていた識別子がどれにも当たらなくなる。
        /// その場合は組み込み側の選択へ黙って戻す。存在しないものを
        /// 選んだままにすると、組み込んだときだけ違うポーズになる。
        /// </summary>
        private int ResolveCrouchSelection(int builtInCount)
        {
            if (!string.IsNullOrEmpty(_options.defaultCrouchPoseKey))
            {
                for (int i = 0; i < _crouchPackPoses.Count; i++)
                {
                    if (_crouchPackPoses[i].Key != _options.defaultCrouchPoseKey) continue;

                    return builtInCount + i;
                }

                _options.defaultCrouchPoseKey = string.Empty;
            }

            return CrouchPoseTable.IndexOf(_options.defaultCrouchPose);
        }

        /// <summary>
        /// 選択肢の元になるパックの一覧を引き直す。
        /// AssetDatabase を舐めるので、更新を押したときだけにする。
        /// </summary>
        private void RefreshCrouchPackPoses()
        {
            if (_crouchPackGeneration == _refreshGeneration) return;

            _crouchPackGeneration = _refreshGeneration;
            _crouchPackPoses = PosePack.SupinePosePackRegistry.ListCrouchEntries();
        }

        /// <summary>
        /// 既存のしゃがみモーションの判定を引き直す。
        /// コントローラ全体を走査するため、入力が変わっていなければ前回の結果を使う。
        /// </summary>
        private void RefreshExistingCrouchTree()
        {
            string signature = string.Join("|", new[]
                {
                    _refreshGeneration.ToString(),
                    _avatarDescriptor != null ? _avatarDescriptor.GetInstanceID().ToString() : "0",
                    _options.mode.ToString(),
                    _options.addTargetOverride != null
                        ? _options.addTargetOverride.GetInstanceID().ToString()
                        : "0",
                    _options.mode == SupineCombineMode.Add
                        ? _options.entryStateName
                        : (_options.ShouldInherit ? _options.inheritCrouchingStateName : "-")
                });

            if (signature == _existingCrouchSignature) return;

            _existingCrouchSignature = signature;
            _existingCrouchTree =
                PosePack.SupineCrouchConflictDetector.FindExistingNestedTree(_avatarDescriptor, _options);
        }
    }
}
