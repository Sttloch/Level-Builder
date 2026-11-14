using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LevelBuilder;
using LevelBuilder.Geometry;

public class WallMeshBuilderTests
{
    static Bounds BoundsOf(MeshData md)
    {
        var b = new Bounds(md.vertices[0], Vector3.zero);
        foreach (var v in md.vertices) b.Encapsulate(v);
        return b;
    }

    static FaceGroup Group(MeshData md, string key) => md.groups.Find(g => g.key == key);

    /// <summary>Each triangle's geometric normal (Cross(b-a, c-a), as Mesh.RecalculateNormals uses) must agree with the stored normals.</summary>
    static void AssertWindingMatchesNormals(MeshData md)
    {
        int checkedTris = 0;
        foreach (var g in md.groups)
        {
            Assert.AreEqual(0, g.triangles.Count % 3, g.key);
            for (int i = 0; i < g.triangles.Count; i += 3)
            {
                int a = g.triangles[i], b = g.triangles[i + 1], c = g.triangles[i + 2];
                var n = Vector3.Cross(md.vertices[b] - md.vertices[a], md.vertices[c] - md.vertices[a]);
                if (n.sqrMagnitude < 1e-12f) continue;
                n.Normalize();
                Assert.Greater(Vector3.Dot(n, md.normals[a]), 0.99f, $"{g.key} tri {i / 3} vs normal of a");
                Assert.Greater(Vector3.Dot(n, md.normals[b]), 0.99f, $"{g.key} tri {i / 3} vs normal of b");
                Assert.Greater(Vector3.Dot(n, md.normals[c]), 0.99f, $"{g.key} tri {i / 3} vs normal of c");
                checkedTris++;
            }
        }
        Assert.Greater(checkedTris, 0);
    }

    /// <summary>
    /// Closed-surface check (open only at the bottom, y = baseY, where walls sit on the slab):
    /// after welding positions within 1e-4 and splitting edges at vertices lying on them
    /// (T-junctions), every directed edge a→b must be matched by the same number of b→a edges.
    /// </summary>
    static void AssertWatertight(MeshData md, float baseY)
    {
        const float tol = 1e-4f;
        var pos = new List<Vector3>();
        var map = new int[md.vertices.Count];
        for (int i = 0; i < md.vertices.Count; i++)
        {
            int found = -1;
            for (int j = 0; j < pos.Count; j++)
                if ((pos[j] - md.vertices[i]).sqrMagnitude < tol * tol) { found = j; break; }
            if (found < 0) { found = pos.Count; pos.Add(md.vertices[i]); }
            map[i] = found;
        }

        var edges = new Dictionary<(int, int), int>();
        void AddEdge(int p, int q)
        {
            var on = new List<KeyValuePair<float, int>>();
            var a = pos[p]; var ab = pos[q] - a;
            float len2 = ab.sqrMagnitude;
            for (int w = 0; w < pos.Count; w++)
            {
                if (w == p || w == q) continue;
                float t = Vector3.Dot(pos[w] - a, ab) / len2;
                if (t <= 0f || t >= 1f) continue;
                if ((a + ab * t - pos[w]).magnitude < tol) on.Add(new KeyValuePair<float, int>(t, w));
            }
            on.Sort((x, y) => x.Key.CompareTo(y.Key));
            int prev = p;
            foreach (var kv in on) { Count(prev, kv.Value); prev = kv.Value; }
            Count(prev, q);
        }
        void Count(int p, int q)
        {
            edges.TryGetValue((p, q), out int c);
            edges[(p, q)] = c + 1;
        }

        foreach (var g in md.groups)
            for (int i = 0; i < g.triangles.Count; i += 3)
            {
                int a = map[g.triangles[i]], b = map[g.triangles[i + 1]], c = map[g.triangles[i + 2]];
                if (a == b || b == c || c == a) continue;
                AddEdge(a, b); AddEdge(b, c); AddEdge(c, a);
            }

        var problems = new List<string>();
        foreach (var kv in edges)
        {
            var (p, q) = kv.Key;
            if (Mathf.Abs(pos[p].y - baseY) < tol && Mathf.Abs(pos[q].y - baseY) < tol) continue;
            edges.TryGetValue((q, p), out int back);
            if (back != kv.Value) problems.Add($"{pos[p]:F3}->{pos[q]:F3} x{kv.Value} vs back x{back}");
        }
        Assert.IsEmpty(problems, "open or inconsistent edges:\n" + string.Join("\n", problems));
    }

    static float TopArea(MeshData md)
    {
        float area = 0f;
        foreach (var g in md.groups)
            for (int i = 0; i < g.triangles.Count; i += 3)
            {
                int a = g.triangles[i], b = g.triangles[i + 1], c = g.triangles[i + 2];
                if (md.normals[a].y < 0.99f) continue;
                area += Vector3.Cross(md.vertices[b] - md.vertices[a], md.vertices[c] - md.vertices[a]).magnitude * 0.5f;
            }
        return area;
    }

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

