using System;

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

        public float Height(FloorData f) => heightOverride < 0f ? f.height : heightOverride;
    }
}
