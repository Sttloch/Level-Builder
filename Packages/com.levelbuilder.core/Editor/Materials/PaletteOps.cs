using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>Testable palette logic: dropping assets into a library and filtering its entries.</summary>
    public static class PaletteOps
    {
        /// <summary>Adds dropped Materials, and one material per texture base name. Returns the first entry
        /// touched (new or existing); <paramref name="created"/> counts new entries.</summary>
        public static MaterialLibrary.Entry AddDropped(MaterialLibrary lib, UnityEngine.Object[] objects, out int created)
        {
            created = 0;
            if (lib == null || objects == null) return null;
            MaterialLibrary.Entry first = null;
            var textures = new List<Texture2D>();
            foreach (var o in objects)
            {
                if (o is Material m)
                {
                    int before = lib.entries.Count;
                    var e = lib.Add(m);
                    if (lib.entries.Count > before) created++;
                    if (first == null) first = e;
                }
                else if (o is Texture2D t) textures.Add(t);
            }
            if (textures.Count > 0)
            {
                foreach (var kv in TextureMaterialFactory.Group(textures))
                {
                    var mat = TextureMaterialFactory.Create(kv.Key, kv.Value, lib.saveFolder);
                    if (mat == null) continue;
                    int before = lib.entries.Count;
                    var e = lib.Add(mat, kv.Key);
                    if (lib.entries.Count > before) created++;
                    if (first == null) first = e;
                }
            }
            return first;
        }

        public static IEnumerable<MaterialLibrary.Entry> Filter(MaterialLibrary lib, string search, string category)
        {
            if (lib == null) yield break;
            bool hasSearch = !string.IsNullOrEmpty(search);
            bool hasCat = !string.IsNullOrEmpty(category);
            foreach (var e in lib.entries)
            {
                if (e == null) continue;
                if (hasCat && !string.Equals(e.category, category, StringComparison.OrdinalIgnoreCase)) continue;
                if (hasSearch && (e.name == null || e.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                yield return e;
            }
        }

        /// <summary>Parses "W x H" (separator x, X, × or ;; decimal '.' or ','). A single value gives a square size.</summary>
        public static bool TryParseSize(string text, out Vector2 size)
        {
            size = Vector2.one;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = text.Split('x', 'X', '×', ';');
            if (parts.Length > 2) return false;
            var v = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                var t = parts[i].Trim().Replace(',', '.');
                if (!float.TryParse(t, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out v[i]) || !(v[i] > 0f)
                    || float.IsInfinity(v[i])) return false;
            }
            size = new Vector2(v[0], v.Length > 1 ? v[1] : v[0]);
            return true;
        }

        public static string FormatSize(Vector2 s) =>
            s.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " x " +
            s.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        public static List<string> Categories(MaterialLibrary lib)
        {
            if (lib == null) return new List<string>();
            return lib.entries.Where(e => e != null && !string.IsNullOrEmpty(e.category))
                .Select(e => e.category).Distinct().OrderBy(c => c).ToList();
        }

        /// <summary>Marks every Building in open scenes that uses this library dirty so UVs/materials regenerate.</summary>
        public static void MarkBuildingsDirty(MaterialLibrary lib)
        {
            if (lib == null) return;
            foreach (var b in Resources.FindObjectsOfTypeAll<Building>())
            {
                if (b == null || b.library != lib) continue;
                if (EditorUtility.IsPersistent(b) || !b.gameObject.scene.IsValid()) continue;
                b.MarkDirty(-1);
            }
        }
    }
}
