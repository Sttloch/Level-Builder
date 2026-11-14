using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LevelBuilder;
using LevelBuilder.Geometry;

public class SlabMeshBuilderTests
{
    static void AddSquare(BuildingData b, FloorData f, float x0, float y0, float s)
    {
        b.AddSegment(f, new Vector2(x0, y0), new Vector2(x0 + s, y0));
        b.AddSegment(f, new Vector2(x0 + s, y0), new Vector2(x0 + s, y0 + s));
        b.AddSegment(f, new Vector2(x0 + s, y0 + s), new Vector2(x0, y0 + s));
        b.AddSegment(f, new Vector2(x0, y0 + s), new Vector2(x0, y0));
    }

    /// <summary>Area of triangles (in group key, or all groups when null) with normal == dir and all vertices at height y.</summary>
    static float FaceArea(MeshData md, string key, Vector3 dir, float y)
    {
        float area = 0f;
        foreach (var g in md.groups)
        {
            if (key != null && g.key != key) continue;
            for (int i = 0; i < g.triangles.Count; i += 3)
            {
                int a = g.triangles[i], b = g.triangles[i + 1], c = g.triangles[i + 2];
                if (Vector3.Dot(md.normals[a], dir) < 0.99f) continue;
                if (Mathf.Abs(md.vertices[a].y - y) > 1e-4f || Mathf.Abs(md.vertices[b].y - y) > 1e-4f || Mathf.Abs(md.vertices[c].y - y) > 1e-4f) continue;
                area += Vector3.Cross(md.vertices[b] - md.vertices[a], md.vertices[c] - md.vertices[a]).magnitude * 0.5f;
            }
        }
        return area;
    }

    static void AssertWinding(MeshData md)
    {
        Assert.Greater(md.vertices.Count, 0);
        foreach (var g in md.groups)
            for (int i = 0; i < g.triangles.Count; i += 3)
            {
                int a = g.triangles[i], b = g.triangles[i + 1], c = g.triangles[i + 2];
                var n = Vector3.Cross(md.vertices[b] - md.vertices[a], md.vertices[c] - md.vertices[a]).normalized;
                Assert.Greater(Vector3.Dot(n, md.normals[a]), 0.99f, g.key);
            }
    }

