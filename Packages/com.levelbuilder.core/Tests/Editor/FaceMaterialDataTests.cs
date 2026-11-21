using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using LevelBuilder;
using LevelBuilder.Geometry;

public class FaceMaterialDataTests
{
    Material m1, m2;

    [SetUp] public void SetUp()
    {
        var sh = Shader.Find("Hidden/InternalErrorShader");
        m1 = new Material(sh) { name = "M1" };
        m2 = new Material(sh) { name = "M2" };
    }

    [TearDown] public void TearDown()
    {
        Object.DestroyImmediate(m1);
        Object.DestroyImmediate(m2);
    }

    static BuildingData Room(out FloorData f, float w = 4f)
    {
        var b = new BuildingData();
        f = b.AddFloor();
        b.AddSegment(f, new Vector2(0, 0), new Vector2(w, 0));
        b.AddSegment(f, new Vector2(w, 0), new Vector2(w, 4));
        b.AddSegment(f, new Vector2(w, 4), new Vector2(0, 4));
        b.AddSegment(f, new Vector2(0, 4), new Vector2(0, 0));
        return b;
    }

    static Region RegionAt(FloorData f, Vector2 p) =>
        PlanarFaces.Compute(f).regions.First(r => PlanarFaces.PointInPolygon(p, r.polygon));

    [Test] public void Split_CopiesWallMaterials()
    {
        var b = new BuildingData();
        var f = b.AddFloor();
        var seg = b.AddSegment(f, new Vector2(0, 0), new Vector2(6, 0))[0];
        seg.materials.left = m1; seg.materials.right = m2; seg.materials.top = m1; seg.materials.caps = m2;
        b.SplitSegment(f, seg.id, new Vector2(3, 0));
        var second = f.segments.First(s => s != seg);
        Assert.AreSame(m1, second.materials.left);
        Assert.AreSame(m2, second.materials.right);
        Assert.AreSame(m1, second.materials.top);
        Assert.AreSame(m2, second.materials.caps);
        Assert.AreNotSame(seg.materials, second.materials);
    }

    [Test] public void Reverse_SwapsLeftRight()
    {
        // Two collinear walls pointing at each other: merging reverses the second one.
        var b = new BuildingData();
        var f = b.AddFloor();
        var a = b.AddSegment(f, new Vector2(0, 0), new Vector2(2, 0))[0];
        var c = b.AddSegment(f, new Vector2(4, 0), new Vector2(2, 0))[0];
        var t = b.AddSegment(f, new Vector2(2, 0), new Vector2(2, 2))[0];
        a.materials.left = m1;
        c.materials.right = m1; // same physical side as a.left once c is reversed
        c.materials.left = m2;
        b.RemoveWall(f, t.id);
        Assert.AreEqual(1, f.segments.Count);
        Assert.AreSame(m1, f.segments[0].materials.left);
        Assert.IsNull(f.segments[0].materials.right);
        // Direct check on the reversed segment's swap.
        Assert.AreSame(m1, c.materials.left);
        Assert.AreSame(m2, c.materials.right);
    }

    [Test] public void Join_KeepsSurvivorMaterials()
    {
        var b = new BuildingData();
        var f = b.AddFloor();
        var seg = b.AddSegment(f, new Vector2(0, 0), new Vector2(6, 0))[0];
        b.SplitSegment(f, seg.id, new Vector2(3, 0));
        var second = f.segments.First(s => s != seg);
        seg.materials.left = m1;
        second.materials.left = m2;
        Assert.IsTrue(b.JoinSplit(f, seg.id, second.id));
        Assert.AreEqual(1, f.segments.Count);
        Assert.AreSame(m1, f.segments[0].materials.left);
    }

    [Test] public void NewFloor_CopiesExteriorWallMaterials()
    {
        var b = Room(out var f);
        foreach (var s in f.segments) { s.materials.left = m1; s.materials.right = m2; s.materials.top = m1; }
        var up = b.AddFloor();
        Assert.AreEqual(4, up.segments.Count);
        foreach (var s in up.segments)
        {
            Assert.AreSame(m1, s.materials.left);
            Assert.AreSame(m2, s.materials.right);
            Assert.AreSame(m1, s.materials.top);
            Assert.IsFalse(f.segments.Any(o => ReferenceEquals(o.materials, s.materials)));
        }
    }

    [Test] public void RegionPaint_FollowsRoomWhenWallsMove()
    {
        var b = Room(out var f);
        var r = RegionAt(f, new Vector2(2, 2));
        FaceMaterialRules.Set(b, f, "region:" + r.key + "/floor", m1);
        Assert.AreEqual(1, f.regionPaints.Count);
        var corner = f.nodes.First(n => n.position == new Vector2(4, 4));
        b.MoveNode(f, corner.id, new Vector2(4.6f, 4.3f));
        b.ResolveNode(f, corner.id);
        var r2 = RegionAt(f, new Vector2(2, 2));
        Assert.AreSame(m1, FaceMaterialRules.Get(b, f, "region:" + r2.key + "/floor"));
        Assert.AreSame(m1, FaceMaterialRules.Get(b, f, "region:" + r2.key + "/floor/under"));
        Assert.IsNull(FaceMaterialRules.Get(b, f, "region:" + r2.key + "/ceiling"));
    }

