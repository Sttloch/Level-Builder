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
