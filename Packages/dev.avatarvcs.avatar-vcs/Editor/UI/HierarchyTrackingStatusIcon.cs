using AvatarVcs.Runtime;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.UI
{
    public enum HierarchyTrackingStatus
    {
        None,
        TrackedReference,
        ContainerManaged,
    }

    /// <summary>
    /// Draws a small marker next to every GameObject covered by either of
    /// AvatarVCS's two tracking mechanisms, so what's captured on commit is
    /// visible at a glance instead of requiring the Inspector to check each
    /// one. A plain colored rect rather than a named built-in icon
    /// (EditorGUIUtility.IconContent), since built-in icon names aren't
    /// guaranteed stable across Unity versions and a missing one would
    /// silently draw nothing.
    /// </summary>
    [InitializeOnLoad]
    public static class HierarchyTrackingStatusIcon
    {
        private static readonly Color TrackedColor = new(0.2f, 0.6f, 1f, 0.9f);
        private static readonly Color ContainerManagedColor = new(0.95f, 0.6f, 0.15f, 0.9f);

        static HierarchyTrackingStatusIcon()
        {
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;
        }

        /// <summary>
        /// GetComponentInParent checks go itself before walking ancestors, so
        /// this covers both "the marker is directly on go" and "an
        /// ancestor's marker's recursive capture already walks down into
        /// go" in one call -- a descendant with no marker of its own is
        /// still genuinely captured via that ancestor (AvatarReferenceCapture
        /// walks every descendant of a marked target), so it must show the
        /// same status. ContainerManaged is checked first: anything under
        /// "[AvatarVCS]" is unconditionally skipped by AvatarReferenceCapture
        /// regardless of a stray AvatarVcsTrackedReference placed there, so
        /// container status must win over it, matching that real behavior.
        /// </summary>
        public static HierarchyTrackingStatus GetTrackingStatus(GameObject go)
        {
            if (go == null) return HierarchyTrackingStatus.None;

            if (go.GetComponentInParent<AvatarVcsRoot>(includeInactive: true) != null)
                return HierarchyTrackingStatus.ContainerManaged;

            if (go.GetComponentInParent<AvatarVcsTrackedReference>(includeInactive: true) != null)
                return HierarchyTrackingStatus.TrackedReference;

            return HierarchyTrackingStatus.None;
        }

        private static void OnHierarchyItemGUI(int instanceId, Rect selectionRect)
        {
            var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            var status = GetTrackingStatus(go);
            if (status == HierarchyTrackingStatus.None) return;

            var color = status == HierarchyTrackingStatus.TrackedReference ? TrackedColor : ContainerManagedColor;
            var markerRect = new Rect(selectionRect.xMax - 16f, selectionRect.y + 2f, 10f, 10f);
            EditorGUI.DrawRect(markerRect, color);
        }
    }
}
