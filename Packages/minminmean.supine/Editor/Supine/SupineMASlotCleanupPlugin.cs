using nadena.dev.ndmf;
using UnityEngine;

[assembly: ExportsPlugin(typeof(Supine.SupineMASlotCleanupPlugin))]

namespace Supine
{
    /// <summary>
    /// SupineMASlotはエディタ上でごろ寝システムMA Prefabを識別するためだけのマーカーで、
    /// アバターとしてアップロードする実体には不要（VRCSDKに未対応コンポーネントとして警告される）。
    /// NDMFのビルド時に取り除く。
    /// </summary>
    public class SupineMASlotCleanupPlugin : Plugin<SupineMASlotCleanupPlugin>
    {
        protected override void Configure()
        {
            InPhase(BuildPhase.Optimizing).Run("Remove SupineMASlot markers", ctx =>
            {
                foreach (SupineMASlot slot in ctx.AvatarRootObject.GetComponentsInChildren<SupineMASlot>(true))
                {
                    Object.DestroyImmediate(slot);
                }
            });
        }
    }
}