    [Test] public void RegionPaint_SplitRoom_SeedSideKeepsIt()
    {
        var b = Room(out var f);
        var r = RegionAt(f, new Vector2(2, 2));
        FaceMaterialRules.Set(b, f, "region:" + r.key + "/ceiling", m1, new Vector2(1, 1));
        b.AddSegment(f, new Vector2(2, 0), new Vector2(2, 4));
        var left = RegionAt(f, new Vector2(1, 2));
        var right = RegionAt(f, new Vector2(3, 2));
        Assert.AreNotEqual(left.key, right.key);
        Assert.AreSame(m1, FaceMaterialRules.Get(b, f, "region:" + left.key + "/ceiling"));
        Assert.AreSame(m1, FaceMaterialRules.Get(b, f, "region:" + left.key + "/roof"));
        Assert.IsNull(FaceMaterialRules.Get(b, f, "region:" + right.key + "/ceiling"));
        // Painting the other side adds a second paint; repainting the left side updates the first.
        FaceMaterialRules.Set(b, f, "region:" + right.key + "/floor", m2);
        FaceMaterialRules.Set(b, f, "region:" + left.key + "/floor/edge", m2);
        Assert.AreEqual(2, f.regionPaints.Count);
        Assert.AreSame(m2, FaceMaterialRules.Get(b, f, "region:" + left.key + "/roof/edge"));
        Assert.AreSame(m1, FaceMaterialRules.Get(b, f, "region:" + left.key + "/ceiling"));
    }

    [Test] public void FaceKeys_MapToFields()
    {
        var b = Room(out var f);
        var seg = f.segments[0];
        var o = b.AddOpening(f, seg.id, new WallOpening { type = OpeningType.Window, center = 2f, width = 1f, height = 1f, sill = 1f });
        var up = b.AddFloor();
        var st = b.AddStair(0, new[] { new StairPoint { position = new Vector2(1, 1) }, new StairPoint { position = new Vector2(1, 3) } });

        FaceMaterialRules.Set(b, f, seg.id + "/L", m1);
        FaceMaterialRules.Set(b, f, seg.id + "/R", m2);
        FaceMaterialRules.Set(b, f, seg.id + "/start", m1);
        FaceMaterialRules.Set(b, f, seg.id + "/top", m2);
        Assert.AreSame(m1, seg.materials.left);
        Assert.AreSame(m2, seg.materials.right);
        Assert.AreSame(m1, seg.materials.caps);
        Assert.AreSame(m1, FaceMaterialRules.Get(b, f, seg.id + "/end"));
        Assert.AreSame(m2, seg.materials.top);

        // Node faces: top material of the first incident segment that has one.
        var node = f.GetNode(seg.startNodeId);
        Assert.AreSame(m2, FaceMaterialRules.Get(b, f, "node:" + node.id + "/top"));
        Assert.AreSame(m2, FaceMaterialRules.Get(b, f, "node:" + node.id + "/side"));
        var far = f.nodes.First(n => f.segments.Where(s => s.startNodeId == n.id || s.endNodeId == n.id).All(s => s != seg));
        Assert.IsNull(FaceMaterialRules.Get(b, f, "node:" + far.id + "/top"));

        FaceMaterialRules.Set(b, f, o.id + "/sill", m1);
        Assert.AreSame(m1, o.materials.jamb);
        Assert.AreSame(m1, FaceMaterialRules.Get(b, f, o.id + "/threshold"));
        Assert.AreSame(m1, FaceMaterialRules.Get(b, f, o.id + "/jamb"));

        string[] parts = { "tread", "riser", "side", "landing", "rail" };
        for (int i = 0; i < parts.Length; i++)
            FaceMaterialRules.Set(b, f, "stair:" + st.id + "/" + parts[i], i % 2 == 0 ? m1 : m2);
        Assert.AreSame(m1, st.materials.tread);
        Assert.AreSame(m2, st.materials.riser);
        Assert.AreSame(m1, st.materials.side);
        Assert.AreSame(m2, st.materials.landing);
        Assert.AreSame(m1, st.materials.rail);
        Assert.AreSame(m2, FaceMaterialRules.Get(b, up, "stair:" + st.id + "/riser"));

        var r = RegionAt(f, new Vector2(2, 2));
        FaceMaterialRules.Set(b, f, "region:" + r.key + "/floor/edge", m2);
        Assert.AreSame(m2, f.regionPaints[0].edge);
        Assert.IsNull(f.regionPaints[0].floor);

        Assert.IsNull(FaceMaterialRules.Get(b, f, "nonexistent/L"));

        CollectionAssert.AreEquivalent(new[] { seg.id + "/L", seg.id + "/R", seg.id + "/start", seg.id + "/end", seg.id + "/top" },
            FaceMaterialRules.PieceKeys(b, f, seg.id + "/R").ToArray());
        CollectionAssert.AreEquivalent(new[] { o.id + "/jamb", o.id + "/sill", o.id + "/threshold" },
            FaceMaterialRules.PieceKeys(b, f, o.id + "/jamb").ToArray());
        CollectionAssert.AreEquivalent(parts.Select(p => "stair:" + st.id + "/" + p).ToArray(),
            FaceMaterialRules.PieceKeys(b, f, "stair:" + st.id + "/rail").ToArray());
        string rk = "region:" + r.key;
        CollectionAssert.AreEquivalent(new[] { rk + "/floor", rk + "/floor/under", rk + "/floor/edge", rk + "/ceiling", rk + "/roof", rk + "/roof/edge" },
            FaceMaterialRules.PieceKeys(b, f, rk + "/floor/edge").ToArray());
    }

