using System;
using System.Collections.Generic;
using UnityEngine;

namespace LevelBuilder
{
    /// <summary>Holds the serialized building data. Generated meshes are rebuilt from it and never saved.</summary>
    [ExecuteAlways, SelectionBase, DisallowMultipleComponent]
    public class Building : MonoBehaviour
    {
        public BuildingData data = new BuildingData();
        public int activeFloor;

        public event Action Changed;

        bool dirtyAll;
        readonly HashSet<int> dirtyFloors = new HashSet<int>();

        /// <summary>floorIndex -1 = all floors.</summary>
        public void MarkDirty(int floorIndex = -1)
        {
            if (floorIndex < 0) dirtyAll = true; else dirtyFloors.Add(floorIndex);
            Changed?.Invoke();
        }

        /// <summary>Returns true if everything is dirty; otherwise fills floors with pending indices. Clears pending state.</summary>
        public bool ConsumeDirty(List<int> floors)
        {
            bool all = dirtyAll;
            floors.Clear();
            floors.AddRange(dirtyFloors);
            dirtyAll = false;
            dirtyFloors.Clear();
            return all;
        }

        void OnValidate() => MarkDirty();
    }
}
