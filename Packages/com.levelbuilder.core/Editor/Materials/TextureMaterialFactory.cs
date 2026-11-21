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
            { "height", MapKind.Height }, { "disp", MapKind.Height },
        };

        static int SepIndex(string name) => name == null ? -1 : Math.Max(name.LastIndexOf('_'), name.LastIndexOf('-'));

        /// <summary>No separator = albedo; separator with unrecognised suffix = Unknown.</summary>
        public static MapKind Classify(string textureName)
        {
            int i = SepIndex(textureName);
            if (i < 0) return MapKind.Albedo;
            return Suffixes.TryGetValue(textureName.Substring(i + 1), out var k) ? k : MapKind.Unknown;
        }

        /// <summary>Name without the recognised suffix; unchanged when there is none.</summary>
        public static string BaseName(string textureName)
        {
            int i = SepIndex(textureName);
            if (i < 0) return textureName;
            return Suffixes.ContainsKey(textureName.Substring(i + 1)) ? textureName.Substring(0, i) : textureName;
        }

        /// <summary>Groups textures by base name; textures with an unknown suffix are ignored.</summary>
        public static Dictionary<string, Dictionary<MapKind, Texture2D>> Group(IEnumerable<Texture2D> textures)
        {
            var result = new Dictionary<string, Dictionary<MapKind, Texture2D>>();
            if (textures == null) return result;
            foreach (var t in textures)
            {
                if (t == null) continue;
                var kind = Classify(t.name);
                if (kind == MapKind.Unknown) continue;
                var key = BaseName(t.name);
                if (!result.TryGetValue(key, out var maps))
                    result[key] = maps = new Dictionary<MapKind, Texture2D>();
                if (!maps.ContainsKey(kind)) maps[kind] = t;
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

            if (maps.TryGetValue(MapKind.Normal, out var n) && n != null)
            {
                var path = AssetDatabase.GetAssetPath(n);
                if (AssetImporter.GetAtPath(path) is TextureImporter imp && imp.textureType != TextureImporterType.NormalMap)
                {
                    imp.textureType = TextureImporterType.NormalMap;
                    imp.SaveAndReimport();
                }
            }

            var mat = new Material(PipelineLitShader()) { name = baseName };
            Texture2D Get(MapKind k) => maps.TryGetValue(k, out var t) ? t : null;

            switch (DetectPipeline())
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
