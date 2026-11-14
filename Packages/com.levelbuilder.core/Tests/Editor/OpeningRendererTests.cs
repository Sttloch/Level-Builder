using System.Linq;
using NUnit.Framework;
using UnityEngine;
using LevelBuilder;

public class OpeningRendererTests
{
    GameObject go;
    BuildingRenderer r;
    Building b;
    GameObject prefab;
    Mesh prefabMesh;

    [SetUp] public void SetUp()
    {
        go = new GameObject("TestBuilding");
        r = go.AddComponent<BuildingRenderer>();
        b = go.GetComponent<Building>();
    }

    [TearDown] public void TearDown()
    {
        if (go != null) Object.DestroyImmediate(go);
        if (prefab != null) Object.DestroyImmediate(prefab);
        if (prefabMesh != null) Object.DestroyImmediate(prefabMesh);
    }

    // 4 x 4 square; returns the south wall (0,0) -> (4,0).
    WallSegment Square(out FloorData f)
    {
        f = b.data.AddFloor();
        var south = b.data.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0))[0];
        b.data.AddSegment(f, new Vector2(4, 0), new Vector2(4, 4));
        b.data.AddSegment(f, new Vector2(4, 4), new Vector2(0, 4));
        b.data.AddSegment(f, new Vector2(0, 4), new Vector2(0, 0));
        return south;
    }

    WallOpening AddDoor(FloorData f, WallSegment s, float center = 2f) =>
        b.data.AddOpening(f, s.id, new WallOpening { type = OpeningType.Door, center = center, width = 0.9f, height = 2.1f });

    WallOpening AddWindow(FloorData f, WallSegment s, float center = 2f) =>
        b.data.AddOpening(f, s.id, new WallOpening { type = OpeningType.Window, center = center, width = 1.2f, height = 1.2f, sill = 0.9f });

    [Test] public void Door_CreatesLeafWithDoorComponent()
    {
        var s = Square(out var f);
        var o = AddDoor(f, s);
        r.Regenerate();
        var t = go.transform.Find("Floor 0/Doors/" + o.id);
        Assert.IsNotNull(t, "door object missing");
        var door = t.GetComponent<Door>();
        Assert.IsNotNull(door);
        // Base-centre of the opening, forward = /R face normal (south wall: -Z).
        Assert.AreEqual(0f, (t.position - new Vector3(2f, 0f, 0f)).magnitude, 1e-4f);
        Assert.AreEqual(0f, (t.forward - new Vector3(0f, 0f, -1f)).magnitude, 1e-4f);

        Assert.IsNotNull(door.leaf);
        Assert.AreEqual("Leaf", door.leaf.name);
        Assert.AreSame(t, door.leaf.parent);
        var box = door.leaf.GetComponent<BoxCollider>();
        Assert.IsNotNull(box);
        Assert.IsFalse(box.isTrigger);
        var mesh = door.leaf.GetComponent<MeshFilter>().sharedMesh;
        var size = mesh.bounds.size;
        Assert.AreEqual(0.88f, size.x, 1e-3f);
        Assert.AreEqual(2.09f, size.y, 1e-3f);
        Assert.AreEqual(0.04f, size.z, 1e-3f);
        Assert.AreEqual(0f, mesh.bounds.min.y, 1e-4f);
        Assert.AreEqual(0.88f, door.leafSize.x, 1e-3f);
        Assert.AreEqual(2.09f, door.leafSize.y, 1e-3f);
        Assert.AreEqual(0.04f, box.size.z, 1e-3f);
        var rb = door.leaf.GetComponent<Rigidbody>();
        Assert.IsNotNull(rb);
        Assert.IsTrue(rb.isKinematic);
        Assert.AreEqual(HideFlags.DontSave, t.gameObject.hideFlags & HideFlags.DontSave);
        Assert.AreEqual(HideFlags.DontSave, door.leaf.gameObject.hideFlags & HideFlags.DontSave);
    }

    [Test] public void DoorSettings_AreCopiedNotShared()
    {
        var s = Square(out var f);
        var o = AddDoor(f, s);
        o.door.openAngle = 70f;
        o.door.activation = DoorActivation.Script;
        r.Regenerate();
        var door = go.transform.Find("Floor 0/Doors/" + o.id).GetComponent<Door>();
        Assert.AreEqual(70f, door.settings.openAngle);
        Assert.AreEqual(DoorActivation.Script, door.settings.activation);
        Assert.AreNotSame(o.door, door.settings);
        Assert.AreNotSame(o.door.onOpened, door.settings.onOpened);
    }

    [Test] public void InvalidOpening_CreatesNothing()
    {
        var s = Square(out var f);
        var o = AddDoor(f, s, 0.2f); // into the corner joint
        r.Regenerate();
        Assert.IsNull(go.transform.Find("Floor 0/Doors/" + o.id));
        Assert.AreEqual(0, go.GetComponentsInChildren<Door>(true).Length);
    }

    [Test] public void Window_CreatesFrameAndGlass()
    {
        var s = Square(out var f);
        var o = AddWindow(f, s);
        r.Regenerate();
        var t = go.transform.Find("Floor 0/Windows/" + o.id);
        Assert.IsNotNull(t, "window object missing");
        Assert.IsNull(t.GetComponent<Door>());
        var mrs = t.GetComponentsInChildren<MeshRenderer>(true);
        Assert.AreEqual(2, mrs.Length);
        Assert.IsTrue(mrs.Any(m => m.sharedMaterial != null && m.sharedMaterial.renderQueue >= 3000), "no transparent glass");
        // Spans v in [sill, sill + height], centred in the wall thickness.
        var bounds = mrs.Select(m => m.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
        Assert.AreEqual(0.9f, bounds.min.y, 1e-3f);
        Assert.AreEqual(2.1f, bounds.max.y, 1e-3f);
        Assert.AreEqual(1.2f, bounds.size.x, 1e-3f);
        Assert.AreEqual(0f, bounds.center.z, 1e-3f);
        var glass = mrs.First(m => m.sharedMaterial.renderQueue >= 3000);
        Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, glass.shadowCastingMode);
    }

    // Instantiate keeps the prefab root's rotation (e.g. FBX roots at (-90,0,0)): the fit must still be done
    // on the opening's axes. The child box is deliberately non-uniform so a wrong axis shows up.
    [TestCase(90f, 0f)]
    [TestCase(0f, 90f)]
    public void RotatedPrefabRoot_IsFittedOnOpeningAxes(float rotX, float rotY)
    {
        prefab = new GameObject("RotatedLeaf");
        prefab.transform.localRotation = Quaternion.Euler(rotX, rotY, 0f);
        var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
        child.transform.SetParent(prefab.transform, false);
        child.transform.localScale = new Vector3(0.5f, 3f, 0.1f);
        var mf = child.GetComponent<MeshFilter>();
        prefabMesh = Object.Instantiate(mf.sharedMesh);
        prefabMesh.hideFlags = HideFlags.None;
        mf.sharedMesh = prefabMesh;

        var s = Square(out var f);
        var o = AddDoor(f, s);
        o.door.leafPrefab = prefab;
        r.Regenerate();

        var door = go.transform.Find("Floor 0/Doors/" + o.id).GetComponent<Door>();
        var inst = go.GetComponentsInChildren<MeshFilter>(true).Single(m => m.sharedMesh == prefabMesh);
        var bounds = inst.GetComponent<MeshRenderer>().bounds;
        Assert.AreEqual(0.88f, bounds.size.x, 1e-3f);
        Assert.AreEqual(2.09f, bounds.size.y, 1e-3f);
        Assert.AreEqual(0f, bounds.min.y, 1e-3f);
        Assert.AreEqual(2f, bounds.center.x, 1e-3f);
        Assert.AreEqual(0f, bounds.center.z, 1e-3f);
        Assert.AreEqual(0.88f, door.leafSize.x, 1e-3f);
        Assert.AreEqual(2.09f, door.leafSize.y, 1e-3f);
        var box = door.leaf.GetComponent<BoxCollider>();
        Assert.AreEqual(0.88f, box.size.x, 1e-3f);
        Assert.AreEqual(2.09f, box.size.y, 1e-3f);
        Assert.AreEqual(bounds.size.z, box.size.z, 1e-3f);
    }

    [Test] public void CustomLeafPrefab_IsFittedAndItsAssetsSurviveRegenerate()
    {
        prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        prefab.name = "LeafPrefab";
        var mf = prefab.GetComponent<MeshFilter>();
        prefabMesh = Object.Instantiate(mf.sharedMesh);
        prefabMesh.hideFlags = HideFlags.None; // like an asset
        mf.sharedMesh = prefabMesh;

        var s = Square(out var f);
        var o = AddDoor(f, s);
        o.door.leafPrefab = prefab;
        r.Regenerate();
        r.Regenerate();

        Assert.IsTrue(prefabMesh != null, "the prefab's mesh was destroyed");
        var instances = go.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh == prefabMesh).ToArray();
        Assert.AreEqual(1, instances.Length);
        var door = go.transform.Find("Floor 0/Doors/" + o.id).GetComponent<Door>();
        Assert.AreEqual("Leaf", door.leaf.name);
        Assert.AreSame(door.transform, door.leaf.parent);
        Assert.IsTrue(instances[0].transform.IsChildOf(door.leaf));
        Assert.AreEqual(HideFlags.DontSave, instances[0].gameObject.hideFlags & HideFlags.DontSave);
        var bounds = instances[0].GetComponent<MeshRenderer>().bounds;
        Assert.AreEqual(0.88f, bounds.size.x, 1e-3f);
        Assert.AreEqual(2.09f, bounds.size.y, 1e-3f);
        Assert.AreEqual(0f, bounds.min.y, 1e-3f);
        Assert.AreEqual(2f, bounds.center.x, 1e-3f);
        Assert.AreEqual(0f, bounds.center.z, 1e-3f);
    }

    [Test] public void DoorEvents_ForwardToOpeningById()
    {
        var s = Square(out var f);
        var o = AddDoor(f, s);
        r.Regenerate();
        int original = 0, copied = 0;
        o.door.onOpened.AddListener(() => original++);

        // Replace the data with a copy (as Undo does): the door generated before must look the opening up by id.
        b.data = JsonUtility.FromJson<BuildingData>(JsonUtility.ToJson(b.data));
        var (_, copy) = b.data.FindOpening(b.data.floors[0], o.id);
        Assert.IsNotNull(copy);
        Assert.AreNotSame(o, copy);
        copy.door.onOpened.AddListener(() => copied++);

        var door = go.transform.Find("Floor 0/Doors/" + o.id).GetComponent<Door>();
        door.Open();
        door.Tick(10f);
        Assert.AreEqual(1, copied);
        Assert.AreEqual(0, original);

        r.Regenerate();
        door = go.transform.Find("Floor 0/Doors/" + o.id).GetComponent<Door>();
        door.Open();
        door.Tick(10f);
        Assert.AreEqual(2, copied);
        Assert.AreEqual(0, original);
    }

    [Test] public void FindDoor_ReturnsGeneratedDoorOrNull()
    {
        var s = Square(out var f);
        var o = AddDoor(f, s, 1.2f);
        var w = AddWindow(f, s, 3f);
        var bad = AddDoor(f, b.data.floors[0].segments[1], 0.1f); // into the corner joint: invalid
        r.Regenerate();

        var door = r.FindDoor(o.id);
        Assert.IsNotNull(door);
        Assert.AreSame(go.transform.Find("Floor 0/Doors/" + o.id).GetComponent<Door>(), door);
        Assert.AreSame(door, b.FindDoor(o.id));
        Assert.IsNull(r.FindDoor(w.id), "a window is not a door");
        Assert.IsNull(r.FindDoor(bad.id), "invalid openings generate nothing");
        Assert.IsNull(r.FindDoor("unknown"));
        Assert.IsNull(r.FindDoor(null));
        Assert.IsNull(r.FindDoor(""));

        r.Regenerate();
        var again = r.FindDoor(o.id);
        Assert.IsNotNull(again);
        Assert.AreNotSame(door, again);
        Assert.IsTrue(door == null, "the previous door was destroyed");
    }

    // South wall (0,0)->(4,0): its R face looks to -Z, so the R-side viewer stands at z < 0 looking toward +Z and
    // has +X on their right. hingeOnRight -> the leaf edge that stays put while opening is at world x > 2.
    [TestCase(true)]
    [TestCase(false)]
    public void HingeOnRight_IsRightOfViewerOnRFace(bool right)
    {
        var s = Square(out var f);
        var o = AddDoor(f, s);
        o.door.hingeOnRight = right;
        o.door.bothWays = false;
        r.Regenerate();
        var door = go.transform.Find("Floor 0/Doors/" + o.id).GetComponent<Door>();
        float half = door.leafSize.x * 0.5f;
        var e0 = door.leaf.TransformPoint(new Vector3(-half, 0f, 0f));
        var e1 = door.leaf.TransformPoint(new Vector3(half, 0f, 0f));
        door.Open();
        door.Tick(10f);
        bool e0Fixed = Vector3.Distance(e0, door.leaf.TransformPoint(new Vector3(-half, 0f, 0f))) < 1e-4f;
        bool e1Fixed = Vector3.Distance(e1, door.leaf.TransformPoint(new Vector3(half, 0f, 0f))) < 1e-4f;
        Assert.AreNotEqual(e0Fixed, e1Fixed, "exactly one edge is the hinge");
        var hinge = e0Fixed ? e0 : e1;
        if (right) Assert.Greater(hinge.x, 2.3f);
        else Assert.Less(hinge.x, 1.7f);
        // The opening tool's hinge marker sits on the same edge (u = x on this wall; the leaf is 1 cm inset).
        Assert.AreEqual(LevelBuilder.Editor.OpeningPlacement.HingeU(o), hinge.x, 0.02f);
    }

    static int DontSaveMeshes() =>
        Resources.FindObjectsOfTypeAll<Mesh>().Count(m => (m.hideFlags & HideFlags.DontSave) == HideFlags.DontSave);

    static int DontSaveMaterials() =>
        Resources.FindObjectsOfTypeAll<Material>().Count(m => (m.hideFlags & HideFlags.DontSave) == HideFlags.DontSave);

    [Test] public void Regenerate_NoLeakedMeshes()
    {
        var s = Square(out var f);
        AddDoor(f, s, 1f);
        AddWindow(f, s, 3f);
        r.Regenerate();
        int meshes = DontSaveMeshes(), materials = DontSaveMaterials();
        r.Regenerate();
        r.Regenerate(0);
        r.Regenerate();
        Assert.AreEqual(meshes, DontSaveMeshes());
        Assert.AreEqual(materials, DontSaveMaterials());
    }
}
