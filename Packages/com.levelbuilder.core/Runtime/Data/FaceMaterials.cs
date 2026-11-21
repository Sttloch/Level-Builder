using System;
using UnityEngine;

namespace LevelBuilder
{
    /// <summary>Per-face materials of a wall. Null = the renderer's default material.</summary>
    [Serializable]
    public class WallFaceMaterials
    {
        public Material left, right, caps, top;

        public WallFaceMaterials Clone() => new WallFaceMaterials { left = left, right = right, caps = caps, top = top };

        /// <summary>Swaps left and right (the segment was reversed).</summary>
        public void SwapSides() => (left, right) = (right, left);
    }

    /// <summary>Material of an opening's jambs, sill and threshold.</summary>
    [Serializable]
    public class OpeningFaceMaterials
    {
        public Material jamb;

        public OpeningFaceMaterials Clone() => new OpeningFaceMaterials { jamb = jamb };
    }

    [Serializable]
    public class StairFaceMaterials
    {
        public Material tread, riser, side, landing, rail;

        public StairFaceMaterials Clone() => new StairFaceMaterials
        { tread = tread, riser = riser, side = side, landing = landing, rail = rail };
    }

    /// <summary>
    /// Materials of a floor region (room), anchored by a plan point: it belongs to the region that contains
    /// the seed, so it survives wall moves and stays on the seed side when a room is split.
    /// </summary>
    [Serializable]
    public class RegionPaint
    {
        public Vector2 seed;
        public Material floor, ceiling, edge;
    }
}
