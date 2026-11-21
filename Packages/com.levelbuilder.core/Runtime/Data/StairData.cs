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
        public float width = 1.2f;
        public float riserTarget = 0.18f;
        public float tread = 0.28f;
        public bool railLeft = true;
        public bool railRight = true;
        public bool collisionRamp = true;
        /// <summary>
        /// Free height the slab hole guarantees over the walking surface at its edge (headroom plus capsule
        /// allowance; StairLayout.StepAllowance is always added on top). Range 1.5–4.0.
        /// </summary>
        public float headroom = 2.25f;
        /// <summary>Extra metres the hole extends toward the stair start and widens on each side. Range 0–1.</summary>
        public float holeMargin = 0f;
        public StairFaceMaterials materials = new StairFaceMaterials();
    }
}
