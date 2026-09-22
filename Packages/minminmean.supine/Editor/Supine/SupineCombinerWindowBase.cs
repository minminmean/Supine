using System;
using UnityEditor;
using UnityEngine;
using Supine.Utilities;

namespace Supine
{
    /// <summary>
    /// EX 4.x がコンパイルを通すためだけの抜け殻。
    ///
    /// EX 4.x は組込ウィンドウをこのクラスの派生として持ち、依存も
    /// minminmean.supine &gt;=4.5.1 としか書いていない。本体だけを 5.x に上げると
    /// このクラスが無いせいでコンパイルが止まり、アバターのアップロードまで巻き添えになる。
    /// 公開済みの依存指定は直せないので、本体側で型だけ残して EX の更新を促す。
    /// </summary>
    [Obsolete(SupineLegacyEx.UpdateMessage)]
    public abstract class SupineCombinerWindowBase : EditorWindow
    {
        protected abstract SupineVariant Variant { get; }
        protected abstract string FolderLabel { get; }
        protected abstract string PrefsKeyPrefix { get; }

        protected virtual void OnEnable()
        {
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(SupineLegacyEx.WindowMessage, MessageType.Warning);
        }
    }

    /// <summary>
    /// EX 4.x 向けの案内文。
    /// 廃止予定の警告は EX 4.x を入れた利用者のコンソールにも出るので、開発者向けのメモではなく更新の案内にする。
    /// </summary>
    internal static class SupineLegacyEx
    {
        public const string UpdateMessage =
            "ごろ寝システム EX を 5.0.0 以上に更新してください。 / Please update Gorone System EX to 5.0.0 or later.";

        public const string WindowMessage =
            "ごろ寝システム EX を 5.0.0 以上に更新してください。\n" +
            "EX 5.0.0 からは、ごろ寝システム本体の組込ウィンドウ（Tools/MinMinMart/Supine Combiner）で EX のポーズも組み込まれます。\n\n" +
            "Please update Gorone System EX to 5.0.0 or later.\n" +
            "From EX 5.0.0 on, the base Supine Combiner (Tools/MinMinMart/Supine Combiner) installs the EX poses as well.";
    }
}

namespace Supine.Utilities
{
    /// <summary>EX 4.x の組込ウィンドウが参照する型。中身は使わない</summary>
    [Obsolete(SupineLegacyEx.UpdateMessage)]
    [Serializable]
    public struct SupineVariant
    {
        public string prefab;
        public string controller;
    }

    /// <summary><see cref="JsonHelper.GetGuidList(string)"/> の戻り値。EX 4.x が variant だけを読む</summary>
    [Obsolete(SupineLegacyEx.UpdateMessage)]
    public struct LegacyGuidDictionary
    {
        public SupineVariant variant;
    }
}
