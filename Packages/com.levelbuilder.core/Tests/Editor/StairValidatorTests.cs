using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LevelBuilder;
using LevelBuilder.Geometry;

// Floors 0 and 1 share an 8x6 exterior rectangle (0,0)-(8,6). Floor height 3 -> 17 risers, 16 pieces,
// run 4.48 m, so stairs run along +x (the 6 m side cannot hold run + both 1 m zones + walls).
public class StairValidatorTests
{
    static StairPoint P(float x, float y, StairPointKind k = StairPointKind.Corner) =>
        new StairPoint { position = new Vector2(x, y), kind = k };

    static BuildingData Building()
    {
        var b = new BuildingData();
        var f0 = b.AddFloor();
        b.AddSegment(f0, new Vector2(0, 0), new Vector2(8, 0));
        b.AddSegment(f0, new Vector2(8, 0), new Vector2(8, 6));
        b.AddSegment(f0, new Vector2(8, 6), new Vector2(0, 6));
        b.AddSegment(f0, new Vector2(0, 6), new Vector2(0, 0));
        b.AddFloor(); // copies the exterior
        return b;
    }

    // These geometry tests were laid out for a 1 m wide stair (the default is 1.2 m).
    static StairData Stair(BuildingData b, params StairPoint[] pts)
    {
        var s = b.AddStair(0, new List<StairPoint>(pts));
        s.width = 1f;
        return s;
    }

    static void Wall(BuildingData b, int floor, float ax, float ay, float bx, float by) =>
        b.AddSegment(b.floors[floor], new Vector2(ax, ay), new Vector2(bx, by));

    static List<StairIssue> Issues(params StairIssue[] i) => new List<StairIssue>(i);

    [Test] public void FreeStair_IsValid()
    {
        var b = Building();
        var s = Stair(b, P(1.5f, 2), P(6, 2));
        var v = StairValidator.Validate(b, s);
        Assert.IsTrue(v.layout.ok, v.layout.error);
        Assert.IsTrue(v.Valid, string.Join(",", v.issues));
    }

    [Test] public void ArrivalAgainstWall_IsBlocked()
    {
        var b = Building();
        Wall(b, 1, 6.2f, 0, 6.2f, 6); // stair top at x = 5.98
        var s = Stair(b, P(1.5f, 2), P(6, 2));
        CollectionAssert.AreEqual(Issues(StairIssue.ArrivalBlocked), StairValidator.Validate(b, s).issues);
    }

    [Test] public void DepartureOutsideBuilding_IsBlocked()
    {
        var b = Building();
        var s = Stair(b, P(0.6f, 2), P(6, 2)); // departure zone x in [-0.4, 0.6]
        CollectionAssert.AreEqual(Issues(StairIssue.DepartureBlocked), StairValidator.Validate(b, s).issues);
    }

    [Test] public void StepsThroughWall_Collision()
    {
        var b = Building();
        Wall(b, 0, 3.5f, 0, 3.5f, 6);
        var s = Stair(b, P(1.5f, 2), P(6, 2));
        CollectionAssert.AreEqual(Issues(StairIssue.Collision), StairValidator.Validate(b, s).issues);
    }

    [Test] public void HoleAcrossUpperWall()
    {
        var b = Building();
        Wall(b, 1, 4f, 0, 4f, 6); // hole spans x ~2.34..5.98
        var s = Stair(b, P(1.5f, 2), P(6, 2));
        var v = StairValidator.Validate(b, s);
        Assert.IsNotNull(v.layout.hole);
        CollectionAssert.AreEqual(Issues(StairIssue.HoleCrossesWall), v.issues);
    }

    [Test] public void NoFloorAbove()
    {
        var b = Building();
        var s = Stair(b, P(1.5f, 2), P(6, 2));
        b.floors.RemoveAt(1);
        var v = StairValidator.Validate(b, s);
        Assert.IsNotNull(v.layout);
        CollectionAssert.AreEqual(Issues(StairIssue.NoFloorAbove), v.issues);
    }

    [Test] public void RouteInvalid_SkipsGeometry()
    {
        var b = Building();
        var s = Stair(b, P(1.5f, 2), P(6, 2));
        s.route.RemoveAt(1);
        CollectionAssert.AreEqual(Issues(StairIssue.RouteInvalid), StairValidator.Validate(b, s).issues);
    }

