using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LevelBuilder;
using LevelBuilder.Geometry;

public class WallOpeningMeshTests
{
    static FaceGroup Group(MeshData md, string key) => md.groups.Find(g => g.key == key);

    static float GroupArea(MeshData md, string key)
    {
        var g = Group(md, key);
        if (g == null) return 0f;
        float area = 0f;
        for (int i = 0; i < g.triangles.Count; i += 3)
        {
            var a = md.vertices[g.triangles[i]];
            area += Vector3.Cross(md.vertices[g.triangles[i + 1]] - a, md.vertices[g.triangles[i + 2]] - a).magnitude * 0.5f;
        }
        return area;
    }

    static WallOpening Window(float center) => new WallOpening
        { type = OpeningType.Window, center = center, width = 1.2f, height = 1.2f, sill = 0.9f };

    static WallOpening Door(float center) => new WallOpening
        { type = OpeningType.Door, center = center, width = 0.9f, height = 2.1f, sill = 0f };

    /// <summary>Isolated wall (0,0)→(6,0), 3 m tall, 0.2 thick: L face at z = +0.1, R at z = −0.1.</summary>
    static (BuildingData b, FloorData f, WallSegment s) Wall6()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        f.height = 3f;
        var s = b.AddSegment(f, new Vector2(0, 0), new Vector2(6, 0))[0];
        return (b, f, s);
    }

    static FloorData Room6x4(BuildingData b)
    {
        var f = b.AddFloor();
        f.height = 3f;
        b.AddSegment(f, new Vector2(0, 0), new Vector2(6, 0));
        b.AddSegment(f, new Vector2(6, 0), new Vector2(6, 4));
        b.AddSegment(f, new Vector2(6, 4), new Vector2(0, 4));
        b.AddSegment(f, new Vector2(0, 4), new Vector2(0, 0));
        return f;
    }

    static void AssertNoCentroidInside(MeshData md, string key, float u0, float u1, float v0, float v1)
    {
        var g = Group(md, key);
        Assert.IsNotNull(g, key);
        for (int i = 0; i < g.triangles.Count; i += 3)
        {
            var c = (md.vertices[g.triangles[i]] + md.vertices[g.triangles[i + 1]] + md.vertices[g.triangles[i + 2]]) / 3f;
            bool inside = c.x > u0 && c.x < u1 && c.y > v0 && c.y < v1;
            Assert.IsFalse(inside, $"{key} triangle {i / 3} centroid {c:F3} inside the opening");
        }
    }

    static bool HasVertex(MeshData md, string key, Vector3 p)
    {
        foreach (int i in Group(md, key).triangles)
            if ((md.vertices[i] - p).magnitude < 1e-4f) return true;
        return false;
    }

    static WallSegment SegmentBetween(FloorData f, Vector2 a, Vector2 c)
    {
        foreach (var s in f.segments)
        {
            var p = f.GetNode(s.startNodeId).position; var q = f.GetNode(s.endNodeId).position;
            if (((p - a).magnitude < 1e-3f && (q - c).magnitude < 1e-3f) || ((p - c).magnitude < 1e-3f && (q - a).magnitude < 1e-3f))
                return s;
        }
        return null;
    }

    [Test] public void Window_CutsHoleInBothSides()
    {
        var (b, f, s) = Wall6();
        b.AddOpening(f, s.id, Window(3f));
        var md = WallMeshBuilder.Build(f, 0f);
        AssertNoCentroidInside(md, s.id + "/L", 2.4f, 3.6f, 0.9f, 2.1f);
        AssertNoCentroidInside(md, s.id + "/R", 2.4f, 3.6f, 0.9f, 2.1f);
        Assert.AreEqual(6f * 3f - 1.44f, GroupArea(md, s.id + "/L"), 1e-3f);
        Assert.AreEqual(6f * 3f - 1.44f, GroupArea(md, s.id + "/R"), 1e-3f);
    }

    [Test] public void Door_IsNotchNotHole()
    {
        var (b, f, s) = Wall6();
        b.AddOpening(f, s.id, Door(3f));
        var md = WallMeshBuilder.Build(f, 0f);
        Assert.IsTrue(HasVertex(md, s.id + "/L", new Vector3(2.55f, 0f, 0.1f)), "notch corner at u0 on the floor");
        Assert.IsTrue(HasVertex(md, s.id + "/L", new Vector3(3.45f, 0f, 0.1f)), "notch corner at u1 on the floor");
        AssertNoCentroidInside(md, s.id + "/L", 2.55f, 3.45f, 0f, 2.1f);
        AssertNoCentroidInside(md, s.id + "/R", 2.55f, 3.45f, 0f, 2.1f);
        Assert.AreEqual(18f - 0.9f * 2.1f, GroupArea(md, s.id + "/L"), 1e-3f);
        Assert.AreEqual(18f - 0.9f * 2.1f, GroupArea(md, s.id + "/R"), 1e-3f);
    }

    [Test] public void Opening_HasJambFacesThroughThickness()
    {
        var (b, f, s) = Wall6();
        var o = b.AddOpening(f, s.id, Window(3f));
        var md = WallMeshBuilder.Build(f, 0f);
        var jamb = Group(md, o.id + "/jamb");
        Assert.IsNotNull(jamb, "jamb");
        Assert.AreEqual((1.2f + 1.2f + 1.2f) * 0.2f, GroupArea(md, o.id + "/jamb"), 1e-3f);
        bool sawPlus = false, sawMinus = false, sawDown = false;
        foreach (int i in jamb.triangles)
        {
            var v = md.vertices[i]; var nrm = md.normals[i];
            if (Mathf.Abs(nrm.x - 1f) < 1e-4f) { sawPlus = true; Assert.AreEqual(2.4f, v.x, 1e-4f, "+d jamb at u0"); }
            else if (Mathf.Abs(nrm.x + 1f) < 1e-4f) { sawMinus = true; Assert.AreEqual(3.6f, v.x, 1e-4f, "-d jamb at u1"); }
            else if (Mathf.Abs(nrm.y + 1f) < 1e-4f) { sawDown = true; Assert.AreEqual(2.1f, v.y, 1e-4f, "lintel at v1"); }
            else Assert.Fail($"unexpected jamb normal {nrm}");
        }
        Assert.IsTrue(sawPlus && sawMinus && sawDown, "two sides and a lintel");

        var sill = Group(md, o.id + "/sill");
        Assert.IsNotNull(sill, "sill");
        Assert.AreEqual(1.2f * 0.2f, GroupArea(md, o.id + "/sill"), 1e-3f);
        foreach (int i in sill.triangles)
        {
            Assert.AreEqual(0.9f, md.vertices[i].y, 1e-4f);
            Assert.AreEqual(1f, md.normals[i].y, 1e-4f);
        }
        WallMeshBuilderTests.AssertWindingMatchesNormals(md);
    }

    [Test] public void Door_HasNoSill()
    {
        var (b, f, s) = Wall6();
        var o = b.AddOpening(f, s.id, Door(3f));
        var md = WallMeshBuilder.Build(f, 0f);
        Assert.IsNull(Group(md, o.id + "/sill"));
        Assert.AreEqual((2.1f + 2.1f + 0.9f) * 0.2f, GroupArea(md, o.id + "/jamb"), 1e-3f);
    }

    [Test] public void Openings_KeepWallWatertight()
    {
        var (b, f, s) = Wall6();
        b.AddOpening(f, s.id, Door(1.5f));
        b.AddOpening(f, s.id, Window(4f));
        var md = WallMeshBuilder.Build(f, 0f);
        WallMeshBuilderTests.AssertWatertight(md, 0f);
        WallMeshBuilderTests.AssertWindingMatchesNormals(md);
    }

    [Test] public void Openings_WatertightInRoomAtBaseY()
    {
        var b = new BuildingData();
        var f = Room6x4(b);
        foreach (var s in f.segments) { b.AddOpening(f, s.id, Door(1f)); b.AddOpening(f, s.id, Window(2.6f)); }
        var md = WallMeshBuilder.Build(f, 2f);
        WallMeshBuilderTests.AssertWatertight(md, 2f);
        WallMeshBuilderTests.AssertWindingMatchesNormals(md);
    }

    [Test] public void InvalidOpening_DoesNotCut()
    {
        var (b, f, s) = Wall6();
        int plain = WallMeshBuilder.Build(f, 0f).vertices.Count;
        var o = b.AddOpening(f, s.id, Door(0.2f));
        Assert.IsFalse(OpeningLayout.Compute(f)[s.id][0].valid);
        var md = WallMeshBuilder.Build(f, 0f);
        Assert.AreEqual(plain, md.vertices.Count);
        Assert.IsNull(Group(md, o.id + "/jamb"));
    }

    [Test] public void Threshold_OnlyWhereNoFloorRegion()
    {
        var b = new BuildingData();
        var f = Room6x4(b);
        var south = SegmentBetween(f, new Vector2(0, 0), new Vector2(6, 0));
        var outer = b.AddOpening(f, south.id, Door(3f));
        var md = WallMeshBuilder.Build(f, 0f);
        var th = Group(md, outer.id + "/threshold");
        Assert.IsNotNull(th, "exterior door threshold");
        Assert.AreEqual(0.9f * 0.1f, GroupArea(md, outer.id + "/threshold"), 1e-3f);
        foreach (int i in th.triangles)
        {
            Assert.LessOrEqual(md.vertices[i].z, 1e-4f, "threshold must stay on the outside (south) half");
            Assert.AreEqual(0f, md.vertices[i].y, 1e-4f);
            Assert.AreEqual(1f, md.normals[i].y, 1e-4f);
        }

        // Interior wall splitting the room: both floors reach the axis, so no threshold.
        b.RemoveOpening(f, outer.id);
        b.AddSegment(f, new Vector2(3, 0), new Vector2(3, 4));
        var mid = SegmentBetween(f, new Vector2(3, 0), new Vector2(3, 4));
        var inner = b.AddOpening(f, mid.id, Door(2f));
        Assert.IsTrue(OpeningLayout.Compute(f)[mid.id][0].valid);
        md = WallMeshBuilder.Build(f, 0f);
        Assert.IsNotNull(Group(md, inner.id + "/jamb"));
        Assert.IsNull(Group(md, inner.id + "/threshold"));
    }

    [Test] public void Opening_UvsInMetres()
    {
        var (b, f, s) = Wall6();
        b.AddOpening(f, s.id, Window(3f));
        var md = WallMeshBuilder.Build(f, 0f);
        var corner = new Vector3(2.4f, 0.9f, -0.1f);
        bool found = false;
        foreach (int i in Group(md, s.id + "/R").triangles)
        {
            if ((md.vertices[i] - corner).magnitude > 1e-4f) continue;
            found = true;
            Assert.AreEqual(2.4f, md.uvs[i].x, 1e-4f);
            Assert.AreEqual(0.9f, md.uvs[i].y, 1e-4f);
        }
        Assert.IsTrue(found, "hole corner vertex in /R");
    }
}
