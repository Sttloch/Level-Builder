using NUnit.Framework;
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
}