    static WallSegment SegmentBetween(FloorData f, Vector2 a, Vector2 b)
    {
        foreach (var s in f.segments)
        {
            var p = f.GetNode(s.startNodeId).position; var q = f.GetNode(s.endNodeId).position;
            if (((p - a).magnitude < 1e-3f && (q - b).magnitude < 1e-3f) || ((p - b).magnitude < 1e-3f && (q - a).magnitude < 1e-3f))
                return s;
        }
        return null;
    }

    static (BuildingData b, FloorData f) StraightWall()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        f.height = 3f;
        b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
        return (b, f);
    }

    [Test] public void StraightWall_BoundsMatchLengthThicknessHeight()
    {
        var (_, f) = StraightWall();
        var md = WallMeshBuilder.Build(f, 0f);
        var mesh = md.ToMesh();
        try
        {
            var bb = mesh.bounds;
            Assert.AreEqual(0f, bb.min.x, 1e-4f); Assert.AreEqual(4f, bb.max.x, 1e-4f);
            Assert.AreEqual(-0.1f, bb.min.z, 1e-4f); Assert.AreEqual(0.1f, bb.max.z, 1e-4f);
            Assert.AreEqual(0f, bb.min.y, 1e-4f); Assert.AreEqual(3f, bb.max.y, 1e-4f);
            Assert.AreEqual(1, mesh.subMeshCount);
        }
        finally { Object.DestroyImmediate(mesh); }
    }

    [Test] public void StraightWall_HasFiveFaceGroups()
    {
        var (_, f) = StraightWall();
        var id = f.segments[0].id;
        var md = WallMeshBuilder.Build(f, 0f);
        Assert.AreEqual(5, md.groups.Count);
        foreach (var suffix in new[] { "L", "R", "start", "end", "top" })
        {
            var g = Group(md, id + "/" + suffix);
            Assert.IsNotNull(g, suffix);
            Assert.GreaterOrEqual(g.triangles.Count, 6, suffix);
        }
    }

    [Test] public void StraightWall_WindingAgreesWithNormals_AndOutward()
    {
        var (_, f) = StraightWall();
        var id = f.segments[0].id;
        var md = WallMeshBuilder.Build(f, 0f);
        AssertWindingMatchesNormals(md);
        // Left of (0,0)->(4,0) is plan +y = world +z.
        Assert.AreEqual(1f, md.normals[Group(md, id + "/L").triangles[0]].z, 1e-4f);
        Assert.AreEqual(-1f, md.normals[Group(md, id + "/R").triangles[0]].z, 1e-4f);
        Assert.AreEqual(-1f, md.normals[Group(md, id + "/start").triangles[0]].x, 1e-4f);
        Assert.AreEqual(1f, md.normals[Group(md, id + "/end").triangles[0]].x, 1e-4f);
        Assert.AreEqual(1f, md.normals[Group(md, id + "/top").triangles[0]].y, 1e-4f);

        // Unity's own RecalculateNormals must agree with the stored ones.
        var mesh = md.ToMesh();
        try
        {
            var stored = mesh.normals;
            mesh.RecalculateNormals();
            var recalculated = mesh.normals;
            for (int i = 0; i < stored.Length; i++)
                Assert.Greater(Vector3.Dot(stored[i], recalculated[i]), 0.99f, $"vertex {i}");
        }
        finally { Object.DestroyImmediate(mesh); }
    }

    [Test] public void LCorner_OuterCornerAtMiter()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
        b.AddSegment(f, new Vector2(4, 0), new Vector2(4, 3));
        var md = WallMeshBuilder.Build(f, 0f);
        bool foundOuter = false, foundInner = false;
        foreach (var v in md.vertices)
        {
            if (Mathf.Abs(v.x - 4.1f) < 1e-4f && Mathf.Abs(v.z + 0.1f) < 1e-4f) foundOuter = true;
            if (Mathf.Abs(v.x - 3.9f) < 1e-4f && Mathf.Abs(v.z - 0.1f) < 1e-4f) foundInner = true;
        }
        Assert.IsTrue(foundOuter, "outer mitre corner (4.1, y, -0.1) missing");
        Assert.IsTrue(foundInner, "inner mitre corner (3.9, y, 0.1) missing");
        AssertWindingMatchesNormals(md);
    }

    [Test] public void AcuteAngle_NoVertexBeyondMiterLimit()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        var corner = Vector2.zero;
        var far1 = new Vector2(4, 0);
        float rad = 10f * Mathf.Deg2Rad;
        var far2 = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * 4f;
        b.AddSegment(f, far1, corner);
        b.AddSegment(f, corner, far2);
        Assert.AreEqual(2, f.segments.Count);
        var md = WallMeshBuilder.Build(f, 0f);
        foreach (var v in md.vertices)
        {
            var p = new Vector2(v.x, v.z);
            // Every vertex belongs near one of the three nodes, or lies on the walls' sides,
            // but no vertex may stick out past the corner node by more than the miter limit.
            float along = Vector2.Dot(p - corner, ((far1 + far2) * 0.5f).normalized);
            Assert.Greater(along, -0.6f, $"spike behind the corner at {v}");
            float dCorner = (p - corner).magnitude;
            float dFar = Mathf.Min((p - far1).magnitude, (p - far2).magnitude);
            Assert.IsTrue(dCorner <= 0.6f || dFar <= 0.6f, $"vertex {v} is > 0.6 m from every node");
        }
    }

    [Test] public void SideFace_UVsInMetres()
    {
        var (_, f) = StraightWall();
        var id = f.segments[0].id;
        var md = WallMeshBuilder.Build(f, 0f);
        var g = Group(md, id + "/L");
        float maxU = float.MinValue, maxV = float.MinValue, minU = float.MaxValue;
        foreach (int i in g.triangles)
        {
            maxU = Mathf.Max(maxU, md.uvs[i].x);
            minU = Mathf.Min(minU, md.uvs[i].x);
            maxV = Mathf.Max(maxV, md.uvs[i].y);
        }
        Assert.AreEqual(4f, maxU, 0.11f);
        Assert.AreEqual(0f, minU, 0.11f);
        Assert.AreEqual(3f, maxV, 1e-4f);
    }

    [Test] public void HeightOverride_UsedInsteadOfFloorHeight()
    {
        var (_, f) = StraightWall();
        f.segments[0].heightOverride = 1.2f;
        var md = WallMeshBuilder.Build(f, 0f);
        Assert.AreEqual(1.2f, BoundsOf(md).max.y, 1e-4f);
    }

    [Test] public void BaseY_OffsetsWall()
    {
        var (_, f) = StraightWall();
        var bb = BoundsOf(WallMeshBuilder.Build(f, 5f));
        Assert.AreEqual(5f, bb.min.y, 1e-4f);
        Assert.AreEqual(8f, bb.max.y, 1e-4f);
    }

    [Test] public void StraightWall_Watertight()
    {
        var (_, f) = StraightWall();
        AssertWatertight(WallMeshBuilder.Build(f, 0f), 0f);
    }

    [Test] public void LCorner_WatertightAndTopAreaEqualsFootprint()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
        b.AddSegment(f, new Vector2(4, 0), new Vector2(4, 3));
        var md = WallMeshBuilder.Build(f, 2f);
        AssertWatertight(md, 2f);
        Assert.AreEqual(4.1f * 0.2f + 2.9f * 0.2f, TopArea(md), 1e-3f);
    }

    [Test] public void TJunction_TopAreaEqualsFootprint()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        b.AddSegment(f, new Vector2(0, 0), new Vector2(8, 0));
        b.AddSegment(f, new Vector2(4, 0), new Vector2(4, 3));
        Assert.AreEqual(3, f.segments.Count);
        var md = WallMeshBuilder.Build(f, 0f);
        // Main wall 8 x 0.2 plus the branch from y = 0.1 to 3.
        Assert.AreEqual(8f * 0.2f + 2.9f * 0.2f, TopArea(md), 1e-3f);
        var hub = f.nodes.Find(n => (n.position - new Vector2(4, 0)).magnitude < 1e-3f);
        Assert.IsNotNull(Group(md, "node:" + hub.id + "/top"), "junction top missing");
        AssertWatertight(md, 0f);
    }

    [Test] public void XJunction_TopAreaEqualsFootprint()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        b.AddSegment(f, new Vector2(0, 0), new Vector2(8, 0));
        b.AddSegment(f, new Vector2(4, -4), new Vector2(4, 4));
        Assert.AreEqual(4, f.segments.Count);
        var md = WallMeshBuilder.Build(f, 0f);
        // Two 8 x 0.2 walls overlapping in a 0.2 x 0.2 square.
        Assert.AreEqual(2f * 8f * 0.2f - 0.04f, TopArea(md), 1e-3f);
        AssertWatertight(md, 0f);
    }

    [Test] public void LowWallLCorner_NoOpening()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        f.height = 3f;
        var tall = b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0))[0];
        var low = b.AddSegment(f, new Vector2(4, 0), new Vector2(4, 3))[0];
        low.heightOverride = 1.2f;
        var md = WallMeshBuilder.Build(f, 0f);
        AssertWatertight(md, 0f);
        // The tall wall's end above the low wall is capped (square, 0.2 x 1.8).
        Assert.AreEqual(0.2f * 1.8f, GroupArea(md, tall.id + "/end"), 1e-3f);
        // Tops: tall wall 4 x 0.2 at 3 m; low wall plus the outer mitre corner at 1.2 m.
        Assert.AreEqual(4f * 0.2f + 0.1f * 0.2f + 2.9f * 0.2f, TopArea(md), 1e-3f);
    }

    [Test] public void LowBranchT_MainWallFaceClosedAboveBranch()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        f.height = 3f;
        b.AddSegment(f, new Vector2(0, 0), new Vector2(8, 0));
        b.AddSegment(f, new Vector2(4, 0), new Vector2(4, 3));
        var branch = SegmentBetween(f, new Vector2(4, 0), new Vector2(4, 3));
        branch.heightOverride = 1.2f;
        var west = SegmentBetween(f, new Vector2(0, 0), new Vector2(4, 0));
        var east = SegmentBetween(f, new Vector2(4, 0), new Vector2(8, 0));
        var md = WallMeshBuilder.Build(f, 0f);
        AssertWatertight(md, 0f);
        // Branch side (+y) of the main wall: full 8 x 3 minus the 0.2 x 1.2 covered by the branch.
        string westKey = west.id + (west.startNodeId == branch.startNodeId || west.startNodeId == branch.endNodeId ? "/R" : "/L");
        string eastKey = east.id + (east.startNodeId == branch.startNodeId || east.startNodeId == branch.endNodeId ? "/L" : "/R");
        Assert.AreEqual(8f * 3f - 0.2f * 1.2f, GroupArea(md, westKey) + GroupArea(md, eastKey), 1e-3f);
    }

    [Test] public void DifferentThicknessT_StepFaceClosesGap()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        b.AddSegment(f, new Vector2(0, 0), new Vector2(8, 0));
        b.AddSegment(f, new Vector2(4, 0), new Vector2(4, 3));
        SegmentBetween(f, new Vector2(4, 0), new Vector2(8, 0)).thickness = 0.4f;
        var md = WallMeshBuilder.Build(f, 0f);
        var hub = f.nodes.Find(n => (n.position - new Vector2(4, 0)).magnitude < 1e-3f);
        // Square fallback between the thin and thick halves leaves a 0.1 m step that must be faced.
        Assert.AreEqual(0.1f * 3f, GroupArea(md, "node:" + hub.id + "/side"), 1e-3f);
        AssertWatertight(md, 0f);
    }

    [Test] public void VerticalFaces_UReadsLeftToRightFromOutside()
    {
        var (_, f) = StraightWall();
        var md = WallMeshBuilder.Build(f, 0f);
        int pairs = 0;
        foreach (var g in md.groups)
        {
            if (g.key.EndsWith("/top")) continue;
            var idx = new HashSet<int>(g.triangles);
            foreach (int i in idx)
                foreach (int j in idx)
                {
                    if (Mathf.Abs(md.vertices[i].y - md.vertices[j].y) > 1e-4f) continue;
                    var right = Vector3.Cross(md.normals[i], Vector3.up);
                    float dp = Vector3.Dot(md.vertices[j] - md.vertices[i], right);
                    float du = md.uvs[j].x - md.uvs[i].x;
                    if (Mathf.Abs(dp) < 1e-4f) continue;
                    Assert.AreEqual(dp, du, 1e-4f, $"{g.key}: u must grow to the viewer's right in metres");
                    pairs++;
                }
        }
        Assert.Greater(pairs, 0);
    }

    [Test] public void Joins_LCorner_MapsToSegmentLeftRight()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        var s1 = b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0))[0];
        var s2 = b.AddSegment(f, new Vector2(4, 0), new Vector2(4, 3))[0];
        var j = WallJoins.Compute(f);
        Assert.AreEqual(4, j.Count);
        var e1 = j[(s1.id, false)];
        Assert.That((e1.right - new Vector2(4.1f, -0.1f)).magnitude, Is.LessThan(1e-4f));
        Assert.That((e1.left - new Vector2(3.9f, 0.1f)).magnitude, Is.LessThan(1e-4f));
        var st2 = j[(s2.id, true)];
        Assert.That((st2.right - new Vector2(4.1f, -0.1f)).magnitude, Is.LessThan(1e-4f));
        Assert.That((st2.left - new Vector2(3.9f, 0.1f)).magnitude, Is.LessThan(1e-4f));
        var free = j[(s1.id, true)];
        Assert.That((free.left - new Vector2(0f, 0.1f)).magnitude, Is.LessThan(1e-4f));
        Assert.That((free.right - new Vector2(0f, -0.1f)).magnitude, Is.LessThan(1e-4f));
    }
}
