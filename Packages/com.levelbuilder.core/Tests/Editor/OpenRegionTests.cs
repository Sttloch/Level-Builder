using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LevelBuilder;
using LevelBuilder.Geometry;

public class OpenRegionTests
{
    static List<WallSegment> Seg(BuildingData b, FloorData f, float ax, float ay, float bx, float by, float h = -1f)
    {
        var list = b.AddSegment(f, new Vector2(ax, ay), new Vector2(bx, by));
        foreach (var s in list) s.heightOverride = h;
        return list;
    }

    static void Square(BuildingData b, FloorData f)
    {
        Seg(b, f, 0, 0, 4, 0); Seg(b, f, 4, 0, 4, 4); Seg(b, f, 4, 4, 0, 4); Seg(b, f, 0, 4, 0, 0);
    }

    // Room (0,0)-(4,4) with full walls; balcony (4,0)-(6,4) closed by a 1 m parapet.
    static BuildingData Balcony(out FloorData f)
    {
        var b = new BuildingData();
        f = b.AddFloor();
        Square(b, f);
        Seg(b, f, 4, 0, 6, 0, 1f); Seg(b, f, 6, 0, 6, 4, 1f); Seg(b, f, 6, 4, 4, 4, 1f);
        return b;
    }

    static Region RegionAt(FloorData f, Vector2 p) =>
        PlanarFaces.Compute(f).regions.Find(r => PlanarFaces.PointInPolygon(p, r.polygon));

    static bool HasGroup(MeshData md, string key) =>
        md.groups.Exists(g => g.key == key && g.triangles.Count > 0);

    [Test] public void ZeroHeightEdges_MakeFloorWithoutWallMesh()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        Seg(b, f, 0, 0, 4, 0); Seg(b, f, 4, 0, 4, 4); Seg(b, f, 4, 4, 0, 4);
        var edge = Seg(b, f, 0, 4, 0, 0, 0f)[0];
        var r = RegionAt(f, new Vector2(2, 2));
        Assert.IsNotNull(r);
        Assert.IsTrue(r.open);
        Assert.IsTrue(HasGroup(SlabMeshBuilder.BuildFloor(f), "region:" + r.key + "/floor"));
        var walls = WallMeshBuilder.Build(f, 0f);
        foreach (var g in walls.groups)
            if (g.key.StartsWith(edge.id)) Assert.AreEqual(0, g.triangles.Count, g.key);
        foreach (var s in f.segments)
            if (s != edge) Assert.IsTrue(HasGroup(walls, s.id + "/L"), s.id);
    }

    [Test] public void OpenRegion_HasNoCeilingOrRoof()
    {
        var b = Balcony(out var f);
        var room = RegionAt(f, new Vector2(2, 2));
        var bal = RegionAt(f, new Vector2(5, 2));
        Assert.IsFalse(room.open);
        Assert.IsTrue(bal.open);
        var ceil = SlabMeshBuilder.BuildCeiling(f, true);
        Assert.IsTrue(HasGroup(ceil, "region:" + room.key + "/ceiling"));
        Assert.IsTrue(HasGroup(ceil, "region:" + room.key + "/roof"));
        Assert.IsFalse(HasGroup(ceil, "region:" + bal.key + "/ceiling"));
        Assert.IsFalse(HasGroup(ceil, "region:" + bal.key + "/roof"));
        Assert.IsFalse(HasGroup(ceil, "region:" + bal.key + "/roof/edge"));
        var floor = SlabMeshBuilder.BuildFloor(f);
        Assert.IsTrue(HasGroup(floor, "region:" + bal.key + "/floor"));
        Assert.IsTrue(HasGroup(floor, "region:" + bal.key + "/floor/under"));
        Assert.IsTrue(HasGroup(floor, "region:" + bal.key + "/floor/edge"));
    }

    [Test] public void NewFloor_DoesNotCopyBalcony()
    {
        var b = Balcony(out var f0);
        var f1 = b.AddFloor();
        Assert.AreEqual(4, f1.segments.Count);
        foreach (var n in f1.nodes) Assert.LessOrEqual(n.position.x, 4.001f);
        foreach (var s in f1.segments) Assert.Less(s.heightOverride, 0f);
        var regions = PlanarFaces.Compute(f1).regions;
        Assert.AreEqual(1, regions.Count);
        Assert.IsFalse(regions[0].open);
        // The room-balcony wall (x = 4) is copied.
        Assert.IsTrue(f1.segments.Exists(s =>
            Mathf.Abs(f1.GetNode(s.startNodeId).position.x - 4f) < 1e-3f &&
            Mathf.Abs(f1.GetNode(s.endNodeId).position.x - 4f) < 1e-3f));
    }

    [Test] public void LoweringLinkedExteriorWall_OnlyThatFloor()
    {
        var b = new BuildingData(); var f0 = b.AddFloor();
        Square(b, f0);
        var f1 = b.AddFloor();
        f1.segments[0].heightOverride = 1f;
        foreach (var s in f0.segments) Assert.Less(s.heightOverride, 0f);
        Assert.IsFalse(PlanarFaces.Compute(f0).regions[0].open);
        Assert.IsTrue(PlanarFaces.Compute(f1).regions[0].open);
    }

    static BuildingData StairBuilding(float wallHeight, out StairData s)
    {
        var b = new BuildingData();
        var f0 = b.AddFloor();
        Seg(b, f0, 0, 0, 8, 0); Seg(b, f0, 8, 0, 8, 6); Seg(b, f0, 8, 6, 0, 6); Seg(b, f0, 0, 6, 0, 0);
        b.AddFloor();
        Seg(b, f0, 3.5f, 0, 3.5f, 6, wallHeight); // wall across the flight
        s = b.AddStair(0, new List<StairPoint> {
            new StairPoint { position = new Vector2(1.5f, 2), kind = StairPointKind.Corner },
            new StairPoint { position = new Vector2(6, 2), kind = StairPointKind.Corner } });
        s.width = 1f;
        return b;
    }

    [Test] public void ZeroHeightWall_NotAStairObstacle()
    {
        var b = StairBuilding(-1f, out var s);
        var v0 = StairValidator.Validate(b, s);
        Assert.IsTrue(v0.issues.Contains(StairIssue.Collision), v0.layout.error + " " + string.Join(",", v0.issues));
        b = StairBuilding(0f, out s);
        var v = StairValidator.Validate(b, s);
        Assert.IsTrue(v.Valid, string.Join(",", v.issues));
    }

    [Test] public void OpeningOnZeroHeightWall_IsInvalid()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        var seg = Seg(b, f, 0, 0, 6, 0, 0f)[0];
        b.AddOpening(f, seg.id, new WallOpening { type = OpeningType.Window, center = 3f, width = 0.5f, height = 0.5f, sill = 1f });
        var r = OpeningLayout.Compute(f)[seg.id][0];
        Assert.IsFalse(r.valid);
        Assert.AreEqual(OpeningIssue.TooTall, r.issue);
    }
}