    [Test] public void AutoFix_TurnsLastFlightTowardFreeSide()
    {
        var b = Building();
        Wall(b, 1, 6.7f, 0, 6.7f, 6);  // in front of the top edge (arrival x 5.98..6.98)
        Wall(b, 1, 0, 4.5f, 6.7f, 4.5f); // blocks a left (+y) turn; right (-y) is free
        var s = Stair(b, P(1.5f, 3), P(6, 3));
        var before = StairValidator.Validate(b, s);
        CollectionAssert.AreEqual(Issues(StairIssue.ArrivalBlocked), before.issues);
        var original = new List<StairPoint>(s.route);

        Assert.IsTrue(StairAutoFix.TryFix(b, s, out var route));
        CollectionAssert.AreEqual(original, s.route);
        var fixedStair = new StairData { id = s.id, fromFloorId = s.fromFloorId, toFloorId = s.toFloorId, width = s.width, route = route };
        var after = StairValidator.Validate(b, fixedStair);
        Assert.IsTrue(after.Valid, string.Join(",", after.issues));
        Assert.AreEqual(0f, Vector2.Dot(after.layout.endDir, before.layout.endDir), 1e-3f);
        Assert.Less(after.layout.endDir.y, -0.99f); // right of +x is -y
    }

    static StairValidation ValidateRoute(BuildingData b, StairData s, List<StairPoint> route) =>
        StairValidator.Validate(b, new StairData { id = s.id, fromFloorId = s.fromFloorId, toFloorId = s.toFloorId, width = s.width, route = route });

    [Test] public void AutoFix_TurnLastFlight_StepA()
    {
        // L-route: +x to a corner at (4.5,1.5), then +y; the stair ends at (4.5,3.96).
        var b = Building();
        Wall(b, 1, 0, 4.5f, 8, 4.5f); // across the arrival zone (y 3.96..4.96)
        var s = Stair(b, P(1.5f, 1.5f), P(4.5f, 1.5f), P(4.5f, 5));
        var before = StairValidator.Validate(b, s);
        CollectionAssert.AreEqual(Issues(StairIssue.ArrivalBlocked), before.issues);

        Assert.IsTrue(StairAutoFix.TryFix(b, s, out var route));
        Assert.AreEqual(s.route.Count, route.Count); // last point swung about (4.5,1.5), no corner inserted
        Assert.AreEqual(new Vector2(4.5f, 1.5f), route[1].position);
        var after = ValidateRoute(b, s, route);
        Assert.IsTrue(after.Valid, string.Join(",", after.issues));
        Assert.AreEqual(0f, Vector2.Dot(after.layout.endDir, before.layout.endDir), 1e-3f);
    }

    [Test] public void AutoFix_TurnFirstFlight_StepA()
    {
        // L-route: -y from (2.8,3) to a corner at (2.8,1.5), then +x.
        var b = Building();
        Wall(b, 0, 0, 3.5f, 8, 3.5f); // across the departure zone (y 3..4)
        var s = Stair(b, P(2.8f, 3), P(2.8f, 1.5f), P(7, 1.5f));
        var before = StairValidator.Validate(b, s);
        CollectionAssert.AreEqual(Issues(StairIssue.DepartureBlocked), before.issues);

        Assert.IsTrue(StairAutoFix.TryFix(b, s, out var route));
        Assert.AreEqual(s.route.Count, route.Count); // first point swung about (2.8,1.5), no corner inserted
        Assert.AreEqual(new Vector2(2.8f, 1.5f), route[1].position);
        var after = ValidateRoute(b, s, route);
        Assert.IsTrue(after.Valid, string.Join(",", after.issues));
        Assert.AreEqual(0f, Vector2.Dot(after.layout.startDir, before.layout.startDir), 1e-3f);
    }

    [Test] public void AutoFix_BothSidesValid_PrefersRoomierSide()
    {
        // Stair at y=4.1 ends at (5.98,4.1). Turning left (+y) leaves 0.02 m to the exterior wall,
        // turning right (-y) leaves 0.42 m (to the blocking wall): right must win though left is tried first.
        var b = Building();
        Wall(b, 1, 7f, 0, 7f, 6); // footprint 6.9..7.1 cuts the arrival zone (5.98..6.98)
        var s = Stair(b, P(1.5f, 4.1f), P(6, 4.1f));
        CollectionAssert.AreEqual(Issues(StairIssue.ArrivalBlocked), StairValidator.Validate(b, s).issues);
        var left = ValidateRoute(b, s, new List<StairPoint> { P(1.5f, 4.1f), P(5.98f, 4.1f), P(5.98f, 5.1f) });
        Assert.IsTrue(left.Valid, "left turn should also be valid: " + string.Join(",", left.issues));

        Assert.IsTrue(StairAutoFix.TryFix(b, s, out var route));
        var after = ValidateRoute(b, s, route);
        Assert.IsTrue(after.Valid, string.Join(",", after.issues));
        Assert.Less(after.layout.endDir.y, -0.99f);
    }

