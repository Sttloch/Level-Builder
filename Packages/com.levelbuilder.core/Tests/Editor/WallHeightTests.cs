using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using LevelBuilder;
using LevelBuilder.Editor;

public class WallHeightTests
{
    GameObject go;

    [TearDown] public void TearDown()
    {
        if (go != null) Object.DestroyImmediate(go);
    }

    [Test] public void SnapHeight_StepsOfTenCm()
    {
        Assert.AreEqual(1.3f, WallTool.SnapHeight(1.34f, 2.7f, false), 1e-5f);
        Assert.AreEqual(1.4f, WallTool.SnapHeight(1.36f, 2.7f, false), 1e-5f);
        Assert.AreEqual(1.34f, WallTool.SnapHeight(1.34f, 2.7f, true), 1e-5f);
    }

    [Test] public void SnapHeight_ClampsToFloor()
    {
        Assert.AreEqual(2.7f, WallTool.SnapHeight(5f, 2.7f, false), 1e-5f);
        Assert.AreEqual(2.7f, WallTool.SnapHeight(5f, 2.7f, true), 1e-5f);
        Assert.AreEqual(0f, WallTool.SnapHeight(-1f, 2.7f, false), 1e-5f);
        Assert.AreEqual(0f, WallTool.SnapHeight(-1f, 2.7f, true), 1e-5f);
    }

    [Test] public void FullHeight_StoresMinusOne()
    {
        var seg = new WallSegment { heightOverride = 1f };
        WallTool.ApplyHeight(seg, 2.7f, 2.7f);
        Assert.AreEqual(-1f, seg.heightOverride);
        WallTool.ApplyHeight(seg, 1.2f, 2.7f);
        Assert.AreEqual(1.2f, seg.heightOverride, 1e-5f);
        WallTool.ApplyHeight(seg, 2.695f, 2.7f);
        Assert.AreEqual(-1f, seg.heightOverride);
        WallTool.ApplyHeight(seg, 0f, 2.7f);
        Assert.AreEqual(0f, seg.heightOverride);
    }

    [Test] public void HeightDrag_IsOneUndoStep()
    {
        go = BuildingMenu.CreateBuilding(null);
        var b = go.GetComponent<Building>();
        var f = b.data.floors.Count > 0 ? b.data.floors[0] : b.data.AddFloor();
        string segId = b.data.AddSegment(f, new Vector2(0, 0), new Vector2(6, 0))[0].id;
        Undo.ClearAll();
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Altura de pared");
        int group = Undo.GetCurrentGroup();

        foreach (float h in new[] { 2f, 1.5f, 1f })
            BuildingEditorUtil.Mutate(b, "Altura de pared", 0, () =>
            {
                var fl = b.data.floors[0];
                WallTool.ApplyHeight(fl.GetSegment(segId), h, fl.height);
            });
        Undo.CollapseUndoOperations(group);
        Assert.AreEqual(1f, b.data.floors[0].GetSegment(segId).heightOverride, 1e-5f);

        Undo.PerformUndo();
        Assert.AreEqual(-1f, b.data.floors[0].GetSegment(segId).heightOverride);
        Undo.PerformRedo();
        Assert.AreEqual(1f, b.data.floors[0].GetSegment(segId).heightOverride, 1e-5f);
    }
}
