using System.Collections.Generic;

namespace Supine
{
    /// <summary>
    /// テンプレート側のステート名から、生成物での実名を引く表。
    ///
    /// 追加モードでは食い違いが2種類ある。
    /// ・流用したステート（しゃがみ、伏せ）は追加先の名前になる。こちらはRenamedStatesに載らない
    /// ・複製したステートは名前が衝突するとUnityが連番を付ける。こちらはRenamedStatesに載る
    /// 前者を拾い損ねると、入口のステート名を変えているアバターでポーズが黙って増えなくなる。
    ///
    /// 組込1回につき1つ作り、生成物を触る処理はすべてこれを通す。
    /// </summary>
    internal sealed class StateNameMap
    {
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>();

        public StateNameMap(SupineCombineOptions options, IReadOnlyDictionary<string, string> renamedStates)
        {
            if (options.mode == SupineCombineMode.Add)
            {
                foreach (KeyValuePair<string, string> pair in
                         SupineLocomotionAdder.BuildStateNameOverrides(options))
                {
                    // 空文字は「対応するステートを持たせない」の意味なので、名前としては使えない
                    if (string.IsNullOrEmpty(pair.Value)) continue;
                    _names[pair.Key] = pair.Value;
                }
            }

            if (renamedStates != null)
            {
                foreach (KeyValuePair<string, string> pair in renamedStates)
                {
                    _names[pair.Key] = pair.Value;
                }
            }
        }

        /// <summary>生成物での実名。食い違いが無ければテンプレート側の名前をそのまま返す</summary>
        public string Resolve(string templateStateName)
        {
            return _names.TryGetValue(templateStateName, out string resolved) ? resolved : templateStateName;
        }
    }
}
