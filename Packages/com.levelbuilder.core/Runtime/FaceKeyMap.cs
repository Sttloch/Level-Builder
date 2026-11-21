using System;
using System.Collections.Generic;
using UnityEngine;

namespace LevelBuilder
{
    /// <summary>
    /// Generated alongside each mesh object (walls, floor, ceiling, steps, rails): which face key each triangle of
    /// each submesh belongs to, so a raycast hit (submesh + triangle) can be turned into a paintable face.
    /// </summary>
    [AddComponentMenu("")]
    public class FaceKeyMap : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public string key;
            public int submesh;
            /// <summary>First triangle (index within the submesh) and number of triangles of the face.</summary>
            public int firstTri, triCount;
        }

        public List<Entry> entries = new List<Entry>();

        /// <summary>The face key of the triangle, or null.</summary>
        public string KeyAt(int submesh, int triangleIndexInSubmesh)
        {
            foreach (var e in entries)
                if (e.submesh == submesh && triangleIndexInSubmesh >= e.firstTri && triangleIndexInSubmesh < e.firstTri + e.triCount)
                    return e.key;
            return null;
        }
    }
}
