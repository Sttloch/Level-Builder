using UnityEngine;

namespace LevelBuilder
{
    /// <summary>
    /// Hidden marker on each generated floor root. ownerId is the instance/entity id of the BuildingRenderer
    /// that created it (a plain number: survives domain reload, is not remapped by Instantiate), so a
    /// renderer can tell the meshes it owns from ones inherited through a clone.
    /// </summary>
    [AddComponentMenu("")]
    internal class GeneratedFloorMarker : MonoBehaviour
    {
        public ulong ownerId;

        /// <summary>Per-session identity of an object. GetInstanceID is an error from Unity 6.4 on; GetEntityId replaces it.</summary>
        internal static ulong KeyOf(Object o)
        {
#if UNITY_6000_4_OR_NEWER
            return EntityId.ToULong(o.GetEntityId());
#else
            return unchecked((ulong)(uint)o.GetInstanceID());
#endif
        }
    }
}
