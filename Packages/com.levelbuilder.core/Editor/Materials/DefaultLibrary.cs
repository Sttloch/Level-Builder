using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using MapKind = LevelBuilder.Editor.TextureMaterialFactory.MapKind;

namespace LevelBuilder.Editor
{
    /// <summary>Creates the default greybox material library on a generated 1 m grid texture.</summary>
    public static class DefaultLibrary
    {
        const string LibraryFile = "LevelBuilder Biblioteca.asset";
        const string GridFile = "Rejilla 1m.png";

        public static MaterialLibrary CreateOrGet(string folder = "Assets/LevelBuilder")
        {
            folder = folder.TrimEnd('/');
            var libPath = folder + "/" + LibraryFile;
            var existing = AssetDatabase.LoadAssetAtPath<MaterialLibrary>(libPath);
            if (existing != null) return existing;

            TextureMaterialFactory.EnsureFolder(folder);
            var matFolder = folder + "/Materiales";
            TextureMaterialFactory.EnsureFolder(matFolder);

            var grid = CreateGridTexture(folder + "/" + GridFile);

            var lib = ScriptableObject.CreateInstance<MaterialLibrary>();
            lib.saveFolder = matFolder;
            var defs = new (string name, Color tint)[]
            {
                ("Gris claro", new Color(0.78f, 0.78f, 0.78f)),
                ("Gris oscuro", new Color(0.35f, 0.35f, 0.38f)),
                ("Suelo", new Color(0.55f, 0.48f, 0.40f)),
                ("Acento", new Color(0.90f, 0.45f, 0.15f)),
            };
            foreach (var d in defs)
            {
                var mat = TextureMaterialFactory.Create(d.name,
                    new Dictionary<MapKind, Texture2D> { { MapKind.Albedo, grid } }, matFolder);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", d.tint);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", d.tint);
                EditorUtility.SetDirty(mat);
                var e = lib.Add(mat, d.name);
                e.category = "Greybox";
                e.size = Vector2.one;
            }

            AssetDatabase.CreateAsset(lib, libPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<MaterialLibrary>(libPath);
        }

        /// <summary>256x256 texture covering 1 m: light grey with a darker line along the cell borders.</summary>
        static Texture2D CreateGridTexture(string path)
        {
            const int size = 256, line = 3;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            var bg = new Color32(235, 235, 235, 255);
            var ln = new Color32(110, 110, 110, 255);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = (x < line || y < line) ? ln : bg;
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
