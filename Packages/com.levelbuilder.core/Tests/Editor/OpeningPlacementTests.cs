using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using LevelBuilder;
using LevelBuilder.Editor;

public class OpeningPlacementTests
{
    GameObject go;

    [TearDown] public void TearDown()
    {
        if (go != null) Object.DestroyImmediate(go);
    }

    // 6 x 4 rectangle; returns the south wall (0,0) -> (6,0).
    static WallSegment Rect6x4(BuildingData b, out FloorData f)
    {
        f = b.floors.Count > 0 ? b.floors[0] : b.AddFloor();
        var south = b.AddSegment(f, new Vector2(0, 0), new Vector2(6, 0))[0];
        b.AddSegment(f, new Vector2(6, 0), new Vector2(6, 4));
        b.AddSegment(f, new Vector2(6, 4), new Vector2(0, 4));
        b.AddSegment(f, new Vector2(0, 4), new Vector2(0, 0));
        return south;
    }

    // Center measured from the segment's start node, whichever end that is.
    static float CenterFromWestX(FloorData f, WallSegment s, float x) =>
        f.GetNode(s.startNodeId).position.x < 3f ? x : 6f - x;

    static WallOpening Door(float center) =>
        OpeningPresets.Create(OpeningPresets.For(OpeningType.Door)[0], center);

    [Test] public void Pick_ProjectsOntoNearestWall()
    {
        var b = new BuildingData();
        var south = Rect6x4(b, out var f);
        Assert.IsTrue(OpeningPlacement.Pick(f, new Vector2(2.3f, 0.2f), 0.5f, out var segId, out var center));
        Assert.AreEqual(south.id, segId);
        Assert.AreEqual(CenterFromWestX(f, south, 2.3f), center, 1e-4f);
    }

    [Test] public void Pick_TooFar_ReturnsFalse()
    {
        var b = new BuildingData();
        Rect6x4(b, out var f);
        Assert.IsFalse(OpeningPlacement.Pick(f, new Vector2(3f, 2f), 0.5f, out var segId, out _));
        Assert.IsNull(segId);
    }

    [Test] public void SnapCenter_RoundsToTenCentimetres()
    {
        Assert.AreEqual(2.3f, OpeningPlacement.SnapCenter(2.34f), 1e-5f);
        Assert.AreEqual(2.4f, OpeningPlacement.SnapCenter(2.36f), 1e-5f);
        Assert.AreEqual(2.5f, OpeningPlacement.SnapCenter(2.34f, 0.5f), 1e-5f);
    }

    [Test] public void Fits_FalseNearCorner_TrueInMiddle()
    {
        var b = new BuildingData();
        var south = Rect6x4(b, out var f);
        Assert.IsFalse(OpeningPlacement.Fits(f, south.id, Door(CenterFromWestX(f, south, 0.3f))));
        Assert.IsTrue(OpeningPlacement.Fits(f, south.id, Door(3f)));
        // The probe never stays in the data.
        Assert.AreEqual(0, south.openings.Count);
    }

    [Test] public void Fits_IgnoresTheOpeningItselfWhenMoving()
    {
        var b = new BuildingData();
        var south = Rect6x4(b, out var f);
        var door = b.AddOpening(f, south.id, Door(3f));
        var probe = door.Clone();
        probe.id = door.id;
        probe.center = 3.2f; // would overlap the original if it were not ignored
        Assert.IsTrue(OpeningPlacement.Fits(f, south.id, probe));
        Assert.AreEqual(1, south.openings.Count);
        Assert.AreSame(door, south.openings[0]);
        Assert.AreEqual(3f, door.center, 1e-5f);
    }

    [Test] public void PickOpening_FindsDoorUnderPoint()
    {
        var b = new BuildingData();
        var south = Rect6x4(b, out var f);
        var door = b.AddOpening(f, south.id, Door(3f));
        Assert.AreEqual(door.id, OpeningPlacement.PickOpening(f, new Vector2(3.2f, 0.05f), 0.5f));
        Assert.IsNull(OpeningPlacement.PickOpening(f, new Vector2(5f, 0f), 0.5f));
    }

