using AvatarVcs.Editor.Core;
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
    /// Draws a small dim marker next to every GameObject covered by
    /// NEITHER of AvatarVCS's two tracking mechanisms. Tracking is the
    /// default now (Ensure Root auto-seeds it, design doc 1.4), so most of
    /// an avatar's hierarchy is covered and marking every tracked object
    /// would just be visual noise -- the useful signal is the exception:
    /// which few objects are NOT covered and would silently not round-trip
    /// through commit/checkout. A plain colored rect rather than a named
    /// built-in icon (EditorGUIUtility.IconContent), since built-in icon
    /// names aren't guaranteed stable across Unity versions and a missing
    /// one would silently draw nothing.
    /// </summary>
    [InitializeOnLoad]
    public static class HierarchyTrackingStatusIcon
    {
        private static readonly Color UntrackedColor = new(0.4f, 0.4f, 0.4f, 0.6f);

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

        /// <summary>
        /// True only for the exception worth flagging: go is part of an
        /// avatar actually under AvatarVCS management (the avatar root
        /// itself, or somewhere underneath it) AND isn't covered by either
        /// tracking mechanism. Without the management-scope check, every
        /// unrelated GameObject in the whole scene (lights, cameras, objects
        /// with nothing to do with any avatar) would get flagged too,
        /// drowning out the actual signal.
        /// </summary>
        public static bool ShouldShowUntrackedMarker(GameObject go) =>
            go != null && GetTrackingStatus(go) == HierarchyTrackingStatus.None && IsPartOfManagedAvatar(go);

        /// <summary>
        /// True if go or any ancestor has "[AvatarVCS]" as a direct child --
        /// i.e. go is the avatar root itself or sits somewhere underneath it.
        /// </summary>
        private static bool IsPartOfManagedAvatar(GameObject go)
        {
            for (var t = go.transform; t != null; t = t.parent)
            {
                if (ContainerManager.FindRoot(t.gameObject) != null)
                    return true;
            }

            return false;
        }

        private static void OnHierarchyItemGUI(int instanceId, Rect selectionRect)
        {
            var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (!ShouldShowUntrackedMarker(go)) return;

            var markerRect = new Rect(selectionRect.xMax - 16f, selectionRect.y + 2f, 10f, 10f);
            EditorGUI.DrawRect(markerRect, UntrackedColor);
        }
    }
}
