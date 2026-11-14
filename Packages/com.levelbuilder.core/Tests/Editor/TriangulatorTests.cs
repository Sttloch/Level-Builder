using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LevelBuilder.Geometry;

public class TriangulatorTests
{
    static float TriangleArea(List<int> tris, List<Vector2> v, out bool allCcw)
    {
        float sum = 0f;
        allCcw = true;
        for (int i = 0; i < tris.Count; i += 3)
        {
            var a = v[tris[i]]; var b = v[tris[i + 1]]; var c = v[tris[i + 2]];
            float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (cross < -1e-6f) allCcw = false;
            sum += Mathf.Abs(cross) * 0.5f;
        }
        return sum;
    }

    static List<Vector2> Rect(float x0, float y0, float x1, float y1) => new List<Vector2>
    {
        new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1)
    };

    [Test] public void Square_TwoTriangles_AreaPreserved()
    {
        var tris = Triangulator.Triangulate(Rect(0, 0, 4, 4), null, out var verts);
        Assert.AreEqual(6, tris.Count);
        Assert.AreEqual(16f, TriangleArea(tris, verts, out bool ccw), 1e-4f);
        Assert.IsTrue(ccw);
    }

    [Test] public void SquareWithHole_AreaIsOuterMinusHole()
    {
        var holes = new List<IList<Vector2>> { Rect(1, 1, 2, 2) };
        var tris = Triangulator.Triangulate(Rect(0, 0, 4, 4), holes, out var verts);
        Assert.AreEqual(15f, TriangleArea(tris, verts, out bool ccw), 1e-4f);
        Assert.IsTrue(ccw);
        // No triangle covers the hole centre.
        var p = new Vector2(1.5f, 1.5f);
        for (int i = 0; i < tris.Count; i += 3)
            Assert.IsFalse(Inside(p, verts[tris[i]], verts[tris[i + 1]], verts[tris[i + 2]]), "triangle covers the hole");
    }

    [Test] public void Clockwise_Input_StillWorks()
    {
        var cw = Rect(0, 0, 4, 4);
        cw.Reverse();
        var hole = Rect(1, 1, 2, 2); // CCW hole, must be normalised to CW
        var tris = Triangulator.Triangulate(cw, new List<IList<Vector2>> { hole }, out var verts);
        Assert.AreEqual(15f, TriangleArea(tris, verts, out bool ccw), 1e-4f);
        Assert.IsTrue(ccw);
    }

    [Test] public void ConcaveL_AreaPreserved()
    {
        var l = new List<Vector2>
        {
            new Vector2(0, 0), new Vector2(4, 0), new Vector2(4, 1),
            new Vector2(1, 1), new Vector2(1, 4), new Vector2(0, 4)
        };
        var tris = Triangulator.Triangulate(l, null, out var verts);
        Assert.AreEqual(12, tris.Count);
        Assert.AreEqual(7f, TriangleArea(tris, verts, out bool ccw), 1e-4f);
        Assert.IsTrue(ccw);
    }

    static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
        float d2 = (c.x - b.x) * (p.y - b.y) - (c.y - b.y) * (p.x - b.x);
        float d3 = (a.x - c.x) * (p.y - c.y) - (a.y - c.y) * (p.x - c.x);
        return d1 > 1e-6f && d2 > 1e-6f && d3 > 1e-6f;
    }
}
