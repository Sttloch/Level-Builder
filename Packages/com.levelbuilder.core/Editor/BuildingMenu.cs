using UnityEditor;
using UnityEngine;

namespace LevelBuilder.Editor
{
    public static class BuildingMenu
    {
        [MenuItem("GameObject/Level Builder/Building", false, 10)]
        static void CreateFromMenu(MenuCommand command)
        {
            var go = CreateBuilding(command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create Building");
            Selection.activeGameObject = go;
        }

        /// <summary>Creates a GameObject with Building + BuildingRenderer and one floor. Undo registration is the caller's.</summary>
        public static GameObject CreateBuilding(GameObject parent)
        {
            var go = new GameObject("Building");
            GameObjectUtility.SetParentAndAlign(go, parent);
            GameObjectUtility.EnsureUniqueNameForSibling(go);
            var b = go.AddComponent<Building>();
            b.data.AddFloor();
            b.activeFloor = 0;
            go.AddComponent<BuildingRenderer>(); // OnEnable regenerates from the data above
            return go;
        }
    }
}