    [Test] public void AutoFix_BothBlocked_FixesArrivalThenDeparture()
    {
        var b = Building();
        Wall(b, 0, 0.8f, 0, 0.8f, 6);    // behind the bottom edge
        Wall(b, 1, 6.7f, 0, 6.7f, 6);    // in front of the top edge
        Wall(b, 1, 0, 5f, 6.7f, 5f);     // blocks a left arrival turn
        var s = Stair(b, P(1.5f, 3.5f), P(6, 3.5f));
        CollectionAssert.AreEqual(Issues(StairIssue.DepartureBlocked, StairIssue.ArrivalBlocked),
            StairValidator.Validate(b, s).issues);
        var original = new List<StairPoint>(s.route);

        Assert.IsTrue(StairAutoFix.TryFix(b, s, out var route));
        CollectionAssert.AreEqual(original, s.route);
        var after = ValidateRoute(b, s, route);
        Assert.IsTrue(after.Valid, string.Join(",", after.issues));
    }

    [Test] public void AutoFix_NoSolution_ReturnsFalse()
    {
        var b = Building();
        Wall(b, 1, 6.7f, 0, 6.7f, 6);
        Wall(b, 1, 0, 2f, 8, 2f);
        Wall(b, 1, 0, 4f, 8, 4f);
        var s = Stair(b, P(1.5f, 3), P(6, 3));
        CollectionAssert.AreEqual(Issues(StairIssue.ArrivalBlocked), StairValidator.Validate(b, s).issues);
        var original = new List<StairPoint>(s.route);
        Assert.IsFalse(StairAutoFix.TryFix(b, s, out _));
        CollectionAssert.AreEqual(original, s.route);
    }

    [Test] public void AutoFix_DepartureSymmetric()
    {
        var b = Building();
        Wall(b, 0, 0.8f, 0, 0.8f, 6);   // behind the bottom edge (departure x 0.5..1.5)
        Wall(b, 0, 0.8f, 2.4f, 8, 2.4f); // blocks a departure from -y; +y is free
        var s = Stair(b, P(1.5f, 3.5f), P(6, 3.5f));
        var before = StairValidator.Validate(b, s);
        CollectionAssert.AreEqual(Issues(StairIssue.DepartureBlocked), before.issues);
        var original = new List<StairPoint>(s.route);

        Assert.IsTrue(StairAutoFix.TryFix(b, s, out var route));
        CollectionAssert.AreEqual(original, s.route);
        var fixedStair = new StairData { id = s.id, fromFloorId = s.fromFloorId, toFloorId = s.toFloorId, width = s.width, route = route };
        var after = StairValidator.Validate(b, fixedStair);
        Assert.IsTrue(after.Valid, string.Join(",", after.issues));
        Assert.AreEqual(0f, Vector2.Dot(after.layout.startDir, before.layout.startDir), 1e-3f);
        Assert.Less(after.layout.startDir.y, -0.99f); // comes down from the free +y side
    }

    // ---------------------------------------------------------------- final review fixes

    static WallSegment SegmentAt(FloorData f, Vector2 a, Vector2 c) => f.segments.Find(sg =>
    {
        var na = f.GetNode(sg.startNodeId).position; var nb = f.GetNode(sg.endNodeId).position;
        return (na - a).magnitude < 0.01f && (nb - c).magnitude < 0.01f || (na - c).magnitude < 0.01f && (nb - a).magnitude < 0.01f;
    });

    /// <summary>A 0.9 m floor-level door centred at the plan point at on the wall a-c of the floor.</summary>
    static WallOpening Door(BuildingData b, int floor, Vector2 a, Vector2 c, Vector2 at)
    {
        var f = b.floors[floor];
        var seg = SegmentAt(f, a, c);
        Assert.IsNotNull(seg, "segment");
        float center = (at - f.GetNode(seg.startNodeId).position).magnitude;
        return b.AddOpening(f, seg.id, new WallOpening { type = OpeningType.Door, center = center, width = 0.9f, height = 2.1f, sill = 0f });
    }

