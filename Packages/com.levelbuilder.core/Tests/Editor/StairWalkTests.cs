using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LevelBuilder;
using LevelBuilder.Geometry;

// A default CharacterController (height 2, radius 0.5, skin 0.08, step 0.3, slope 45°) walks a default stair
// (AddStair: width 1.2) from the departure zone of floor 0 onto floor 1 beyond the top edge. Building 12x6,
// stair along +x from (2, 3).
public class StairWalkTests
{
    GameObject go, player;
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
        if (player != null) Object.DestroyImmediate(player);
        if (go != null) Object.DestroyImmediate(go);
    }

    static StairPoint P(float x, float y) => new StairPoint { position = new Vector2(x, y), kind = StairPointKind.Corner };

    StairData Setup(float height, bool ramp)
    {
        var f0 = b.data.AddFloor();
        b.data.AddSegment(f0, new Vector2(0, 0), new Vector2(12, 0));
        b.data.AddSegment(f0, new Vector2(12, 0), new Vector2(12, 6));
        b.data.AddSegment(f0, new Vector2(12, 6), new Vector2(0, 6));
        b.data.AddSegment(f0, new Vector2(0, 6), new Vector2(0, 0));
        b.data.AddFloor();
        b.data.SetFloorHeight(0, height);
        var s = b.data.AddStair(0, new List<StairPoint> { P(2, 3), P(9, 3) });
        s.collisionRamp = ramp;
        r.Regenerate();
        var v = r.StairValidations[s.id];
        Assert.IsTrue(v.Valid, string.Join(",", v.issues));
        return s;
    }

    // Without the ramp the controller climbs the steps with its step offset (it first moves up 0.3 m), so near the
    // hole edge it needs StairLayout.StepAllowance more headroom. On a 2.7 m floor that up move is already blocked by
    // the slab above floor 0 just before the first step, and the hole cannot extend beyond the stair: the
    // combination (2.7 m, ramp off) is not supported, hence no such case.
    [TestCase(2.7f, true)]
    [TestCase(3.0f, true)]
    [TestCase(3.3f, true)]
    [TestCase(3.0f, false)]
    [TestCase(3.3f, false)]
    public void DefaultCharacterController_ClimbsDefaultStair(float height, bool ramp)
    {
        var s = Setup(height, ramp);
        var l = r.StairValidations[s.id].layout;
        var to = b.data.floors[1];

        player = new GameObject("Player");
        var cc = player.AddComponent<CharacterController>();
        var bottom = (l.bottomA + l.bottomB) * 0.5f - l.startDir * 0.6f; // middle of the departure zone
        player.transform.position = new Vector3(bottom.x, b.data.floors[0].elevation + cc.height * 0.5f + cc.skinWidth + 0.02f, bottom.y);
        Physics.SyncTransforms();

        var dir = new Vector3(l.endDir.x, 0f, l.endDir.y);
        var top = (l.topA + l.topB) * 0.5f;
        for (int i = 0; i < 240; i++) cc.Move(dir * 0.05f + Vector3.down * 0.05f);

        var p = player.transform.position;
        float past = Vector2.Dot(new Vector2(p.x, p.z) - top, l.endDir);
        Assert.Greater(past, cc.radius, $"stopped {past:0.00} m from the top edge at {p}");
        Assert.AreEqual(to.elevation + cc.height * 0.5f + cc.skinWidth, p.y, 0.1f, "on floor 1");
    }
}
