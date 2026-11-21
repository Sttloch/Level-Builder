using System;
using UnityEngine;
using UnityEngine.Events;

namespace LevelBuilder
{
    public enum OpeningType { Door, Window, Empty }
    public enum DoorKind { Swing, Sliding }
    public enum DoorActivation { Proximity, Interact, Script }

    [Serializable]
    public class DoorSettings
    {
        public DoorKind kind = DoorKind.Swing;
        public bool hingeOnRight;
        public bool bothWays = true;
        public float openAngle = 95f;
        /// <summary>Full 0-to-1 turns per second.</summary>
        public float speed = 2f;
        public DoorActivation activation = DoorActivation.Proximity;
        public KeyCode key = KeyCode.E;
        public string playerTag = "Player";
        public bool locked;
        public GameObject leafPrefab;
        public UnityEvent onOpened = new UnityEvent();
        public UnityEvent onClosed = new UnityEvent();

        /// <summary>Copies every value and the prefab reference; the events are fresh and empty.</summary>
        public DoorSettings Clone() => new DoorSettings
        {
            kind = kind,
            hingeOnRight = hingeOnRight,
            bothWays = bothWays,
            openAngle = openAngle,
            speed = speed,
            activation = activation,
            key = key,
            playerTag = playerTag,
            locked = locked,
            leafPrefab = leafPrefab
        };
    }

    [Serializable]
    public class WallOpening
    {
        public string id;
        public OpeningType type;
        /// <summary>Center in meters from the segment's start node.</summary>
        public float center;
        public float width;
        public float height;
        public float sill;
        public DoorSettings door = new DoorSettings();
        public GameObject windowPrefab;
        public OpeningFaceMaterials materials = new OpeningFaceMaterials();

        /// <summary>Copy with a new id; UnityEvents are copied empty.</summary>
        public WallOpening Clone() => new WallOpening
        {
            id = IdUtil.New(),
            type = type,
            center = center,
            width = width,
            height = height,
            sill = sill,
            door = door != null ? door.Clone() : new DoorSettings(),
            windowPrefab = windowPrefab,
            materials = materials != null ? materials.Clone() : new OpeningFaceMaterials()
        };
    }
}