    [Test] public void Library_SizeOfUnknownIsOne()
    {
        var lib = ScriptableObject.CreateInstance<MaterialLibrary>();
        try
        {
            Assert.AreEqual(Vector2.one, lib.SizeOf(m1));
            Assert.AreEqual(Vector2.one, lib.SizeOf(null));
            Assert.AreEqual("Assets/LevelBuilder/Materiales", lib.saveFolder);
            var e = lib.Add(m1);
            Assert.AreEqual("M1", e.name);
            e.size = new Vector2(2f, 3f);
            Assert.AreSame(e, lib.Add(m1, "Otro"));
            Assert.AreEqual(1, lib.entries.Count);
            Assert.AreSame(e, lib.Find(m1));
            Assert.IsNull(lib.Find(m2));
            Assert.AreEqual(new Vector2(2f, 3f), lib.SizeOf(m1));
            Assert.AreEqual(Vector2.one, lib.SizeOf(m2));
        }
        finally { Object.DestroyImmediate(lib); }
    }

    [Test] public void JsonRoundTrip_KeepsMaterialReferences()
    {
        // EditorJsonUtility writes object references as persistent asset ids, so the materials must be assets.
        const string dir = "Assets/__FaceMaterialDataTests";
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "__FaceMaterialDataTests");
        var go = new GameObject("B");
        var go2 = new GameObject("B2");
        try
        {
            var sh = Shader.Find("Hidden/InternalErrorShader");
            var a1 = new Material(sh); AssetDatabase.CreateAsset(a1, dir + "/A1.mat");
            var a2 = new Material(sh); AssetDatabase.CreateAsset(a2, dir + "/A2.mat");
            var lib = ScriptableObject.CreateInstance<MaterialLibrary>(); AssetDatabase.CreateAsset(lib, dir + "/Lib.asset");

            var bld = go.AddComponent<Building>();
            bld.library = lib;
            var f = bld.data.floors.Count > 0 ? bld.data.floors[0] : bld.data.AddFloor();
            var seg = bld.data.AddSegment(f, new Vector2(0, 0), new Vector2(3, 0))[0];
            seg.materials.left = a1;
            var o = bld.data.AddOpening(f, seg.id, new WallOpening { type = OpeningType.Empty, center = 1.5f, width = 1f, height = 2f });
            o.materials.jamb = a2;
            f.regionPaints.Add(new RegionPaint { seed = new Vector2(1, 2), floor = a1, ceiling = a2, edge = a1 });
            bld.data.AddFloor();
            var st = bld.data.AddStair(0, new[] { new StairPoint { position = new Vector2(1, 1) }, new StairPoint { position = new Vector2(1, 3) } });
            st.materials.rail = a2;

            string json = EditorJsonUtility.ToJson(bld);
            var copy = go2.AddComponent<Building>();
            EditorJsonUtility.FromJsonOverwrite(json, copy);

            Assert.AreNotSame(bld.data, copy.data);
            var cf = copy.data.floors[0];
            Assert.AreEqual(a1, cf.segments[0].materials.left);
            Assert.IsNull(cf.segments[0].materials.right);
            Assert.AreEqual(a2, cf.segments[0].openings[0].materials.jamb);
            Assert.AreEqual(new Vector2(1, 2), cf.regionPaints[0].seed);
            Assert.AreEqual(a2, cf.regionPaints[0].ceiling);
            Assert.AreEqual(a1, cf.regionPaints[0].edge);
            Assert.AreEqual(a2, copy.data.stairs[0].materials.rail);
            Assert.AreEqual(lib, copy.library);
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(go2);
            AssetDatabase.DeleteAsset(dir);
        }
    }
}
