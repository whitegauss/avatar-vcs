using AvatarVcs.Runtime;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.UI
{
    /// <summary>
    /// Draws a small marker next to GameObjects carrying
    /// AvatarVcsTrackedReference in the Hierarchy window, so which parts of
    /// the avatar body are opted into tracking (design doc 1.4) are visible
    /// at a glance instead of requiring the Inspector to check each one.
    /// A plain colored rect rather than a named built-in icon (EditorGUIUtility.
    /// IconContent), since built-in icon names aren't guaranteed stable across
    /// Unity versions and a missing one would silently draw nothing.
    /// </summary>
    [InitializeOnLoad]
    public static class TrackedReferenceHierarchyIcon
    {
        private static readonly Color MarkerColor = new(0.2f, 0.6f, 1f, 0.9f);

        static TrackedReferenceHierarchyIcon()
        {
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;
        }

        public static bool ShouldShowMarker(GameObject go) =>
            go != null && go.GetComponent<AvatarVcsTrackedReference>() != null;

        private static void OnHierarchyItemGUI(int instanceId, Rect selectionRect)
        {
            var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (!ShouldShowMarker(go)) return;

            var markerRect = new Rect(selectionRect.xMax - 16f, selectionRect.y + 2f, 10f, 10f);
            EditorGUI.DrawRect(markerRect, MarkerColor);
        }
    }
}
