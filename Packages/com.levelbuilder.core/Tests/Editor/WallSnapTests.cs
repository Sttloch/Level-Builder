using NUnit.Framework;
using UnityEngine;
using LevelBuilder;
using LevelBuilder.Editor;
using LevelBuilder.Geometry;

public class WallSnapTests
{
    static FloorData Floor(params (string id, Vector2 pos)[] nodes)
    {
        var f = new FloorData();
        foreach (var n in nodes) f.nodes.Add(new WallNode { id = n.id, position = n.pos });
        return f;
    }

    static void Wall(FloorData f, string id, string a, string b, float thickness = 0.2f) =>
        f.segments.Add(new WallSegment { id = id, startNodeId = a, endNodeId = b, thickness = thickness });

    [Test] public void SnapsToNearbyNode()
    {
        var f = Floor(("a", new Vector2(1.13f, 0.77f)), ("b", new Vector2(5f, 5f)));
        var r = WallSnap.Snap(f, new Vector2(1.3f, 0.9f), 0.5f, out var kind, out var target);
        Assert.AreEqual(WallSnap.Kind.Node, kind);
        Assert.AreEqual("a", target);
        Assert.AreEqual(new Vector2(1.13f, 0.77f), r);
    }

    [Test] public void SnapsOntoWallAxis_AtGridCrossing()
    {
        var f = Floor(("a", new Vector2(0, 0)), ("b", new Vector2(6, 0)));
        Wall(f, "s", "a", "b");
        var r = WallSnap.Snap(f, new Vector2(2.1f, 0.18f), 0.5f, out var kind, out var target);
        Assert.AreEqual(WallSnap.Kind.Wall, kind);
        Assert.AreEqual("s", target);
        Assert.AreEqual(2.0f, r.x, 1e-4f);
        Assert.AreEqual(0f, r.y, 1e-4f);
    }

    [Test] public void SnapsOntoWallAxis_Projection_WhenNoGridNear()
    {
        var f = Floor(("a", new Vector2(0, 0.15f)), ("b", new Vector2(10, 2.15f)));
        Wall(f, "s", "a", "b");
        var p = new Vector2(2.2304f, 0.698f);
        var r = WallSnap.Snap(f, p, 0.5f, out var kind, out var target);
        Assert.AreEqual(WallSnap.Kind.Wall, kind);
        Assert.AreEqual("s", target);
        float d = Seg2D.DistanceToSegment(r, f.nodes[0].position, f.nodes[1].position, out _);
        Assert.AreEqual(0f, d, 1e-4f);
        // It is the orthogonal projection: the move from p is perpendicular to the wall.
        var dir = (f.nodes[1].position - f.nodes[0].position).normalized;
        Assert.AreEqual(0f, Vector2.Dot(r - p, dir), 1e-4f);
    }

    [Test] public void NodeBeatsWall()
    {
        var f = Floor(("a", new Vector2(0, 0)), ("b", new Vector2(6, 0)), ("c", new Vector2(2.2f, 0.2f)));
        Wall(f, "s", "a", "b");
        var r = WallSnap.Snap(f, new Vector2(2.1f, 0.1f), 0.5f, out var kind, out var target);
        Assert.AreEqual(WallSnap.Kind.Node, kind);
        Assert.AreEqual("c", target);
        Assert.AreEqual(new Vector2(2.2f, 0.2f), r);
    }

    [Test] public void FarPoint_FallsBackToGrid()
    {
        var f = Floor(("a", new Vector2(0, 0)), ("b", new Vector2(6, 0)));
        Wall(f, "s", "a", "b");
        var r = WallSnap.Snap(f, new Vector2(2.6f, 1.4f), 0.5f, out var kind, out var target);
        Assert.AreEqual(WallSnap.Kind.Grid, kind);
        Assert.IsNull(target);
        Assert.AreEqual(2.5f, r.x, 1e-5f);
        Assert.AreEqual(1.5f, r.y, 1e-5f);
    }

    [Test] public void IgnoresDraggedNodeAndItsWalls()
    {
        var f = Floor(("a", new Vector2(0, 0)), ("b", new Vector2(6, 0)));
        Wall(f, "s", "a", "b");
        // Near node b and its wall, but b is the dragged node: nothing to snap to, plain grid.
        var r = WallSnap.Snap(f, new Vector2(5.9f, 0.1f), 0.5f, out var kind, out var target, "b");
        Assert.AreEqual(WallSnap.Kind.Grid, kind);
        Assert.IsNull(target);
        Assert.AreEqual(6.0f, r.x, 1e-5f);
        Assert.AreEqual(0f, r.y, 1e-5f);
    }
}