    [Test] public void Square_FloorTopFaceAtElevation_AreaMatches()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        AddSquare(b, f, 0, 0, 4);
        var md = SlabMeshBuilder.BuildFloor(f);
        Assert.AreEqual(16f, FaceArea(md, null, Vector3.up, 0f), 1e-3f);
        Assert.AreEqual(16f, FaceArea(md, null, Vector3.down, -0.2f), 1e-3f);
        var key = "region:" + PlanarFaces.Compute(f).regions[0].key + "/floor";
        Assert.IsNotNull(md.groups.Find(g => g.key == key));
        AssertWinding(md);
        WallMeshBuilderTests.AssertWatertight(md, -100f);
    }

    [Test] public void OpenWalls_NoFloor_NoThrow()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
        b.AddSegment(f, new Vector2(4, 0), new Vector2(4, 3));
        MeshData floor = null, ceil = null;
        Assert.DoesNotThrow(() => { floor = SlabMeshBuilder.BuildFloor(f); ceil = SlabMeshBuilder.BuildCeiling(f, true); });
        Assert.AreEqual(0, floor.vertices.Count);
        Assert.AreEqual(0, ceil.vertices.Count);
    }

    [Test] public void Ceiling_FacesDown_AtUnderside()
    {
        var b = new BuildingData(); var f = b.AddFloor(); b.AddFloor();
        AddSquare(b, f, 0, 0, 4);
        var md = SlabMeshBuilder.BuildCeiling(f, false);
        Assert.AreEqual(16f, FaceArea(md, null, Vector3.down, 3f - 0.2f), 1e-3f);
        Assert.AreEqual(0f, FaceArea(md, null, Vector3.up, 3f), 1e-6f);
        Assert.IsNotNull(md.groups.Find(g => g.key.EndsWith("/ceiling")));
        AssertWinding(md);
    }

    [Test] public void TopFloor_HasRoofSlab()
    {
        var b = new BuildingData(); b.AddFloor(); var f = b.AddFloor();
        AddSquare(b, f, 0, 0, 4);
        var md = SlabMeshBuilder.BuildCeiling(f, true);
        float e = f.elevation;
        Assert.AreEqual(16f, FaceArea(md, null, Vector3.up, e + 3f + 0.2f), 1e-3f);
        Assert.AreEqual(16f, FaceArea(md, null, Vector3.down, e + 3f), 1e-3f);
        AssertWinding(md);
        WallMeshBuilderTests.AssertWatertight(md, -100f);
    }

    [Test] public void PillarInRoom_FloorHasHole()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        AddSquare(b, f, 0, 0, 4);
        AddSquare(b, f, 1.5f, 1.5f, 1);
        var r = PlanarFaces.Compute(f);
        Region big = null;
        foreach (var reg in r.regions) if (reg.holes.Count == 1) big = reg;
        Assert.IsNotNull(big);
        var md = SlabMeshBuilder.BuildFloor(f);
        Assert.AreEqual(15f, FaceArea(md, "region:" + big.key + "/floor", Vector3.up, 0f), 1e-3f);
        AssertWinding(md);
    }

    static HashSet<string> Keys(MeshData md)
    {
        var set = new HashSet<string>();
        foreach (var g in md.groups) set.Add(g.key);
        return set;
    }

    [Test] public void FaceKeys_SeparateSurfaces()
    {
        var b = new BuildingData(); var f = b.AddFloor(); b.AddFloor();
        AddSquare(b, f, 0, 0, 4);
        string k = "region:" + PlanarFaces.Compute(f).regions[0].key;
        var floor = SlabMeshBuilder.BuildFloor(f);
        CollectionAssert.AreEquivalent(new[] { k + "/floor", k + "/floor/under", k + "/floor/edge" }, Keys(floor));
        Assert.AreEqual(16f, FaceArea(floor, k + "/floor", Vector3.up, 0f), 1e-3f);
        Assert.AreEqual(16f, FaceArea(floor, k + "/floor/under", Vector3.down, -0.2f), 1e-3f);
        CollectionAssert.AreEquivalent(new[] { k + "/ceiling" }, Keys(SlabMeshBuilder.BuildCeiling(f, false)));
        var roof = SlabMeshBuilder.BuildCeiling(f, true);
        CollectionAssert.AreEquivalent(new[] { k + "/ceiling", k + "/roof", k + "/roof/edge" }, Keys(roof));
        Assert.AreEqual(16f, FaceArea(roof, k + "/ceiling", Vector3.down, 3f), 1e-3f);
        Assert.AreEqual(16f, FaceArea(roof, k + "/roof", Vector3.up, 3.2f), 1e-3f);
    }
    static List<IList<Vector2>> Hole1x2() => new List<IList<Vector2>>
    {
        new List<Vector2> { new Vector2(2, 2), new Vector2(3, 2), new Vector2(3, 4), new Vector2(2, 4) },
    };

    static bool OnHoleRim(Vector3 v) =>
        (Mathf.Abs(v.x - 2f) < 1e-4f || Mathf.Abs(v.x - 3f) < 1e-4f) && v.z > 2f - 1e-4f && v.z < 4f + 1e-4f ||
        (Mathf.Abs(v.z - 2f) < 1e-4f || Mathf.Abs(v.z - 4f) < 1e-4f) && v.x > 2f - 1e-4f && v.x < 3f + 1e-4f;

    [Test] public void FloorHole_CutsTopUnderAndEdges()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        AddSquare(b, f, 0, 0, 6);
        string k = "region:" + PlanarFaces.Compute(f).regions[0].key + "/floor";
        var md = SlabMeshBuilder.BuildFloor(f, Hole1x2());
        Assert.AreEqual(34f, FaceArea(md, k, Vector3.up, 0f), 1e-3f);
        Assert.AreEqual(34f, FaceArea(md, k + "/under", Vector3.down, -0.2f), 1e-3f);

        // Edge faces around the hole: 4 quads (8 triangles), total area = perimeter 6 x thickness, facing into the hole.
        var edge = md.groups.Find(g => g.key == k + "/edge");
        Assert.IsNotNull(edge);
        float rimArea = 0f; int rimTris = 0;
        var center = new Vector3(2.5f, 0f, 3f);
        for (int i = 0; i < edge.triangles.Count; i += 3)
        {
            var a = md.vertices[edge.triangles[i]]; var bb = md.vertices[edge.triangles[i + 1]]; var c = md.vertices[edge.triangles[i + 2]];
            if (!OnHoleRim(a) || !OnHoleRim(bb) || !OnHoleRim(c)) continue;
            rimTris++;
            rimArea += Vector3.Cross(bb - a, c - a).magnitude * 0.5f;
            var toCenter = center - a; toCenter.y = 0f;
            Assert.Greater(Vector3.Dot(md.normals[edge.triangles[i]], toCenter), 0f);
        }
        Assert.AreEqual(8, rimTris);
        Assert.AreEqual(6f * 0.2f, rimArea, 1e-3f);
        AssertWinding(md);
    }

    [Test] public void CeilingHole_DoesNotCutRoof()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        AddSquare(b, f, 0, 0, 6);
        string k = "region:" + PlanarFaces.Compute(f).regions[0].key;
        var md = SlabMeshBuilder.BuildCeiling(f, true, Hole1x2());
        Assert.AreEqual(34f, FaceArea(md, k + "/ceiling", Vector3.down, 3f), 1e-3f);
        Assert.AreEqual(36f, FaceArea(md, k + "/roof", Vector3.up, 3.2f), 1e-3f);
        var roofEdge = md.groups.Find(g => g.key == k + "/roof/edge");
        foreach (int idx in roofEdge.triangles) Assert.IsFalse(OnHoleRim(md.vertices[idx]), "roof edge around the hole");

        // Non-top floor: the ceiling still gets the hole.
        var b2 = new BuildingData(); var f2 = b2.AddFloor(); b2.AddFloor();
        AddSquare(b2, f2, 0, 0, 6);
        string k2 = "region:" + PlanarFaces.Compute(f2).regions[0].key;
        Assert.AreEqual(34f, FaceArea(SlabMeshBuilder.BuildCeiling(f2, false, Hole1x2()), k2 + "/ceiling", Vector3.down, 2.8f), 1e-3f);
    }
}
