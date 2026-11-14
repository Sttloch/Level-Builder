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

    static StairData Stair(BuildingData b, params StairPoint[] pts) => b.AddStair(0, new List<StairPoint>(pts));

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
        var fixedStair = new StairData { id = s.id, fromFloorId = s.fromFloorId, toFloorId = s.toFloorId, route = route };
        var after = StairValidator.Validate(b, fixedStair);
        Assert.IsTrue(after.Valid, string.Join(",", after.issues));
        Assert.AreEqual(0f, Vector2.Dot(after.layout.endDir, before.layout.endDir), 1e-3f);
        Assert.Less(after.layout.endDir.y, -0.99f); // right of +x is -y
    }

    static StairValidation ValidateRoute(BuildingData b, StairData s, List<StairPoint> route) =>
        StairValidator.Validate(b, new StairData { id = s.id, fromFloorId = s.fromFloorId, toFloorId = s.toFloorId, route = route });

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
        var fixedStair = new StairData { id = s.id, fromFloorId = s.fromFloorId, toFloorId = s.toFloorId, route = route };
        var after = StairValidator.Validate(b, fixedStair);
        Assert.IsTrue(after.Valid, string.Join(",", after.issues));
        Assert.AreEqual(0f, Vector2.Dot(after.layout.startDir, before.layout.startDir), 1e-3f);
        Assert.Less(after.layout.startDir.y, -0.99f); // comes down from the free +y side
    }
}
