using System;
using System.Collections.Generic;
using UnityEngine;

namespace LevelBuilder
{
    public enum StairPointKind { Corner, Smooth }

    [Serializable]
    public struct StairPoint
    {
        public Vector2 position;
        public StairPointKind kind;
    }

    [Serializable]
    public class StairData
    {
        public string id;
        public string fromFloorId;
        public string toFloorId;
        public List<StairPoint> route = new List<StairPoint>();
        public float width = 1f;
        public float riserTarget = 0.18f;
        public float tread = 0.28f;
        public bool railLeft = true;
        public bool railRight = true;
        public bool collisionRamp = true;
    }
}
