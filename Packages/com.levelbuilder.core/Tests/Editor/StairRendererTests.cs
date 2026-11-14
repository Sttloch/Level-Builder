using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using LevelBuilder;
using LevelBuilder.Geometry;

// Floors 0 and 1 share a 10x6 exterior rectangle (0,0)-(10,6). Floor height 3 -> 17 risers, 16 treads,
// run 4.48 m along +x from (1.5, 2): a free, valid stair.
public class StairRendererTests
{
    GameObject go;
    BuildingRenderer r;
    Building b;

    [SetUp] public void SetUp()
    {
        go = new GameObject("TestBuilding");
        r = go.AddComponent<BuildingRenderer>();
        b = go.GetComponent<Building>();
    }

    [TearDown] public void TearDown()
    {
        if (go != null) Object.DestroyImmediate(go);
    }

    static StairPoint P(float x, float y) => new StairPoint { position = new Vector2(x, y), kind = StairPointKind.Corner };

    void TwoFloors()
    {
        var f0 = b.data.AddFloor();
        b.data.AddSegment(f0, new Vector2(0, 0), new Vector2(10, 0));
        b.data.AddSegment(f0, new Vector2(10, 0), new Vector2(10, 6));
        b.data.AddSegment(f0, new Vector2(10, 6), new Vector2(0, 6));
        b.data.AddSegment(f0, new Vector2(0, 6), new Vector2(0, 0));
        b.data.AddFloor(); // copies the exterior
    }

    StairData AddStair() => b.data.AddStair(0, new List<StairPoint> { P(1.5f, 2), P(6, 2) });

    Transform StairObj(string id, string child = null) =>
        go.transform.Find("Floor 0/Stairs/" + id + (child != null ? "/" + child : ""));

    static float PolygonArea(IList<Vector2> p)
    {
        float a = 0f;
        for (int i = 0; i < p.Count; i++)
        {
            var u = p[i]; var v = p[(i + 1) % p.Count];
            a += u.x * v.y - v.x * u.y;
        }
        return Mathf.Abs(a) * 0.5f;
    }

