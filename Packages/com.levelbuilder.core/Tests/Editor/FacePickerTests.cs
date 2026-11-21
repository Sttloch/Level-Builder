using System.Linq;
using NUnit.Framework;
using UnityEngine;
using LevelBuilder;
using LevelBuilder.Editor;

public class FacePickerTests
{
    GameObject go;
    BuildingRenderer r;
    Building b;

    [SetUp] public void SetUp()
    {
        go = new GameObject("PickBuilding");
        r = go.AddComponent<BuildingRenderer>();
        b = go.GetComponent<Building>();
    }

    [TearDown] public void TearDown() { if (go != null) Object.DestroyImmediate(go); }

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

    [Test] public void Pick_WallSide_ReturnsLeftOrRight()
    {
        var f = Room(b.data.AddFloor(), 0, 4);
        var s = SegFrom(f, Vector2.zero); // south wall, runs +x; its left side faces +z (into the room)
        r.Regenerate();
        Assert.IsTrue(FacePicker.Pick(b, new Ray(new Vector3(2, 1, 2), Vector3.back), out var key, out var floor, out var hit));
        Assert.AreEqual(s.id + "/L", key);
        Assert.AreSame(f, floor);
        Assert.AreEqual(0f, hit.z, 0.2f); // wall thickness
        // From outside, the right side.
        Assert.IsTrue(FacePicker.Pick(b, new Ray(new Vector3(2, 1, -5), Vector3.forward), out key, out _, out _));
        Assert.AreEqual(s.id + "/R", key);
    }

    [Test] public void Pick_Floor_ReturnsRegionFloorKey()
    {
        Room(b.data.AddFloor(), 0, 4);
        r.Regenerate();
        Assert.IsTrue(FacePicker.Pick(b, new Ray(new Vector3(2, 1, 2), Vector3.down), out var key, out _, out var hit));
        StringAssert.StartsWith("region:", key);
        StringAssert.EndsWith("/floor", key);
        Assert.Less(hit.y, 0.5f);
    }

    [Test] public void Pick_NearestHitWins()
    {
        var f = Room(b.data.AddFloor(), 0, 4);
        var s = SegFrom(f, new Vector2(0, 4)); // west wall: from (0,4) to (0,0)
        r.Regenerate();
        // Ray along +x crosses the west wall first, then the east wall.
        Assert.IsTrue(FacePicker.Pick(b, new Ray(new Vector3(-5, 1, 2), Vector3.right), out var key, out _, out var hit));
        Assert.AreEqual(0f, hit.x, 0.2f);
        StringAssert.StartsWith(s.id, key);
    }

    [Test] public void Pick_Miss_ReturnsFalse()
    {
        Room(b.data.AddFloor(), 0, 4);
        r.Regenerate();
        Assert.IsFalse(FacePicker.Pick(b, new Ray(new Vector3(50, 1, 50), Vector3.right), out var key, out var floor, out _));
        Assert.IsNull(key);
        Assert.IsNull(floor);
    }

    [Test] public void Pick_IgnoresHiddenFloors()
    {
        var f0 = Room(b.data.AddFloor(), 0, 4);
        var f1 = Room(b.data.AddFloor(), 0, 4);
        r.Regenerate();
        var down = new Ray(new Vector3(2, 30, 2), Vector3.down);
        Assert.IsTrue(FacePicker.Pick(b, down, out _, out var floor, out var hitAll));
        Assert.AreSame(f1, floor);
        foreach (var mr in go.transform.Find("Floor 1").GetComponentsInChildren<MeshRenderer>(true)) mr.enabled = false;
        Assert.IsTrue(FacePicker.Pick(b, down, out _, out floor, out var hitLow));
        Assert.AreSame(f0, floor);
        Assert.Less(hitLow.y, hitAll.y - 1f);
    }

    [Test] public void Pick_InactiveFloorRoot_IsIgnored()
    {
        var f0 = Room(b.data.AddFloor(), 0, 4);
        Room(b.data.AddFloor(), 0, 4);
        r.Regenerate();
        go.transform.Find("Floor 1").gameObject.SetActive(false);
        Assert.IsTrue(FacePicker.Pick(b, new Ray(new Vector3(2, 30, 2), Vector3.down), out _, out var floor, out _));
        Assert.AreSame(f0, floor);
    }

    [Test] public void Pick_RotatedNonUniformScale_StillCorrect()
    {
        var f = Room(b.data.AddFloor(), 0, 4);
        var s = SegFrom(f, Vector2.zero);
        r.Regenerate();
        go.transform.SetPositionAndRotation(new Vector3(3, 1, -2), Quaternion.Euler(0, 30, 0));
        go.transform.localScale = new Vector3(2f, 1f, 0.5f);
        var t = go.transform;
        // Aim from inside the room (local 2,1,2) through the south wall (local z = 0) and the opposite north wall.
        var o = t.TransformPoint(new Vector3(2, 1, 2));
        var dir = (t.TransformPoint(new Vector3(2, 1, 0)) - o).normalized;
        Assert.IsTrue(FacePicker.Pick(b, new Ray(o, dir), out var key, out var floor, out var hit));
        Assert.AreEqual(s.id + "/L", key);
        Assert.AreSame(f, floor);
        var local = t.InverseTransformPoint(hit);
        Assert.AreEqual(0f, local.z, 0.2f);
        // Nearest wins: from outside south, the near (south) wall is hit, not the north one.
        var o2 = t.TransformPoint(new Vector3(2, 1, -5));
        var dir2 = (t.TransformPoint(new Vector3(2, 1, 8)) - o2).normalized;
        Assert.IsTrue(FacePicker.Pick(b, new Ray(o2, dir2), out key, out _, out hit));
        Assert.AreEqual(s.id + "/R", key);
        Assert.AreEqual(0f, t.InverseTransformPoint(hit).z, 0.2f);
    }
}
