using UnityEngine;
using VRC.SDKBase;

namespace Supine
{
    /// <summary>
    /// ごろ寝システムMA Prefabのルートにつけるマーカー。
    /// 「既に設置されている他のごろ寝システムMA Prefab」を名前を知らずに検出・整理するために使う。
    /// IEditorOnlyを実装することで、VRCSDKの未対応コンポーネント警告やアバターへの実際の混入を防ぐ。
    ///
    /// 組込時の設定もここに控えておき、後から見返せるようにする。
    /// </summary>
    public class SupineMASlot : MonoBehaviour, IEditorOnly
    {
        public SupineCombineRecord record = new SupineCombineRecord();
    }
}
