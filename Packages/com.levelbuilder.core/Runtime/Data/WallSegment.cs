using System;
using System.Collections.Generic;

namespace LevelBuilder
{
    [Serializable]
    public class WallSegment
    {
        public string id;
        public string startNodeId;
        public string endNodeId;
        public float thickness = 0.2f;
        /// <summary>Less than 0 means use the floor height.</summary>
        public float heightOverride = -1f;
        public List<WallOpening> openings = new List<WallOpening>();

        /// <summary>Shared height tolerance: walls at most this tall are wall-less edges; walls lower than floor height minus this are low.</summary>
        public const float HeightTol = 0.01f;

        /// <summary>Height within <see cref="HeightTol"/> of the floor height.</summary>
        public bool IsFullHeight(FloorData f) => Height(f) >= f.height - HeightTol;

        /// <summary>At most <see cref="HeightTol"/> tall: an edge without wall.</summary>
        public bool IsWallLess(FloorData f) => Height(f) <= HeightTol;

        public float Height(FloorData f) => heightOverride < 0f ? f.height : heightOverride;
    }
}
