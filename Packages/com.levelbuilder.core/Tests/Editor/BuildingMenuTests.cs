using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using LevelBuilder;
using LevelBuilder.Editor;

public class BuildingMenuTests
{
    GameObject go;

    [TearDown] public void TearDown()
    {
        if (go != null) Object.DestroyImmediate(go);
    }

    [Test] public void CreateBuilding_HasComponentsAndOneFloor()
    {
        go = BuildingMenu.CreateBuilding(null);
        var b = go.GetComponent<Building>();
        Assert.IsNotNull(b);
        Assert.IsNotNull(go.GetComponent<BuildingRenderer>());
        Assert.AreEqual(1, b.data.floors.Count);
        Assert.AreEqual(0, b.activeFloor);
    }

    [Test] public void CreateBuilding_ParentsUnderContext()
    {
        var parent = new GameObject("Parent");
        try
        {
            go = BuildingMenu.CreateBuilding(parent);
            Assert.AreSame(parent.transform, go.transform.parent);
        }
        finally { Object.DestroyImmediate(parent); go = null; }
    }

    [Test] public void CreateBuilding_HasPersistentDefaultMaterial()
    {
        go = BuildingMenu.CreateBuilding(null);
        var r = go.GetComponent<BuildingRenderer>();
        Assert.IsNotNull(r.defaultMaterial);
        Assert.IsTrue(EditorUtility.IsPersistent(r.defaultMaterial));
    }

    // Switching the active floor is view state: it must not create Undo entries.
    [Test] public void SetActiveFloor_DoesNotRecordUndo()
    {
        go = BuildingMenu.CreateBuilding(null);
        var b = go.GetComponent<Building>();
        b.data.AddFloor();
        b.activeFloor = 0;
        Undo.ClearAll();
        typeof(BuildingOverlay).GetMethod("SetActiveFloor", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { b, 1 });
        Assert.AreEqual(1, b.activeFloor);
        Undo.PerformUndo();
        Assert.AreEqual(1, b.activeFloor);
    }
}
