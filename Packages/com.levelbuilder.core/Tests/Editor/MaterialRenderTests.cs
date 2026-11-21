using System.Linq;
using NUnit.Framework;
using UnityEngine;
using LevelBuilder;
using LevelBuilder.Geometry;

public class MaterialRenderTests
{
    GameObject go;
    BuildingRenderer r;
    Building b;
    Material m1, m2, baseMat;
    MaterialLibrary lib;

    [SetUp] public void SetUp()
    {
        var sh = Shader.Find("Standard");
        m1 = new Material(sh) { name = "M1" };
        m2 = new Material(sh) { name = "M2" };
        baseMat = new Material(sh) { name = "Base" };
        go = new GameObject("TestBuilding");
        r = go.AddComponent<BuildingRenderer>();
        b = go.GetComponent<Building>();
        lib = ScriptableObject.CreateInstance<MaterialLibrary>();
    }

    [TearDown] public void TearDown()
    {
        if (go != null) Object.DestroyImmediate(go);
        foreach (var o in new Object[] { m1, m2, baseMat, lib }) if (o != null) Object.DestroyImmediate(o);
    }

    FloorData Room(FloorData f, float x0, float x1, float z0 = 0f, float z1 = 4f)
    {
        b.data.AddSegment(f, new Vector2(x0, z0), new Vector2(x1, z0));
        b.data.AddSegment(f, new Vector2(x1, z0), new Vector2(x1, z1));
        b.data.AddSegment(f, new Vector2(x1, z1), new Vector2(x0, z1));
        b.data.AddSegment(f, new Vector2(x0, z1), new Vector2(x0, z0));
        return f;
    }

    WallSegment SegFrom(FloorData f, Vector2 a) =>
        f.segments.First(s => (f.GetNode(s.startNodeId).position - a).magnitude < 1e-3f);

    Transform Part(string path) => go.transform.Find(path);

    // Max u of the vertices used by the given face key in a part.
    float MaxU(string part, string key)
    {
        var t = Part(part);
        var mesh = t.GetComponent<MeshFilter>().sharedMesh;
        var map = t.GetComponent<FaceKeyMap>();
        var uvs = mesh.uv;
        float max = float.MinValue;
        foreach (var e in map.entries.Where(e => e.key == key))
        {
            var tris = mesh.GetTriangles(e.submesh);
            for (int i = e.firstTri * 3; i < (e.firstTri + e.triCount) * 3; i++) max = Mathf.Max(max, uvs[tris[i]].x);
        }
        return max;
    }

    [Test] public void PaintedWall_UsesItsMaterialAsSubmesh()
    {
        r.defaultMaterial = baseMat;
        var f = Room(b.data.AddFloor(), 0, 4);
        var s = SegFrom(f, Vector2.zero);
        FaceMaterialRules.Set(b.data, f, s.id + "/L", m1);
        r.Regenerate();
        var walls = Part("Floor 0/Walls");
        var mats = walls.GetComponent<MeshRenderer>().sharedMaterials;
        Assert.AreEqual(2, walls.GetComponent<MeshFilter>().sharedMesh.subMeshCount);
        Assert.AreEqual(2, mats.Length);
        CollectionAssert.AreEquivalent(new[] { baseMat, m1 }, mats);
        var map = walls.GetComponent<FaceKeyMap>();
        var e = map.entries.First(x => x.key == s.id + "/L");
        Assert.AreSame(m1, mats[e.submesh]);
    }

    [Test] public void UvScaleFollowsMaterialSize()
    {
        b.library = lib;
        lib.Add(m1).size = new Vector2(2f, 2f);
        var f = Room(b.data.AddFloor(), 0, 6);
        var s = SegFrom(f, Vector2.zero);
        FaceMaterialRules.Set(b.data, f, s.id + "/L", m1);
        r.Regenerate();
        Assert.AreEqual(3f, MaxU("Floor 0/Walls", s.id + "/L"), 0.1f);
    }

    [Test] public void LongerWall_RepeatsTexture()
    {
        var f = Room(b.data.AddFloor(), 0, 4);
        var s = SegFrom(f, Vector2.zero);
        r.Regenerate();
        float u4 = MaxU("Floor 0/Walls", s.id + "/L");
        var n = f.nodes.First(x => (x.position - new Vector2(4, 0)).magnitude < 0.01f);
        b.data.MoveNode(f, n.id, new Vector2(8, 0));
        r.Regenerate();
        float u8 = MaxU("Floor 0/Walls", s.id + "/L");
        Assert.AreEqual(4f, u4, 0.15f);
        Assert.Greater(u8, u4 + 3.5f);
    }

