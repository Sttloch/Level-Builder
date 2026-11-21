using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using LevelBuilder.Editor;

namespace LevelBuilder.Tests
{
    public class PaletteStateTests
    {
        const string Root = "Assets/__LBPaletteTests";
        MaterialLibrary lib;

        [SetUp]
        public void SetUp()
        {
            lib = ScriptableObject.CreateInstance<MaterialLibrary>();
            lib.saveFolder = Root + "/Mats";
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(lib);
            if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
            AssetDatabase.Refresh();
        }

        static Texture2D MakeTexture(string name)
        {
            Directory.CreateDirectory(Root);
            var t = new Texture2D(4, 4);
            var path = Root + "/" + name + ".png";
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        [Test]
        public void AddDropped_Material_AddsEntryOnce()
        {
            var m = new Material(Shader.Find("Standard")) { name = "Drop" };
            var e1 = PaletteOps.AddDropped(lib, new Object[] { m }, out int c1);
            var e2 = PaletteOps.AddDropped(lib, new Object[] { m }, out int c2);
            Assert.AreEqual(1, lib.entries.Count);
            Assert.AreEqual(1, c1);
            Assert.AreEqual(0, c2);
            Assert.AreSame(e1, e2);
            Object.DestroyImmediate(m);
        }

        [Test]
        public void AddDropped_Textures_CreatesOneMaterialPerBaseName()
        {
            var objs = new Object[]
            {
                MakeTexture("Brick_Albedo"), MakeTexture("Brick_Normal"), MakeTexture("Tile_Albedo")
            };
            var e = PaletteOps.AddDropped(lib, objs, out int created);
            Assert.AreEqual(2, created);
            Assert.AreEqual(2, lib.entries.Count);
            Assert.NotNull(e);
            Assert.IsTrue(lib.entries.All(x => x.material != null));
            Assert.IsTrue(lib.entries.Any(x => x.name == "Brick"));
            Assert.IsTrue(lib.entries.Any(x => x.name == "Tile"));
        }

        [TestCase("0.5 x 0.5", 0.5f, 0.5f)]
        [TestCase("0,5 x 0,5", 0.5f, 0.5f)]
        [TestCase("2x2", 2f, 2f)]
        [TestCase("1,5 × 3", 1.5f, 3f)]
        [TestCase("2;4", 2f, 4f)]
        [TestCase("3", 3f, 3f)]
        public void TryParseSize_Valid(string text, float x, float y)
        {
            Assert.IsTrue(PaletteOps.TryParseSize(text, out var s));
            Assert.AreEqual(x, s.x, 1e-5f);
            Assert.AreEqual(y, s.y, 1e-5f);
        }

        [TestCase("")] [TestCase("abc")] [TestCase("0 x 1")] [TestCase("-1 x 2")] [TestCase("1x2x3")]
        public void TryParseSize_Invalid(string text)
        {
            Assert.IsFalse(PaletteOps.TryParseSize(text, out _));
        }

        [Test]
        public void Filter_BySearchAndCategory()
        {
            var sh = Shader.Find("Standard");
            var a = new Material(sh) { name = "A" };
            var b = new Material(sh) { name = "B" };
            var c = new Material(sh) { name = "C" };
            lib.Add(a, "Ladrillo rojo").category = "Muros";
            lib.Add(b, "Ladrillo gris").category = "Suelos";
            lib.Add(c, "Madera").category = "Muros";
            Assert.AreEqual(3, PaletteOps.Filter(lib, "", "").Count());
            Assert.AreEqual(2, PaletteOps.Filter(lib, "ladrillo", "").Count());
            Assert.AreEqual(1, PaletteOps.Filter(lib, "LADRILLO", "Muros").Count());
            Assert.AreEqual(2, PaletteOps.Filter(lib, null, "Muros").Count());
            Assert.AreEqual(0, PaletteOps.Filter(lib, "zzz", "").Count());
            Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(c);
        }
    }
}
