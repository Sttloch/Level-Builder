using System;
using UnityEditor;
using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>Active material and library of the palette; persisted across domain reloads by asset GUID.</summary>
    public static class PaletteState
    {
        const string MaterialKey = "LevelBuilder.Palette.Material";
        const string LibraryKey = "LevelBuilder.Palette.Library";

        static Material active;
        static MaterialLibrary library;

        public static event Action Changed;

        public static Material Active
        {
            get { return active != null ? active : (active = Load<Material>(MaterialKey)); }
            set
            {
                if (active == value) return;
                active = value;
                Save(MaterialKey, value);
                Changed?.Invoke();
            }
        }

        public static MaterialLibrary Library
        {
            get { return library != null ? library : (library = Load<MaterialLibrary>(LibraryKey)); }
            set
            {
                if (library == value) return;
                library = value;
                Save(LibraryKey, value);
                Changed?.Invoke();
            }
        }

        static void Save(string key, UnityEngine.Object o)
        {
            var path = o != null ? AssetDatabase.GetAssetPath(o) : null;
            var guid = string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path);
            EditorPrefs.SetString(key, guid);
        }

        static T Load<T>(string key) where T : UnityEngine.Object
        {
            var guid = EditorPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(guid)) return null;
            var path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
        }
    }
}
