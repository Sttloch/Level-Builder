using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using LevelBuilder;
using LevelBuilder.Editor;
using LevelBuilder.Geometry;

public class PaintOpsTests
{
    GameObject go;
    Building b;
    Material m;

    [SetUp] public void SetUp()
    {
        go = new GameObject("PaintBuilding");
        b = go.AddComponent<Building>();
        m = new Material(Shader.Find("Hidden/Internal-Colored"));
    }

    [TearDown] public void TearDown()
    {
        if (go != null) Object.DestroyImmediate(go);
        if (m != null) Object.DestroyImmediate(m);
    }

    FloorData Room()
    {
        var f = b.data.AddFloor();
        b.data.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
        b.data.AddSegment(f, new Vector2(4, 0), new Vector2(4, 4));
        b.data.AddSegment(f, new Vector2(4, 4), new Vector2(0, 4));
        b.data.AddSegment(f, new Vector2(0, 4), new Vector2(0, 0));
        return f;
    }

    static string RegionKey(FloorData f) => "region:" + PlanarFaces.Compute(f).regions.First(r => !r.open).key;

    [Test] public void PaintFace_SetsOnlyThatSide()
    {
        var f = Room();
        var s = f.segments[0];
        PaintOps.PaintFace(b, f, s.id + "/L", m, new Vector3(2, 1, 0));
        Assert.AreSame(m, s.materials.left);
        Assert.IsNull(s.materials.right);
        Assert.IsNull(s.materials.caps);
        Assert.IsNull(s.materials.top);
    }

    [Test] public void PaintPiece_Wall_SetsAllWallFaces()
    {
        var f = Room();
        var s = f.segments[0];
        PaintOps.PaintPiece(b, f, s.id + "/L", m, new Vector3(2, 1, 0));
        Assert.AreSame(m, s.materials.left);
        Assert.AreSame(m, s.materials.right);
        Assert.AreSame(m, s.materials.caps);
        Assert.AreSame(m, s.materials.top);
        Assert.IsNull(f.segments[1].materials?.left);
    }

    [Test] public void PaintPiece_Region_SetsFloorCeilingEdge()
    {
        var f = Room();
        var key = RegionKey(f);
        PaintOps.PaintPiece(b, f, key + "/floor", m, new Vector3(1, 0, 1));
        Assert.AreEqual(1, f.regionPaints.Count);
        var p = f.regionPaints[0];
        Assert.AreSame(m, p.floor);
        Assert.AreSame(m, p.ceiling);
        Assert.AreSame(m, p.edge);
        Assert.AreEqual(new Vector2(1, 1), p.seed); // seed = plan point of the hit
    }

    [Test] public void Sample_ReturnsFaceMaterial()
    {
        var f = Room();
        var s = f.segments[0];
        Assert.IsNull(PaintOps.Sample(b, f, s.id + "/R"));
        PaintOps.PaintFace(b, f, s.id + "/R", m, Vector3.zero);
        Assert.AreSame(m, PaintOps.Sample(b, f, s.id + "/R"));
        Assert.IsNull(PaintOps.Sample(b, f, s.id + "/L"));
    }

    [Test] public void Paint_IsOneUndoStep()
    {
        var f = Room();
        var s = f.segments[0];
        string sid = s.id;
        Undo.IncrementCurrentGroup();
        PaintOps.PaintPiece(b, f, sid + "/L", m, new Vector3(2, 1, 0));
        Assert.AreSame(m, s.materials.right);
        Undo.PerformUndo();
        var s2 = b.data.floors[0].GetSegment(sid);
        Assert.IsNull(s2.materials?.left);
        Assert.IsNull(s2.materials?.right);
        Assert.IsNull(s2.materials?.top);
    }

    [Test] public void FaceNames_ReadableSpanish()
    {
        Assert.AreEqual("Pared – lado izquierdo", FaceNames.Of("abc/L"));
        Assert.AreEqual("Pared – lado derecho", FaceNames.Of("abc/R"));
        Assert.AreEqual("Pared – canto", FaceNames.Of("abc/start"));
        Assert.AreEqual("Pared – tapa", FaceNames.Of("abc/top"));
        Assert.AreEqual("Unión", FaceNames.Of("node:n1/side"));
        Assert.AreEqual("Suelo", FaceNames.Of("region:k/1/floor"));
        Assert.AreEqual("Suelo – cara inferior", FaceNames.Of("region:k/floor/under"));
        Assert.AreEqual("Canto del forjado", FaceNames.Of("region:k/floor/edge"));
        Assert.AreEqual("Techo", FaceNames.Of("region:k/ceiling"));
        Assert.AreEqual("Cubierta", FaceNames.Of("region:k/roof"));
        Assert.AreEqual("Canto de cubierta", FaceNames.Of("region:k/roof/edge"));
        Assert.AreEqual("Jamba", FaceNames.Of("o1/jamb"));
        Assert.AreEqual("Alféizar", FaceNames.Of("o1/sill"));
        Assert.AreEqual("Umbral", FaceNames.Of("o1/threshold"));
        Assert.AreEqual("Escalón – huella", FaceNames.Of("stair:s/tread"));
        Assert.AreEqual("Escalón – contrahuella", FaceNames.Of("stair:s/riser"));
        Assert.AreEqual("Escalón – lateral", FaceNames.Of("stair:s/side"));
        Assert.AreEqual("Descansillo", FaceNames.Of("stair:s/landing"));
        Assert.AreEqual("Barandilla", FaceNames.Of("stair:s/rail"));
    }
}