    [Test] public void MissingMaterial_FallsBackToDefault()
    {
        r.defaultMaterial = baseMat;
        var f = Room(b.data.AddFloor(), 0, 4);
        var s = SegFrom(f, Vector2.zero);
        FaceMaterialRules.Set(b.data, f, s.id + "/L", m2);
        Object.DestroyImmediate(m2);
        r.Regenerate();
        var mats = Part("Floor 0/Walls").GetComponent<MeshRenderer>().sharedMaterials;
        Assert.AreEqual(1, mats.Length);
        Assert.AreSame(baseMat, mats[0]);
    }

    [Test] public void FaceKeyMap_ReturnsKeyForTriangle()
    {
        var f = Room(b.data.AddFloor(), 0, 4);
        var s = SegFrom(f, Vector2.zero);
        FaceMaterialRules.Set(b.data, f, s.id + "/R", m1);
        r.Regenerate();
        var walls = Part("Floor 0/Walls");
        var map = walls.GetComponent<FaceKeyMap>();
        Assert.IsNotNull(map);
        Assert.AreEqual(HideFlags.DontSave, map.hideFlags & HideFlags.DontSave);
        var e = map.entries.First(x => x.key == s.id + "/R");
        Assert.AreEqual(s.id + "/R", map.KeyAt(e.submesh, e.firstTri));
        Assert.AreEqual(s.id + "/R", map.KeyAt(e.submesh, e.firstTri + e.triCount - 1));
        Assert.IsNull(map.KeyAt(99, 0));
        Assert.IsNotNull(Part("Floor 0/Floor").GetComponent<FaceKeyMap>());
        Assert.IsNotNull(Part("Floor 0/Ceiling").GetComponent<FaceKeyMap>());
    }

    static bool HasKeySuffix(MeshData md, string suffix) =>
        md.groups.Any(g => g.key.EndsWith(suffix) && g.triangles.Count > 0);

    [Test] public void FloorUnder_OmittedOverClosedRoomBelow()
    {
        var f0 = Room(b.data.AddFloor(), 0, 4);
        var f1 = Room(b.data.AddFloor(), 0, 4);
        Assert.IsTrue(HasKeySuffix(SlabMeshBuilder.BuildFloor(f0, null, null), "/floor/under"), "floor 0 keeps it");
        var md = SlabMeshBuilder.BuildFloor(f1, null, f0);
        Assert.IsFalse(HasKeySuffix(md, "/floor/under"));
        Assert.IsTrue(HasKeySuffix(md, "/floor"));
        Assert.IsTrue(HasKeySuffix(md, "/floor/edge"));
    }

    [Test] public void FloorUnder_KeptOnOverhang()
    {
        var f0 = Room(b.data.AddFloor(), 0, 4);
        var f1 = Room(b.data.AddFloor(), 10, 14);
        Assert.IsTrue(HasKeySuffix(SlabMeshBuilder.BuildFloor(f1, null, f0), "/floor/under"));
    }

    [Test] public void ChangingDefaultMaterial_RegeneratesAllFloorsGlass()
    {
        r.defaultMaterial = baseMat;
        var f0 = Room(b.data.AddFloor(), 0, 4);
        var f1 = Room(b.data.AddFloor(), 0, 4);
        var f2 = Room(b.data.AddFloor(), 0, 4);
        foreach (var f in new[] { f0, f1, f2 })
            b.data.AddOpening(f, SegFrom(f, Vector2.zero).id,
                new WallOpening { type = OpeningType.Window, center = 2f, width = 1.2f, height = 1.2f, sill = 0.9f });
        r.Regenerate();
        r.defaultMaterial = m1;
        r.Regenerate(0); // partial request (floors 0-1): the material change must still rebuild every floor
        foreach (var i in new[] { 0, 1, 2 })
        {
            var glass = go.transform.Find("Floor " + i + "/Windows").GetChild(0).Find("Glass").GetComponent<MeshRenderer>();
            Assert.IsTrue(glass.sharedMaterial != null, "floor " + i + " glass destroyed");
            var frame = go.transform.Find("Floor " + i + "/Windows").GetChild(0).Find("Frame").GetComponent<MeshRenderer>();
            Assert.AreSame(m1, frame.sharedMaterial, "floor " + i + " re-skinned");
        }
    }
}