    [Test] public void Resize_WidthIsSymmetric_HeightMovesTop_SillKeepsTop()
    {
        // Edge dragged to u = 3.8 with the centre at 3 -> width 1.6; clamped to MinSize.
        Assert.AreEqual(1.6f, OpeningPlacement.WidthFromEdge(3f, 3.8f), 1e-5f);
        Assert.AreEqual(1.6f, OpeningPlacement.WidthFromEdge(3f, 2.2f), 1e-5f);
        Assert.AreEqual(0.3f, OpeningPlacement.WidthFromEdge(3f, 3.05f), 1e-5f);
        // Top dragged to v = 2.5 with sill 0.9 -> height 1.6; clamped.
        Assert.AreEqual(1.6f, OpeningPlacement.HeightFromTop(0.9f, 2.5f), 1e-5f);
        Assert.AreEqual(0.3f, OpeningPlacement.HeightFromTop(0.9f, 0.5f), 1e-5f);
        // Bottom of a 0.9 + 1.2 window dragged to 0.5 -> sill 0.5, height 1.6 (top stays at 2.1).
        OpeningPlacement.SillFromBottom(0.9f, 1.2f, 0.5f, out float sill, out float h);
        Assert.AreEqual(0.5f, sill, 1e-5f);
        Assert.AreEqual(1.6f, h, 1e-5f);
        // Never above top - MinSize, never below the floor.
        OpeningPlacement.SillFromBottom(0.9f, 1.2f, 2.0f, out sill, out h);
        Assert.AreEqual(1.8f, sill, 1e-5f);
        Assert.AreEqual(0.3f, h, 1e-5f);
        OpeningPlacement.SillFromBottom(0.9f, 1.2f, -1f, out sill, out h);
        Assert.AreEqual(0f, sill, 1e-5f);
        Assert.AreEqual(2.1f, h, 1e-5f);
    }

    [Test] public void PlaceOpening_IsOneUndoStep()
    {
        go = BuildingMenu.CreateBuilding(null);
        var b = go.GetComponent<Building>();
        var south = Rect6x4(b.data, out _);
        string segId = south.id;
        Undo.ClearAll();
        Undo.IncrementCurrentGroup();

        var placed = OpeningPlacement.Place(b, 0, segId, Door(3f));
        Assert.IsNotNull(placed);
        Assert.AreEqual(1, b.data.floors[0].GetSegment(segId).openings.Count);

        Undo.PerformUndo();
        Assert.AreEqual(0, b.data.floors[0].GetSegment(segId).openings.Count); // data may be a new instance
        Undo.PerformRedo();
        var openings = b.data.floors[0].GetSegment(segId).openings;
        Assert.AreEqual(1, openings.Count);
        Assert.AreEqual(placed.id, openings[0].id);
    }

    [Test] public void ApplyType_DoorHasNoSill_WindowAndEmptyKeepIt()
    {
        var o = new WallOpening { type = OpeningType.Window, center = 3f, width = 1.2f, height = 1.2f, sill = 0.9f };
        OpeningPlacement.ApplyType(o, OpeningType.Empty);
        Assert.AreEqual(OpeningType.Empty, o.type);
        Assert.AreEqual(0.9f, o.sill, 1e-6f);
        OpeningPlacement.ApplyType(o, OpeningType.Door);
        Assert.AreEqual(OpeningType.Door, o.type);
        Assert.AreEqual(0f, o.sill);
        Assert.AreEqual(1.2f, o.height, 1e-6f);
        Assert.IsTrue(OpeningPlacement.HasSill(OpeningType.Window));
        Assert.IsTrue(OpeningPlacement.HasSill(OpeningType.Empty));
        Assert.IsFalse(OpeningPlacement.HasSill(OpeningType.Door));
    }

    [Test] public void SetType_WindowToDoor_SitsOnFloor_OneUndoStep()
    {
        go = BuildingMenu.CreateBuilding(null);
        var b = go.GetComponent<Building>();
        var south = Rect6x4(b.data, out var f);
        var o = b.data.AddOpening(f, south.id,
            new WallOpening { type = OpeningType.Window, center = 3f, width = 1.2f, height = 1.2f, sill = 0.9f });
        Undo.ClearAll();
        Undo.IncrementCurrentGroup();

        Assert.IsTrue(OpeningPlacement.SetType(b, 0, o.id, OpeningType.Door));
        var now = b.data.FindOpening(b.data.floors[0], o.id).o;
        Assert.AreEqual(OpeningType.Door, now.type);
        Assert.AreEqual(0f, now.sill);

        Undo.PerformUndo();
        now = b.data.FindOpening(b.data.floors[0], o.id).o;
        Assert.AreEqual(OpeningType.Window, now.type);
        Assert.AreEqual(0.9f, now.sill, 1e-6f);
        Assert.IsFalse(OpeningPlacement.SetType(b, 0, "missing", OpeningType.Door));
    }

    [Test] public void Remove_IsOneUndoStep()
    {
        go = BuildingMenu.CreateBuilding(null);
        var b = go.GetComponent<Building>();
        var south = Rect6x4(b.data, out _);
        string segId = south.id;
        var o = b.data.AddOpening(b.data.floors[0], segId, Door(3f));
        Undo.ClearAll();
        Undo.IncrementCurrentGroup();

        Assert.IsTrue(OpeningPlacement.Remove(b, 0, o.id));
        Assert.AreEqual(0, b.data.floors[0].GetSegment(segId).openings.Count);
        Assert.IsFalse(OpeningPlacement.Remove(b, 0, o.id), "already gone");

        Undo.PerformUndo();
        var openings = b.data.floors[0].GetSegment(segId).openings;
        Assert.AreEqual(1, openings.Count);
        Assert.AreEqual(o.id, openings[0].id);
        Undo.PerformRedo();
        Assert.AreEqual(0, b.data.floors[0].GetSegment(segId).openings.Count);
    }
}
