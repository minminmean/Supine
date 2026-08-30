using System.Collections.Generic;
using Supine.Utilities;

namespace Supine
{
    /// <summary>組込できない理由。ユーザーに出すメッセージを理由ごとに出し分けるために持つ</summary>
    public enum SupineCombineFailure
    {
        None = 0,

        /// <summary>アバターではない</summary>
        NoAvatarDescriptor,

        /// <summary>guids.jsonが読めていない、または内容が欠けている</summary>
        InvalidVariant,

        /// <summary>追加先のアニメーターを解決できない</summary>
        AddTargetNotFound,

        /// <summary>追加先のアニメーターにレイヤーが無い</summary>
        AddTargetNoLayer,

        /// <summary>入口ステートが決まっていない。ごろ寝システムへ入る経路が作れない</summary>
        AddEntryStateNotSelected
    }

    /// <summary>検証結果。警告があっても組込自体は続行できる</summary>
    public sealed class SupineCheckResult
    {
        public SupineCombineFailure Failure = SupineCombineFailure.None;
        public readonly List<string> Warnings = new List<string>();

        public bool CanCombine  => Failure == SupineCombineFailure.None;
        public bool HasWarnings => Warnings.Count > 0;
    }

    internal static class SupineCombineFailureTable
    {
        public static string GetMessage(SupineCombineFailure failure, LocalizeDictionary dict)
        {
            switch (failure)
            {
                case SupineCombineFailure.NoAvatarDescriptor:   return dict.check_failure_message;
                case SupineCombineFailure.InvalidVariant:       return dict.check_failure_variant_message;
                case SupineCombineFailure.AddTargetNotFound:  return dict.check_failure_add_target_message;
                case SupineCombineFailure.AddTargetNoLayer:   return dict.check_failure_add_layer_message;
                case SupineCombineFailure.AddEntryStateNotSelected: return dict.check_failure_add_entry_message;
                default:                                        return dict.check_failure_message;
            }
        }
    }
}
