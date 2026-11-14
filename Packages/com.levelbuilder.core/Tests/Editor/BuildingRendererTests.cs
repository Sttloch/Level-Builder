using System.Linq;
using NUnit.Framework;
using UnityEngine;
using LevelBuilder;

public class BuildingRendererTests
{
    GameObject go;
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
        if (go != null) Object.DestroyImmediate(go);
    }

    FloorData Square()
    {
        var f = b.data.AddFloor();
        b.data.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
        b.data.AddSegment(f, new Vector2(4, 0), new Vector2(4, 4));
        b.data.AddSegment(f, new Vector2(4, 4), new Vector2(0, 4));
        b.data.AddSegment(f, new Vector2(0, 4), new Vector2(0, 0));
        return f;
    }

    [Test] public void Regenerate_CreatesFloorHierarchyWithColliders()
    {
        Square();
        r.Regenerate();
        var walls = go.transform.Find("Floor 0/Walls");
        Assert.IsNotNull(walls);
        Assert.IsNotNull(walls.GetComponent<MeshFilter>().sharedMesh);
        Assert.IsNotNull(walls.GetComponent<MeshRenderer>());
        Assert.IsNotNull(walls.GetComponent<MeshCollider>());
        Assert.IsNotNull(go.transform.Find("Floor 0/Floor"));
        Assert.IsNotNull(go.transform.Find("Floor 0/Ceiling"));
    }

    [Test] public void GeneratedObjects_AreDontSave()
    {
        Square();
        r.Regenerate();
        var floor = go.transform.Find("Floor 0");
        Assert.AreEqual(HideFlags.DontSave, floor.gameObject.hideFlags & HideFlags.DontSave);
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            Assert.AreEqual(HideFlags.DontSave, mf.gameObject.hideFlags & HideFlags.DontSave);
            Assert.AreEqual(HideFlags.DontSave, mf.sharedMesh.hideFlags & HideFlags.DontSave);
        }
    }

    [Test] public void MoveNode_ThenRegenerate_UpdatesBounds()
    {
        var f = Square();
        r.Regenerate();
        var walls = go.transform.Find("Floor 0/Walls").GetComponent<MeshFilter>();
        Assert.AreEqual(4.1f, walls.sharedMesh.bounds.max.x, 0.2f);
        var n = f.nodes.First(x => (x.position - new Vector2(4, 0)).magnitude < 0.01f);
        b.data.MoveNode(f, n.id, new Vector2(6, 0));
        r.Regenerate();
        walls = go.transform.Find("Floor 0/Walls").GetComponent<MeshFilter>();
        Assert.AreEqual(6.1f, walls.sharedMesh.bounds.max.x, 0.1f);
    }

    [Test] public void Regenerate_Twice_DoesNotDuplicateChildren()
    {
        Square();
        r.Regenerate();
        r.Regenerate();
        r.Regenerate(0);
        int count = 0;
        foreach (Transform t in go.transform) if (t.name == "Floor 0") count++;
        Assert.AreEqual(1, count);
    }

    [Test] public void Instantiate_DoesNotDestroyOriginalMeshes()
    {
        Square();
        r.Regenerate();
        var clone = Object.Instantiate(go);
        try
        {
            var walls = go.transform.Find("Floor 0/Walls").GetComponent<MeshFilter>();
            Assert.IsTrue(walls.sharedMesh != null);
            Assert.Greater(walls.sharedMesh.vertexCount, 0);
            Assert.IsTrue(walls.GetComponent<MeshCollider>().sharedMesh != null);

            int count = 0;
            foreach (Transform t in clone.transform)
                if (t.name == "Floor 0") { count++; Assert.AreEqual(HideFlags.DontSave, t.gameObject.hideFlags & HideFlags.DontSave); }
            Assert.AreEqual(1, count);
            var cloneWalls = clone.transform.Find("Floor 0/Walls").GetComponent<MeshFilter>();
            Assert.AreNotSame(walls.sharedMesh, cloneWalls.sharedMesh);
        }
        finally { Object.DestroyImmediate(clone); }
    }

    [Test] public void UserChildNamedFloorLamp_SurvivesRegenerate()
    {
        Square();
        var lamp = new GameObject("Floor Lamp");
        lamp.transform.SetParent(go.transform, false);
        r.Regenerate();
        r.Regenerate(0);
        Assert.IsTrue(lamp != null);
    }

    [Test] public void DestroyingRenderer_RemovesGeneratedMeshesAndChildren()
    {
        Square();
        r.Regenerate();
        var mesh = go.transform.Find("Floor 0/Walls").GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(r);
        Assert.IsTrue(mesh == null);
        Assert.IsNull(go.transform.Find("Floor 0"));
    }

    // Ownership lives on the generated objects (marker), not in renderer memory, so it survives a domain reload.
    [Test] public void Regenerate_DestroysPreviousMeshesWithoutInMemoryBookkeeping()
    {
        Square();
        r.Regenerate();
        var prev = go.transform.Find("Floor 0/Walls").GetComponent<MeshFilter>().sharedMesh;
        var prevFloor = go.transform.Find("Floor 0/Floor").GetComponent<MeshFilter>().sharedMesh;
        r.Regenerate();
        Assert.IsTrue(prev == null);
        Assert.IsTrue(prevFloor == null);
        Object.DestroyImmediate(r);
        Assert.IsNull(go.transform.Find("Floor 0"));
    }

    [Test] public void RemoveLinkedNode_Regenerate_NoThrow()
    {
        var f0 = Square();
        b.data.AddFloor();
        r.Regenerate();
        b.data.RemoveNode(f0, f0.nodes[0].id);
        Assert.DoesNotThrow(() => r.Regenerate());
        Assert.DoesNotThrow(() => r.Regenerate(0));
    }
}
