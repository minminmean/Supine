using UnityEditor;
using Supine.Utilities;

namespace Supine
{
    /// <summary>
    /// 通常版ごろ寝システムの組込ウィンドウ
    /// </summary>
    public sealed class SupineCombinerEditor : SupineCombinerWindowBase
    {
        protected override SupineVariant Variant  => JsonHelper.GetGuidList().variant;
        protected override string FolderLabel     => "Supine";
        protected override string PrefsKeyPrefix  => "MinMinMart.Supine";

        [MenuItem("Tools/MinMinMart/Supine Combiner")]
        private static void Create()
        {
            GetWindow<SupineCombinerEditor>("Supine Combiner");
        }
    }
}
