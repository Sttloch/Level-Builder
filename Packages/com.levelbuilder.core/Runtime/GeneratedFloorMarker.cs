using UnityEngine;

namespace LevelBuilder
{
    /// <summary>
    /// Hidden marker on each generated floor root. ownerId is the InstanceID of the BuildingRenderer
    /// that created it (a plain int: survives domain reload, is not remapped by Instantiate), so a
    /// renderer can tell the meshes it owns from ones inherited through a clone.
    /// </summary>
    [AddComponentMenu("")]
    internal class GeneratedFloorMarker : MonoBehaviour
    {
        public int ownerId;
    }
}