    [Test] public void ArrivalZoneOverOwnHole_IsBlocked()
    {
        // Square spiral, 1.2 m wide: 9 treads +x, landing, 2 treads +y, landing, 1 tread -x, landing, 1 tread -y.
        // The stair ends at y = 2.62 heading -y; its arrival zone (y 1.42..2.62) lies over the first flight's
        // upper treads (x 2.55..3.75), which are part of the slab hole. No piece overlaps another.
        var b = Building();
        var s = b.AddStair(0, new List<StairPoint> { P(1.5f, 1.5f), P(4.75f, 1.5f), P(4.75f, 3.5f), P(3.15f, 3.5f), P(3.15f, 0.5f) });
        s.width = 1.2f;
        var v = StairValidator.Validate(b, s);
        Assert.IsTrue(v.layout.ok, v.layout.error);
        Assert.AreEqual(3, v.layout.pieces.FindAll(p => p.kind == StairPieceKind.Landing).Count);
        Assert.AreEqual(2.62f, v.layout.topA.y, 1e-3f);
        CollectionAssert.AreEqual(Issues(StairIssue.ArrivalBlocked), v.issues);
    }

    static BuildingData ThreeFloors()
    {
        var b = Building();
        b.AddFloor();
        return b;
    }

    [Test] public void StackedStairCore_UpperStairOverLowerHole_Collides()
    {
        var b = ThreeFloors();
        var lower = Stair(b, P(1.5f, 2), P(6, 2));                        // 0 -> 1
        var upper = b.AddStair(1, new List<StairPoint> { P(3.5f, 2), P(8, 2) }); // 1 -> 2, starts over the lower hole
        upper.width = 1f;
        var vl = StairValidator.Validate(b, lower);
        var vu = StairValidator.Validate(b, upper);
        Assert.IsNotNull(vl.layout.hole);
        // The lower stair arrives (x 5.98..6.98) onto the upper stair's treads.
        CollectionAssert.Contains(vl.issues, StairIssue.ArrivalBlocked);
        // The upper stair's treads stand over the lower stair's hole in floor 1's slab and its departure zone
        // (x 2.5..3.5) too.
        CollectionAssert.Contains(vu.issues, StairIssue.Collision);
        CollectionAssert.Contains(vu.issues, StairIssue.DepartureBlocked);
    }

    [Test] public void StackedStairCore_SideBySide_IsValid()
    {
        var b = ThreeFloors();
        var lower = Stair(b, P(1.5f, 1.5f), P(6, 1.5f));
        var upper = b.AddStair(1, new List<StairPoint> { P(1.5f, 4.5f), P(6, 4.5f) });
        upper.width = 1f;
        Assert.IsTrue(StairValidator.Validate(b, lower).Valid, string.Join(",", StairValidator.Validate(b, lower).issues));
        Assert.IsTrue(StairValidator.Validate(b, upper).Valid, string.Join(",", StairValidator.Validate(b, upper).issues));
    }

    [Test] public void TwoStairsWithOverlappingHoles()
    {
        var b = Building();
        var a = Stair(b, P(1.5f, 2), P(6, 2));
        var c = Stair(b, P(1.5f, 2.8f), P(6, 2.8f)); // 0.2 m of overlap across the width
        foreach (var s in new[] { a, c })
        {
            var v = StairValidator.Validate(b, s);
            Assert.IsTrue(v.layout.ok, v.layout.error);
            CollectionAssert.AreEqual(Issues(StairIssue.Collision, StairIssue.HoleCrossesWall), v.issues);
        }
    }

    [Test] public void StairAlongWallWithDoor_Collides()
    {
        var b = Building();
        var s = Stair(b, P(1.5f, 0.7f), P(6, 0.7f)); // y 0.2..1.2, along the south wall (face at y = 0.1)
        Assert.IsTrue(StairValidator.Validate(b, s).Valid, string.Join(",", StairValidator.Validate(b, s).issues));
        Door(b, 0, new Vector2(0, 0), new Vector2(8, 0), new Vector2(4, 0)); // x 3.55..4.45, clearance to y = 1.1
        CollectionAssert.AreEqual(Issues(StairIssue.Collision), StairValidator.Validate(b, s).issues);
    }

