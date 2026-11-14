using System;
using UnityEngine;

namespace LevelBuilder
{
    [Serializable]
    public class WallNode
    {
        public string id;
        public Vector2 position;
        /// <summary>Id of the node on the floor below forming a column with this one; null/empty = no link.</summary>
        public string linkedBelowId;
    }
}
