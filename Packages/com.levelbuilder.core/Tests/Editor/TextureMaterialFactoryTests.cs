using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using LevelBuilder.Editor;
using MapKind = LevelBuilder.Editor.TextureMaterialFactory.MapKind;

namespace LevelBuilder.Tests
{
    public class TextureMaterialFactoryTests
    {
        const string Root = "Assets/__LBTexTests";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
            AssetDatabase.Refresh();
        }

        [TestCase("Brick_Normal", MapKind.Normal)]
        [TestCase("brick_n", MapKind.Normal)]
        [TestCase("Brick_Rough", MapKind.Roughness)]
        [TestCase("Brick_AO", MapKind.Occlusion)]
        [TestCase("Brick_BaseColor", MapKind.Albedo)]
        [TestCase("Brick", MapKind.Albedo)]
        [TestCase("Brick_Foo", MapKind.Unknown)]
        [TestCase("Brick-Metallic", MapKind.Metallic)]
        [TestCase("Brick_DISP", MapKind.Height)]
        [TestCase("Brick_NormalGL", MapKind.Normal)]
        [TestCase("Brick_nor_gl", MapKind.Normal)]
        [TestCase("Brick-Nor-DX", MapKind.Normal)]
        [TestCase("Brick_AmbientOcclusion", MapKind.Occlusion)]
        [TestCase("Brick_Displacement", MapKind.Height)]
        [TestCase("Brick_Diff", MapKind.Albedo)]
        [TestCase("Ladrillo_Rojo", MapKind.Unknown)]
        public void Classify_RecognisesSuffixes(string name, MapKind expected)
        {
            Assert.AreEqual(expected, TextureMaterialFactory.Classify(name));
        }

        [Test]
        public void BaseName_StripsRecognisedSuffix()
        {
            Assert.AreEqual("Brick", TextureMaterialFactory.BaseName("Brick_Normal"));
            Assert.AreEqual("Brick", TextureMaterialFactory.BaseName("Brick"));
            Assert.AreEqual("Brick_Foo", TextureMaterialFactory.BaseName("Brick_Foo"));
        }

        static Texture2D MakeTexture(string folder, string name)
        {
            Directory.CreateDirectory(folder);
            var t = new Texture2D(4, 4);
            var path = folder + "/" + name + ".png";
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        [Test]
        public void Group_ByBaseName()
        {
            AssetDatabase.CreateFolder("Assets", "__LBTexTests");
            var list = new List<Texture2D>
            {
                MakeTexture(Root, "Brick_Albedo"), MakeTexture(Root, "Brick_Normal"), MakeTexture(Root, "Wood")
            };
            var g = TextureMaterialFactory.Group(list);
            Assert.AreEqual(2, g.Count);
            Assert.AreEqual(2, g["Brick"].Count);
            Assert.IsTrue(g["Brick"].ContainsKey(MapKind.Normal));
            Assert.IsTrue(g["Wood"].ContainsKey(MapKind.Albedo));
        }

        [Test]
        public void BaseName_MultiPartSuffix()
        {
            Assert.AreEqual("Brick", TextureMaterialFactory.BaseName("Brick_nor_gl"));
            Assert.AreEqual("Ladrillo_Rojo", TextureMaterialFactory.BaseName("Ladrillo_Rojo_Normal"));
        }

        [Test]
        public void Group_UnknownSuffixIsAlbedoOfFullName()
        {
            AssetDatabase.CreateFolder("Assets", "__LBTexTests");
            var g = TextureMaterialFactory.Group(new List<Texture2D>
            {
                MakeTexture(Root, "Ladrillo_Rojo"), MakeTexture(Root, "Ladrillo_Rojo_Normal"),
                MakeTexture(Root, "Muro_2K"),
                MakeTexture(Root, "Brick_Albedo"), MakeTexture(Root, "Brick_Foo"),
            });
            Assert.IsTrue(g["Ladrillo_Rojo"].ContainsKey(MapKind.Albedo));
            Assert.IsTrue(g["Ladrillo_Rojo"].ContainsKey(MapKind.Normal));
            Assert.IsTrue(g["Muro_2K"].ContainsKey(MapKind.Albedo));
            Assert.AreEqual("Brick_Albedo", g["Brick"][MapKind.Albedo].name);
            Assert.IsTrue(g.ContainsKey("Brick_Foo"));
        }

        [Test]
        public void Group_AliasesAndCaseInsensitive()
        {
            AssetDatabase.CreateFolder("Assets", "__LBTexTests");
            var g = TextureMaterialFactory.Group(new List<Texture2D>
            {
                MakeTexture(Root, "Rock_Diff"), MakeTexture(Root, "ROCK_nor_gl"), MakeTexture(Root, "rock_Displacement"),
            });
            Assert.AreEqual(1, g.Count);
            var m = g["Rock"];
            Assert.IsTrue(m.ContainsKey(MapKind.Albedo) && m.ContainsKey(MapKind.Normal) && m.ContainsKey(MapKind.Height));
        }

        [Test]
        public void Create_DisablesSrgbOnDataMaps()
        {
            AssetDatabase.CreateFolder("Assets", "__LBTexTests");
            var ao = MakeTexture(Root, "Brick_AO");
            TextureMaterialFactory.Create("Brick", new Dictionary<MapKind, Texture2D> { { MapKind.Occlusion, ao } }, Root + "/M");
            var imp = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(ao));
            Assert.IsFalse(imp.sRGBTexture);
        }

        [Test]
        public void Create_UsesPipelineShaderAndAssignsMaps()
        {
            AssetDatabase.CreateFolder("Assets", "__LBTexTests");
            var maps = new Dictionary<MapKind, Texture2D>
            {
                { MapKind.Albedo, MakeTexture(Root, "Brick_Albedo") },
                { MapKind.Normal, MakeTexture(Root, "Brick_Normal") },
            };
            var folder = Root + "/Sub/Materiales";
            var mat = TextureMaterialFactory.Create("Brick", maps, folder);
            Assert.IsNotNull(mat);
            Assert.AreEqual(TextureMaterialFactory.PipelineLitShader(), mat.shader);
            Assert.IsTrue(AssetDatabase.GetAssetPath(mat).StartsWith(folder));
            Assert.AreEqual(maps[MapKind.Albedo], mat.GetTexture("_MainTex"));
            Assert.AreEqual(maps[MapKind.Normal], mat.GetTexture("_BumpMap"));
            Assert.IsTrue(mat.IsKeywordEnabled("_NORMALMAP"));
            var imp = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(maps[MapKind.Normal]));
            Assert.AreEqual(TextureImporterType.NormalMap, imp.textureType);
        }

        [Test]
        public void DefaultLibrary_CreatesFourEntriesOnce()
        {
            var lib = DefaultLibrary.CreateOrGet(Root);
            Assert.IsNotNull(lib);
            Assert.AreEqual(4, lib.entries.Count);
            foreach (var e in lib.entries)
            {
                Assert.IsNotNull(e.material);
                Assert.AreEqual(Vector2.one, e.size);
                Assert.AreEqual("Greybox", e.category);
            }
            var again = DefaultLibrary.CreateOrGet(Root);
            Assert.AreSame(lib, again);
            Assert.AreEqual(4, again.entries.Count);
        }
    }
}