    [Test] public void DoorBesideDepartureZone_BlocksDeparture()
    {
        var b = Building();
        var s = Stair(b, P(2.2f, 0.7f), P(7, 0.7f)); // departure zone x 1.2..2.2, y 0.2..1.2; top at x = 6.68
        Assert.IsTrue(StairValidator.Validate(b, s).Valid, string.Join(",", StairValidator.Validate(b, s).issues));
        Door(b, 0, new Vector2(0, 0), new Vector2(8, 0), new Vector2(1.5f, 0)); // x 1.05..1.95
        CollectionAssert.AreEqual(Issues(StairIssue.DepartureBlocked), StairValidator.Validate(b, s).issues);
    }

    [Test] public void DoorInFrontOfArrival_BlocksArrival()
    {
        var b = Building();
        var s = Stair(b, P(1.5f, 3), P(6, 3)); // arrival zone x 5.98..6.98, y 2.5..3.5
        Assert.IsTrue(StairValidator.Validate(b, s).Valid, string.Join(",", StairValidator.Validate(b, s).issues));
        var door = Door(b, 1, new Vector2(8, 0), new Vector2(8, 6), new Vector2(8, 3)); // floor 1, clearance x 6.9..9.1
        CollectionAssert.AreEqual(Issues(StairIssue.ArrivalBlocked), StairValidator.Validate(b, s).issues);
        // A window (sill above the floor) never blocks.
        door.type = OpeningType.Window;
        door.sill = 0.9f;
        door.height = 1.2f;
        Assert.IsTrue(StairValidator.Validate(b, s).Valid, string.Join(",", StairValidator.Validate(b, s).issues));
    }

    [Test] public void HoleUnderNoUpperFloor_IsHoleOutsideFloor()
    {
        var b = new BuildingData();
        var f0 = b.AddFloor();
        b.AddFloor(); // empty: nothing to copy yet
        b.AddSegment(f0, new Vector2(0, 0), new Vector2(8, 0));
        b.AddSegment(f0, new Vector2(8, 0), new Vector2(8, 6));
        b.AddSegment(f0, new Vector2(8, 6), new Vector2(0, 6));
        b.AddSegment(f0, new Vector2(0, 6), new Vector2(0, 0));
        var s = Stair(b, P(1.5f, 2), P(6, 2));
        CollectionAssert.AreEqual(Issues(StairIssue.ArrivalBlocked, StairIssue.HoleOutsideFloor), StairValidator.Validate(b, s).issues);
    }

    [Test] public void AutoFix_OfferedOnlyForBlockedZones()
    {
        var v = new StairValidation();
        Assert.IsFalse(StairAutoFix.CanTry(v));
        v.issues.Add(StairIssue.Collision);
        Assert.IsFalse(StairAutoFix.CanTry(v));
        v.issues.Add(StairIssue.ArrivalBlocked);
        Assert.IsTrue(StairAutoFix.CanTry(v));
        v.issues.Clear();
        v.issues.Add(StairIssue.DepartureBlocked);
        Assert.IsTrue(StairAutoFix.CanTry(v));
        Assert.IsFalse(StairAutoFix.CanTry(null));
    }

    [Test] public void HoleMargin_IntoWall_IsHoleCrossesWall()
    {
        var b = Building();
        Wall(b, 1, 0, 2.85f, 8, 2.85f); // stair y 1.5..2.5; wall faces at ~2.75 / 2.95
        var s = Stair(b, P(1.5f, 2), P(6, 2));
        Assert.IsTrue(StairValidator.Validate(b, s).Valid, string.Join(",", StairValidator.Validate(b, s).issues));
        s.holeMargin = 0.4f;
        CollectionAssert.AreEqual(Issues(StairIssue.HoleCrossesWall), StairValidator.Validate(b, s).issues);
    }

    [Test] public void HoleMargin_UStair_IsValid()
    {
        // 16x12 building; U-stair with touching flights, margin 0.2.
        var b = new BuildingData();
        var f0 = b.AddFloor();
        b.AddSegment(f0, new Vector2(0, 0), new Vector2(16, 0));
        b.AddSegment(f0, new Vector2(16, 0), new Vector2(16, 12));
        b.AddSegment(f0, new Vector2(16, 12), new Vector2(0, 12));
        b.AddSegment(f0, new Vector2(0, 12), new Vector2(0, 0));
        b.AddFloor();
        var s = Stair(b, P(6, 3), P(6, 7), P(7, 7), P(7, 3));
        s.holeMargin = 0.2f;
        var v = StairValidator.Validate(b, s);
        Assert.IsTrue(v.Valid, string.Join(",", v.issues));
    }
}
