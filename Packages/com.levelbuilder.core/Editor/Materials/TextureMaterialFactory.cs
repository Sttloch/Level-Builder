using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LevelBuilder.Editor
{
    /// <summary>Builds materials from texture sets recognised by name suffix, using the active pipeline Lit shader.
    /// Pipeline packages are not referenced: detection is by type/shader name.</summary>
    public static class TextureMaterialFactory
    {
        public enum MapKind { Albedo, Normal, Roughness, Metallic, Occlusion, Height, Unknown }

        enum Pipeline { BuiltIn, Urp, Hdrp }

        static readonly Dictionary<string, MapKind> Suffixes = new Dictionary<string, MapKind>(StringComparer.OrdinalIgnoreCase)
        {
            { "albedo", MapKind.Albedo }, { "basecolor", MapKind.Albedo }, { "diffuse", MapKind.Albedo },
            { "color", MapKind.Albedo }, { "col", MapKind.Albedo },
            { "normal", MapKind.Normal }, { "nrm", MapKind.Normal }, { "nor", MapKind.Normal }, { "n", MapKind.Normal },
            { "rough", MapKind.Roughness }, { "roughness", MapKind.Roughness },
            { "metal", MapKind.Metallic }, { "metallic", MapKind.Metallic },
            { "ao", MapKind.Occlusion }, { "occlusion", MapKind.Occlusion },
            { "height", MapKind.Height }, { "disp", MapKind.Height }, { "displacement", MapKind.Height },
            { "diff", MapKind.Albedo }, { "ambientocclusion", MapKind.Occlusion },
            { "normalgl", MapKind.Normal }, { "normaldx", MapKind.Normal },
            // multi-part suffixes, written with '_' regardless of the separator used in the name
            { "nor_gl", MapKind.Normal }, { "nor_dx", MapKind.Normal },
            { "normal_gl", MapKind.Normal }, { "normal_dx", MapKind.Normal },
        };

        static int SepIndex(string name) => name == null ? -1 : Math.Max(name.LastIndexOf('_'), name.LastIndexOf('-'));

        /// <summary>Finds a recognised suffix (one or two parts); returns the index of its separator, or -1.</summary>
        static int SuffixStart(string name, out MapKind kind)
        {
            kind = MapKind.Unknown;
            int i = SepIndex(name);
            if (i < 0) return -1;
            int j = i > 0 ? Math.Max(name.LastIndexOf('_', i - 1), name.LastIndexOf('-', i - 1)) : -1;
            if (j >= 0)
            {
                var two = name.Substring(j + 1).Replace('-', '_');
                if (Suffixes.TryGetValue(two, out kind)) return j;
            }
            if (Suffixes.TryGetValue(name.Substring(i + 1), out kind)) return i;
            kind = MapKind.Unknown;
            return -1;
        }

        /// <summary>No separator = albedo; separator with unrecognised suffix = Unknown.</summary>
        public static MapKind Classify(string textureName)
        {
            if (SepIndex(textureName) < 0) return MapKind.Albedo;
            SuffixStart(textureName, out var k);
            return k;
        }

        /// <summary>Name without the recognised suffix; unchanged when there is none.</summary>
        public static string BaseName(string textureName)
        {
            int i = SuffixStart(textureName, out _);
            return i < 0 ? textureName : textureName.Substring(0, i);
        }

        /// <summary>Groups textures by base name (case-insensitive). A texture with an unrecognised suffix is
        /// treated as the albedo of its full name, unless that group already has an explicit albedo.</summary>
        public static Dictionary<string, Dictionary<MapKind, Texture2D>> Group(IEnumerable<Texture2D> textures)
        {
            var result = new Dictionary<string, Dictionary<MapKind, Texture2D>>(StringComparer.OrdinalIgnoreCase);
            if (textures == null) return result;
            var unknown = new List<Texture2D>();
            foreach (var t in textures)
            {
                if (t == null) continue;
                var kind = Classify(t.name);
                if (kind == MapKind.Unknown) { unknown.Add(t); continue; }
                var key = BaseName(t.name);
                if (!result.TryGetValue(key, out var maps))
                    result[key] = maps = new Dictionary<MapKind, Texture2D>();
                if (!maps.ContainsKey(kind)) maps[kind] = t;
            }
            foreach (var t in unknown)
            {
                if (!result.TryGetValue(t.name, out var maps))
                    result[t.name] = maps = new Dictionary<MapKind, Texture2D>();
                if (!maps.ContainsKey(MapKind.Albedo)) maps[MapKind.Albedo] = t;
            }
            return result;
        }

        static Pipeline DetectPipeline()
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null) return Pipeline.BuiltIn;
            var names = rp.GetType().Name + " " + (rp.defaultShader != null ? rp.defaultShader.name : "");
            if (names.Contains("HDRenderPipeline") || names.Contains("HDRP")) return Pipeline.Hdrp;
            if (names.Contains("Universal")) return Pipeline.Urp;
            return Pipeline.BuiltIn;
        }

        public static Shader PipelineLitShader()
        {
            Shader s = null;
            switch (DetectPipeline())
            {
                case Pipeline.Urp: s = Shader.Find("Universal Render Pipeline/Lit"); break;
                case Pipeline.Hdrp: s = Shader.Find("HDRP/Lit"); break;
            }
            return s != null ? s : Shader.Find("Standard");
        }

        internal static void EnsureFolder(string folder)
        {
            folder = folder.TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folder)) return;
            int i = folder.LastIndexOf('/');
            if (i <= 0) return;
            var parent = folder.Substring(0, i);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(i + 1));
        }

        static void Assign(Material m, string prop, Texture2D tex, string keyword = null)
        {
            if (tex == null || !m.HasProperty(prop)) return;
            m.SetTexture(prop, tex);
            if (keyword != null) m.EnableKeyword(keyword);
        }

        /// <summary>Creates and saves a material for the map set. Roughness is kept as-is (Unity uses smoothness = 1 - roughness),
        /// so it has no slot on the stock shaders and is not assigned.</summary>
        public static Material Create(string baseName, Dictionary<MapKind, Texture2D> maps, string folder)
        {
            EnsureFolder(folder);
            maps = maps ?? new Dictionary<MapKind, Texture2D>();

            foreach (var kv in maps)
            {
                if (kv.Value == null) continue;
                var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(kv.Value)) as TextureImporter;
                if (imp == null) continue;
                bool changed = false;
                if (kv.Key == MapKind.Normal && imp.textureType != TextureImporterType.NormalMap)
                { imp.textureType = TextureImporterType.NormalMap; changed = true; }
                else if ((kv.Key == MapKind.Roughness || kv.Key == MapKind.Metallic || kv.Key == MapKind.Occlusion || kv.Key == MapKind.Height)
                         && imp.sRGBTexture)
                { imp.sRGBTexture = false; changed = true; }
                if (changed) imp.SaveAndReimport();
            }

            var shader = PipelineLitShader();
            if (DetectPipeline() != Pipeline.BuiltIn && shader != null && shader.name == "Standard")
                Debug.LogWarning("LevelBuilder: no se encontro el shader Lit del pipeline activo; se usa Standard.");
            var mat = new Material(shader) { name = baseName };
            Texture2D Get(MapKind k) => maps.TryGetValue(k, out var t) ? t : null;

            var sn = shader.name;
            var family = sn.Contains("HDRP") ? Pipeline.Hdrp : sn.Contains("Universal") ? Pipeline.Urp : Pipeline.BuiltIn;
            switch (family)
            {
                case Pipeline.Hdrp:
                    Assign(mat, "_BaseColorMap", Get(MapKind.Albedo));
                    Assign(mat, "_NormalMap", Get(MapKind.Normal), "_NORMALMAP");
                    Assign(mat, "_HeightMap", Get(MapKind.Height));
                    break;
                case Pipeline.Urp:
                    Assign(mat, "_BaseMap", Get(MapKind.Albedo));
                    Assign(mat, "_BumpMap", Get(MapKind.Normal), "_NORMALMAP");
                    Assign(mat, "_OcclusionMap", Get(MapKind.Occlusion), "_OCCLUSIONMAP");
                    Assign(mat, "_MetallicGlossMap", Get(MapKind.Metallic), "_METALLICSPECGLOSSMAP");
                    Assign(mat, "_ParallaxMap", Get(MapKind.Height), "_PARALLAXMAP");
                    break;
                default:
                    Assign(mat, "_MainTex", Get(MapKind.Albedo));
                    Assign(mat, "_BumpMap", Get(MapKind.Normal), "_NORMALMAP");
                    Assign(mat, "_OcclusionMap", Get(MapKind.Occlusion));
                    Assign(mat, "_MetallicGlossMap", Get(MapKind.Metallic), "_METALLICGLOSSMAP");
                    Assign(mat, "_ParallaxMap", Get(MapKind.Height), "_PARALLAXMAP");
                    break;
            }

            var assetPath = AssetDatabase.GenerateUniqueAssetPath(folder.TrimEnd('/') + "/" + baseName + ".mat");
            AssetDatabase.CreateAsset(mat, assetPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        }
    }
}