    /// <summary>Area of the triangles of a generated part facing dir with all vertices at height y.</summary>
    float FaceArea(string path, Vector3 dir, float y)
    {
        var mesh = go.transform.Find(path).GetComponent<MeshFilter>().sharedMesh;
        var v = mesh.vertices;
        var t = mesh.triangles;
        float area = 0f;
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 a = v[t[i]], c1 = v[t[i + 1]], c2 = v[t[i + 2]];
            if (Mathf.Abs(a.y - y) > 1e-3f || Mathf.Abs(c1.y - y) > 1e-3f || Mathf.Abs(c2.y - y) > 1e-3f) continue;
            var n = Vector3.Cross(c1 - a, c2 - a);
            if (n.sqrMagnitude < 1e-12f || Vector3.Dot(n.normalized, dir) < 0.99f) continue;
            area += n.magnitude * 0.5f;
        }
        return area;
    }

    static int DontSaveMeshCount() =>
        Resources.FindObjectsOfTypeAll<Mesh>().Count(m => m != null && (m.hideFlags & HideFlags.DontSave) == HideFlags.DontSave);

    [Test] public void Stair_CreatesStepsRampAndRails()
    {
        TwoFloors();
        var s = AddStair();
        r.Regenerate();

        Assert.IsTrue(r.StairValidations.ContainsKey(s.id));
        var v = r.StairValidations[s.id];
        Assert.IsTrue(v.Valid, string.Join(",", v.issues));

        var steps = StairObj(s.id, "Steps");
        Assert.IsNotNull(steps);
        Assert.IsNotNull(steps.GetComponent<MeshRenderer>());
        Assert.Greater(steps.GetComponent<MeshFilter>().sharedMesh.vertexCount, 0);
        Assert.IsNull(steps.GetComponent<MeshCollider>());

        var ramp = StairObj(s.id, "Ramp");
        Assert.IsNotNull(ramp);
        var rc = ramp.GetComponent<MeshCollider>();
        Assert.IsNotNull(rc);
        Assert.IsTrue(rc.sharedMesh != null && rc.sharedMesh.vertexCount > 0);
        Assert.IsNull(ramp.GetComponent<MeshRenderer>());

        var rails = StairObj(s.id, "Rails");
        Assert.IsNotNull(rails);
        Assert.IsNotNull(rails.GetComponent<MeshRenderer>());
        Assert.IsNotNull(rails.GetComponent<MeshCollider>());

        foreach (var t in StairObj(s.id).GetComponentsInChildren<Transform>(true))
            Assert.AreEqual(HideFlags.DontSave, t.gameObject.hideFlags & HideFlags.DontSave);
    }

    [Test] public void RampOff_StepsGetCollider()
    {
        TwoFloors();
        var s = AddStair();
        s.collisionRamp = false;
        s.railLeft = s.railRight = false;
        r.Regenerate();

        var steps = StairObj(s.id, "Steps");
        Assert.IsNotNull(steps);
        var mc = steps.GetComponent<MeshCollider>();
        Assert.IsNotNull(mc);
        Assert.IsTrue(mc.sharedMesh != null && mc.sharedMesh.vertexCount > 0);
        Assert.IsNull(StairObj(s.id, "Ramp"));
        Assert.IsNull(StairObj(s.id, "Rails"));
    }

    [Test] public void Stair_CutsUpperFloorAndLowerCeiling_NotRoof()
    {
        TwoFloors();
        r.Regenerate();
        float floorTop = b.data.floors[1].elevation;
        float ceilingY = b.data.floors[0].elevation + b.data.floors[0].height - b.data.floors[0].slabThickness;
        float roofY = b.data.floors[1].elevation + b.data.floors[1].height + b.data.floors[1].slabThickness;
        float floorBefore = FaceArea("Floor 1/Floor", Vector3.up, floorTop);
        float ceilingBefore = FaceArea("Floor 0/Ceiling", Vector3.down, ceilingY);
        float roofBefore = FaceArea("Floor 1/Ceiling", Vector3.up, roofY);
        Assert.AreEqual(60f, floorBefore, 1e-2f);
        Assert.AreEqual(60f, ceilingBefore, 1e-2f);
        Assert.AreEqual(60f, roofBefore, 1e-2f);

        var s = AddStair();
        r.Regenerate();
        var hole = r.StairValidations[s.id].layout.hole;
        Assert.IsNotNull(hole);
        float holeArea = PolygonArea(hole);
        Assert.Greater(holeArea, 1f);

        Assert.AreEqual(floorBefore - holeArea, FaceArea("Floor 1/Floor", Vector3.up, floorTop), 1e-2f);
        Assert.AreEqual(ceilingBefore - holeArea, FaceArea("Floor 0/Ceiling", Vector3.down, ceilingY), 1e-2f);
        Assert.AreEqual(roofBefore, FaceArea("Floor 1/Ceiling", Vector3.up, roofY), 1e-2f);
    }

    [Test] public void MovingWallIntoStair_RevalidatesWithoutThrowing()
    {
        TwoFloors();
        var f0 = b.data.floors[0];
        b.data.AddSegment(f0, new Vector2(3.5f, 4.5f), new Vector2(3.5f, 6f)); // stub off the north wall
        var s = AddStair();
        r.Regenerate();
        Assert.IsTrue(r.StairValidations[s.id].Valid, string.Join(",", r.StairValidations[s.id].issues));

        var free = f0.nodes.First(n => (n.position - new Vector2(3.5f, 4.5f)).magnitude < 0.01f);
        b.data.MoveNode(f0, free.id, new Vector2(3.5f, 1f));
        Assert.DoesNotThrow(() => r.Regenerate());

        CollectionAssert.Contains(r.StairValidations[s.id].issues, StairIssue.Collision);
        var steps = StairObj(s.id, "Steps");
        Assert.IsNotNull(steps);
        Assert.Greater(steps.GetComponent<MeshFilter>().sharedMesh.vertexCount, 0);
        LogAssert.NoUnexpectedReceived();
    }

    [Test] public void ChangingFloorHeight_RecomputesSteps()
    {
        TwoFloors();
        var s = AddStair();
        r.Regenerate();
        float holeBefore = PolygonArea(r.StairValidations[s.id].layout.hole);

        b.data.SetFloorHeight(0, 3.6f);
        r.Regenerate();

        var l = r.StairValidations[s.id].layout;
        Assert.IsTrue(l.ok, l.error);
        Assert.AreEqual(20, l.risers);
        Assert.AreEqual(19, l.pieces.Count(p => p.kind == StairPieceKind.Tread));
        var steps = StairObj(s.id, "Steps").GetComponent<MeshFilter>().sharedMesh;
        Assert.AreEqual(3.6f, steps.bounds.max.y, 1e-3f);

        Assert.IsNotNull(l.hole);
        float holeAfter = PolygonArea(l.hole);
        Assert.AreNotEqual(holeBefore, holeAfter, "hole not recomputed");
        float floorTop = b.data.floors[1].elevation;
        Assert.AreEqual(3.6f, floorTop, 1e-4f);
        Assert.AreEqual(60f - holeAfter, FaceArea("Floor 1/Floor", Vector3.up, floorTop), 1e-2f);
    }

    [Test] public void RemoveTopFloor_RemovesStairObjects_UndoRestores()
    {
        TwoFloors();
        var s = AddStair();
        string id = s.id;
        r.Regenerate();
        Assert.IsNotNull(StairObj(id));

        Undo.ClearAll();
        Undo.IncrementCurrentGroup();
        Undo.RecordObject(b, "Eliminar planta");
        Assert.IsTrue(b.data.RemoveTopFloor());
        r.Regenerate();
        Assert.IsNull(StairObj(id));
        Assert.IsNull(go.transform.Find("Floor 1"));
        Assert.IsFalse(r.StairValidations.ContainsKey(id));

        Undo.PerformUndo(); // data may be a new instance
        Assert.AreEqual(2, b.data.floors.Count);
        Assert.IsNotNull(b.data.FindStair(id));
        r.Regenerate();
        Assert.IsNotNull(StairObj(id, "Steps"));
        Assert.IsTrue(r.StairValidations.ContainsKey(id));
        Undo.ClearAll();
    }

    [Test] public void Regenerate_NoLeakedMeshes()
    {
        TwoFloors();
        AddStair();
        r.Regenerate();
        int before = DontSaveMeshCount();
        r.Regenerate();
        r.Regenerate(0);
        r.Regenerate();
        Assert.AreEqual(before, DontSaveMeshCount());
    }

    [Test] public void DeletedStair_DropsValidationAndObjects()
    {
        TwoFloors();
        var s = AddStair();
        r.Regenerate();
        Assert.IsTrue(b.data.RemoveStair(s.id));
        r.Regenerate(0);
        Assert.IsFalse(r.StairValidations.ContainsKey(s.id));
        Assert.IsNull(StairObj(s.id));
        Assert.AreEqual(60f, FaceArea("Floor 1/Floor", Vector3.up, b.data.floors[1].elevation), 1e-2f);
    }

    // ---------------------------------------------------------------- final review fixes

    [Test] public void RailColliders_AreThinBoxesOutsideTheFootprint()
    {
        TwoFloors();
        var s = AddStair(); // default width 1.2 along +x at y = 2
        Assert.AreEqual(1.2f, s.width, 1e-5f);
        r.Regenerate();
        var rails = StairObj(s.id, "Rails");
        var mf = rails.GetComponent<MeshFilter>();
        var mc = rails.GetComponent<MeshCollider>();
        Assert.IsNotNull(mc);
        Assert.IsNotNull(mc.sharedMesh);
        Assert.AreNotSame(mf.sharedMesh, mc.sharedMesh, "collider is not the visual rail mesh");
        Assert.AreEqual(HideFlags.DontSave, mc.sharedMesh.hideFlags & HideFlags.DontSave);
        var l = r.StairValidations[s.id].layout;
        float H = l.risers * l.riser;
        bool left = false, right = false;
        foreach (var v in mc.sharedMesh.vertices)
        {
            float off = Mathf.Abs(v.z - 2f);
            Assert.GreaterOrEqual(off, 0.6f - 1e-3f, "inside the footprint: " + v);
            Assert.LessOrEqual(off, 0.63f + 1e-3f, "too thick: " + v);
            Assert.GreaterOrEqual(v.x, 1.5f - 1e-3f);
            Assert.LessOrEqual(v.x, 5.98f + 1e-3f);
            Assert.LessOrEqual(v.y, H + 0.9f + 1e-3f);
            if (v.z > 2f) left = true; else right = true;
        }
        Assert.IsTrue(left && right, "one collider per side");
        // Only the left rail: every collider vertex on the left (+z = plan +y is the left of +x).
        s.railRight = false;
        r.Regenerate();
        foreach (var v in StairObj(s.id, "Rails").GetComponent<MeshCollider>().sharedMesh.vertices)
            Assert.Greater(v.z, 2f);
    }

    [Test] public void RampOn_StepsSidesCollider_HasOnlySideFaces()
    {
        TwoFloors();
        var s = AddStair();
        r.Regenerate();
        var sides = StairObj(s.id, "StepsSides");
        Assert.IsNotNull(sides);
        Assert.AreEqual(HideFlags.DontSave, sides.gameObject.hideFlags & HideFlags.DontSave);
        Assert.IsNull(sides.GetComponent<MeshRenderer>());
        var mc = sides.GetComponent<MeshCollider>();
        Assert.IsNotNull(mc);
        Assert.IsNotNull(mc.sharedMesh);
        Assert.AreEqual(HideFlags.DontSave, mc.sharedMesh.hideFlags & HideFlags.DontSave);

        var l = r.StairValidations[s.id].layout;
        var md = StairMeshBuilder.Steps(s, l, b.data.floors[0].elevation);
        var sideGroup = md.groups.Find(g => g.key == "stair:" + s.id + "/side");
        Assert.AreEqual(sideGroup.triangles.Count, mc.sharedMesh.triangles.Length, "side triangles only");
        // Vertical faces only: no tread/landing tops.
        var n = mc.sharedMesh.normals;
        foreach (var nn in n) Assert.Less(Mathf.Abs(nn.y), 1e-3f);

        s.collisionRamp = false;
        r.Regenerate();
        Assert.IsNull(StairObj(s.id, "StepsSides"), "the steps' own collider covers the sides");
    }

    static float GroupArea(MeshData md, string key)
    {
        var g = md.groups.Find(x => x.key == key);
        if (g == null) return 0f;
        float a = 0f;
        for (int i = 0; i < g.triangles.Count; i += 3)
        {
            var p = md.vertices[g.triangles[i]]; var q = md.vertices[g.triangles[i + 1]]; var w = md.vertices[g.triangles[i + 2]];
            a += Vector3.Cross(q - p, w - p).magnitude * 0.5f;
        }
        return a;
    }

    /// <summary>Edge quads of a group whose plan midpoint lies on the polygon's boundary.</summary>
    static int EdgeQuadsOn(MeshData md, string key, IList<Vector2> poly)
    {
        var g = md.groups.Find(x => x.key == key);
        if (g == null) return 0;
        int count = 0;
        for (int i = 0; i < g.triangles.Count; i += 6)
        {
            var a = md.vertices[g.triangles[i]]; var c = md.vertices[g.triangles[i + 1]]; var d = md.vertices[g.triangles[i + 2]];
            var mid = new Vector2((a.x + c.x + d.x) / 3f, (a.z + c.z + d.z) / 3f);
            if (StairValidator.DistanceToBoundary(mid, new List<Vector2>(poly)) < 1e-2f) count++;
        }
        return count;
    }

    [Test] public void TwoRooms_HoleOnlyCutsItsRegion([Values(false, true)] bool inRightRoom)
    {
        // 16x8 building split at x = 8 on both floors; the stair runs +x inside one room.
        var f0 = b.data.AddFloor();
        b.data.AddSegment(f0, new Vector2(0, 0), new Vector2(16, 0));
        b.data.AddSegment(f0, new Vector2(16, 0), new Vector2(16, 8));
        b.data.AddSegment(f0, new Vector2(16, 8), new Vector2(0, 8));
        b.data.AddSegment(f0, new Vector2(0, 8), new Vector2(0, 0));
        b.data.AddSegment(f0, new Vector2(8, 0), new Vector2(8, 8));
        var f1 = b.data.AddFloor();
        b.data.AddSegment(f1, new Vector2(8, 0), new Vector2(8, 8));
        float x0 = inRightRoom ? 9.5f : 1.5f;
        var s = b.data.AddStair(0, new List<StairPoint> { P(x0, 4), P(x0 + 4.5f, 4) });
        r.Regenerate();
        var v = r.StairValidations[s.id];
        Assert.IsTrue(v.Valid, string.Join(",", v.issues));
        var hole = v.layout.hole;
        float holeArea = PolygonArea(hole);
        var holes = new List<IList<Vector2>> { hole };

        var floorMd = SlabMeshBuilder.BuildFloor(f1, holes);
        var ceilMd = SlabMeshBuilder.BuildCeiling(b.data.floors[0], false, holes);
        var regions1 = PlanarFaces.Compute(f1).regions;
        var regions0 = PlanarFaces.Compute(b.data.floors[0]).regions;
        Assert.AreEqual(2, regions1.Count);
        Assert.AreEqual(2, regions0.Count);
        int edgesOnHole = 0;
        foreach (var reg in regions1)
        {
            bool mine = (reg.polygon[0].x + reg.polygon[1].x + reg.polygon[2].x) / 3f > 8f == inRightRoom;
            Assert.AreEqual(mine ? 64f - holeArea : 64f, GroupArea(floorMd, "region:" + reg.key + "/floor"), 1e-2f, "floor " + reg.key);
            Assert.AreEqual(mine ? 64f - holeArea : 64f, GroupArea(floorMd, "region:" + reg.key + "/floor/under"), 1e-2f);
            edgesOnHole += EdgeQuadsOn(floorMd, "region:" + reg.key + "/floor/edge", hole);
        }
        Assert.AreEqual(hole.Count, edgesOnHole, "hole edge faces only once");
        foreach (var reg in regions0)
        {
            bool mine = (reg.polygon[0].x + reg.polygon[1].x + reg.polygon[2].x) / 3f > 8f == inRightRoom;
            Assert.AreEqual(mine ? 64f - holeArea : 64f, GroupArea(ceilMd, "region:" + reg.key + "/ceiling"), 1e-2f, "ceiling " + reg.key);
        }
        // The renderer's own slabs agree.
        Assert.AreEqual(128f - holeArea, FaceArea("Floor 1/Floor", Vector3.up, b.data.floors[1].elevation), 1e-2f);
    }
}
