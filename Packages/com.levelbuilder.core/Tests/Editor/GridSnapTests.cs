using NUnit.Framework;
using UnityEngine;
using LevelBuilder.Editor;

public class GridSnapTests
{
    [Test] public void Snap_RoundsToGrid()
    {
        var p = GridSnap.Snap(new Vector2(1.26f, -0.24f), 0.5f);
        Assert.AreEqual(1.5f, p.x, 1e-5f);
        Assert.AreEqual(0f, p.y, 1e-5f);
    }

    [Test] public void Snap_NonPositiveGrid_ReturnsInput()
    {
        var p = GridSnap.Snap(new Vector2(1.26f, -0.24f), 0f);
        Assert.AreEqual(new Vector2(1.26f, -0.24f), p);
    }

    [Test] public void SnapAngle_RoundsTo15Degrees()
    {
        var from = new Vector2(0, 0);
        var to = new Vector2(1, 0.2f);
        var r = GridSnap.SnapAngle(from, to, 15f);
        float angle = Mathf.Atan2(r.y - from.y, r.x - from.x) * Mathf.Rad2Deg;
        Assert.AreEqual(15f, angle, 1e-3f);
        Assert.AreEqual((to - from).magnitude, (r - from).magnitude, 1e-5f);
    }

    [Test] public void SnapAngle_IsRelativeToFrom()
    {
        var from = new Vector2(2, 3);
        var r = GridSnap.SnapAngle(from, from + new Vector2(0.1f, 2f), 15f);
        Assert.AreEqual(from.x, r.x, 1e-4f);
        Assert.AreEqual(from.y + Mathf.Sqrt(0.01f + 4f), r.y, 1e-4f);
    }
}
