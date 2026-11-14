using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
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

    // The clone's OnEnable sees the copied (inherited) floors: it never destroys their meshes (owned by the
    // original) and replaces them with its own on the deferred editor regenerate.
    [UnityTest] public IEnumerator Instantiate_DoesNotDestroyOriginalMeshes()
    {
        Square();
        r.Regenerate();
        yield return null; // flush anything pending on the original (e.g. Reset re-skin) before cloning
        var clone = Object.Instantiate(go);
        try
        {
            var walls = go.transform.Find("Floor 0/Walls").GetComponent<MeshFilter>();
            Assert.IsTrue(walls.sharedMesh != null);
            Assert.Greater(walls.sharedMesh.vertexCount, 0);
            Assert.IsTrue(walls.GetComponent<MeshCollider>().sharedMesh != null);

            yield return null; // deferred regenerate of the clone
            yield return null;

            Assert.IsTrue(walls.sharedMesh != null);
            Assert.Greater(walls.sharedMesh.vertexCount, 0);
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

    int FloorZeroCount()
    {
        int count = 0;
        foreach (Transform t in go.transform) if (t.name == "Floor 0") count++;
        return count;
    }

    // SetActive(false -> true) re-runs OnEnable while the hierarchy is being activated: destroying the
    // existing generated children there is an error ("Cannot destroy GameObject while it is being
    // activated or deactivated") and used to duplicate floors. OnEnable must leave them alone and defer.
    [UnityTest] public IEnumerator ToggleActive_NoErrorNoDuplicates()
    {
        Square();
        r.Regenerate();
        var before = go.transform.Find("Floor 0").gameObject;
        go.SetActive(false);
        go.SetActive(true);
        Assert.IsTrue(before != null, "OnEnable destroyed generated children during activation");
        Assert.AreEqual(1, FloorZeroCount());
        yield return null; // let the deferred editor regenerate run
        yield return null;
        Assert.AreEqual(1, FloorZeroCount());
        var walls = go.transform.Find("Floor 0/Walls").GetComponent<MeshFilter>().sharedMesh;
        Assert.IsTrue(walls != null && walls.vertexCount > 0);
        LogAssert.NoUnexpectedReceived();
    }

    [Test] public void ToggleParentActive_ThenRegenerate_SingleFloor()
    {
        Square();
        r.Regenerate();
        var parent = new GameObject("Parent");
        try
        {
            go.transform.SetParent(parent.transform, false);
            var before = go.transform.Find("Floor 0").gameObject;
            parent.SetActive(false);
            parent.SetActive(true);
            Assert.IsTrue(before != null, "OnEnable destroyed generated children during activation");
            r.Regenerate();
            Assert.AreEqual(1, FloorZeroCount());
            LogAssert.NoUnexpectedReceived();
        }
        finally
        {
            go.transform.SetParent(null, false);
            Object.DestroyImmediate(parent);
        }
    }

    [Test] public void Building_IsSelectionBaseAndSingle()
    {
        Assert.IsTrue(System.Attribute.IsDefined(typeof(Building), typeof(SelectionBaseAttribute)));
        Assert.IsTrue(System.Attribute.IsDefined(typeof(Building), typeof(DisallowMultipleComponent)));
    }

    [Test] public void GeneratedFloors_HiddenInHierarchy()
    {
        Square();
        r.Regenerate();
        var floor = go.transform.Find("Floor 0");
        Assert.AreEqual(HideFlags.HideInHierarchy, floor.gameObject.hideFlags & HideFlags.HideInHierarchy);
    }

    [Test] public void GeneratedMeshes_HaveTangents()
    {
        Square();
        r.Regenerate();
        var mesh = go.transform.Find("Floor 0/Floor").GetComponent<MeshFilter>().sharedMesh;
        Assert.Greater(mesh.vertexCount, 0);
        Assert.AreEqual(mesh.vertexCount, mesh.tangents.Length);
    }

    [Test] public void Reset_AssignsPersistentDefaultMaterial()
    {
        r.defaultMaterial = null;
        typeof(BuildingRenderer).GetMethod("Reset", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Invoke(r, null);
        Assert.IsNotNull(r.defaultMaterial);
        Assert.IsTrue(EditorUtility.IsPersistent(r.defaultMaterial));
    }
}
