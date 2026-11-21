using System;
using System.Collections.Generic;
using UnityEngine;

namespace LevelBuilder
{
    /// <summary>Materials available for painting, with the real-world size (metres) each texture covers.
    /// Asset creation happens in the Editor.</summary>
    [CreateAssetMenu(menuName = "LevelBuilder/Biblioteca de materiales", fileName = "BibliotecaMateriales")]
    public class MaterialLibrary : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string name;
            public string category;
            public Material material;
            /// <summary>Metres covered by one texture repeat.</summary>
            public Vector2 size = Vector2.one;
        }

        public List<Entry> entries = new List<Entry>();
        public string saveFolder = "Assets/LevelBuilder/Materiales";

        public Entry Find(Material m)
        {
            if (m == null) return null;
            return entries.Find(e => e != null && e.material == m);
        }

        /// <summary>Texture size in metres; 1 x 1 when the material is not in the library.</summary>
        public Vector2 SizeOf(Material m)
        {
            var e = Find(m);
            return e != null ? e.size : Vector2.one;
        }

        /// <summary>Adds the material (or returns its existing entry).</summary>
        public Entry Add(Material m, string name = null)
        {
            if (m == null) return null;
            var e = Find(m);
            if (e != null) return e;
            e = new Entry { name = string.IsNullOrEmpty(name) ? m.name : name, material = m };
            entries.Add(e);
            return e;
        }
    }
}
