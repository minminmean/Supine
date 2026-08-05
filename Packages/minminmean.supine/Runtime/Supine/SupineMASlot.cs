using UnityEngine;
using VRC.SDKBase;

namespace Supine
{
    /// <summary>
    /// ごろ寝システムMA Prefabのルートにつけるマーカー。
    /// 通常版/EX版どちらのバリアントかを問わず、
    /// 「既に設置されている他のごろ寝システムMA Prefab」を名前を知らずに検出・整理するために使う。
    /// IEditorOnlyを実装することで、VRCSDKの未対応コンポーネント警告やアバターへの実際の混入を防ぐ。
    /// </summary>
    public class SupineMASlot : MonoBehaviour, IEditorOnly
    {
    }
}
