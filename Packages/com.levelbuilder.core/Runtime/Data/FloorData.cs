using System;
using System.Collections.Generic;

namespace LevelBuilder
{
    [Serializable]
    public class FloorData
    {
        public string id;
        public string name;
        public float elevation;
        public float height = 3f;
        public float slabThickness = 0.2f;
        public List<WallNode> nodes = new List<WallNode>();
        public List<WallSegment> segments = new List<WallSegment>();

        public WallNode GetNode(string id) => nodes.Find(n => n.id == id);
        public WallSegment GetSegment(string id) => segments.Find(s => s.id == id);
    }
}
