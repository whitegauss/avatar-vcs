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

        /// <summary>
        /// Explicitly opted out via AvatarVcsUntracked, at this object or an
        /// ancestor -- not captured, even if a tracked ancestor's recursive
        /// walk would otherwise reach it (KAN-11).
        /// </summary>
        Untracked,
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
        /// each check covers both "the marker is directly on go" and "an
        /// ancestor's marker applies to go" in one call.
        /// Priority, matching AvatarReferenceCapture's real behaviour:
        /// ContainerManaged (anything under "[AvatarVCS]" is unconditionally
        /// skipped, even with a stray AvatarVcsTrackedReference there) >
        /// Untracked (AvatarVcsUntracked excludes its subtree even from a
        /// tracked ancestor's recursive walk, KAN-11) > TrackedReference (a
        /// descendant with no marker of its own is still genuinely captured
        /// via a marked ancestor) > None.
        /// </summary>
        public static HierarchyTrackingStatus GetTrackingStatus(GameObject go)
        {
            if (go == null) return HierarchyTrackingStatus.None;

            if (go.GetComponentInParent<AvatarVcsRoot>(includeInactive: true) != null)
                return HierarchyTrackingStatus.ContainerManaged;

            if (go.GetComponentInParent<AvatarVcsUntracked>(includeInactive: true) != null)
                return HierarchyTrackingStatus.Untracked;

            if (go.GetComponentInParent<AvatarVcsTrackedReference>(includeInactive: true) != null)
                return HierarchyTrackingStatus.TrackedReference;

            return HierarchyTrackingStatus.None;
        }

        /// <summary>
        /// True for an object worth flagging as "won't round-trip through
        /// commit/checkout": part of an avatar actually under AvatarVCS
        /// management, and either covered by no tracking mechanism (None) or
        /// explicitly opted out (Untracked). The management-scope check keeps
        /// every unrelated GameObject in the scene (lights, cameras, ...)
        /// from lighting up and drowning out the signal.
        /// </summary>
        public static bool ShouldShowUntrackedMarker(GameObject go)
        {
            if (go == null || !ContainerManager.IsUnderManagedAvatar(go)) return false;
            var status = GetTrackingStatus(go);
            return status is HierarchyTrackingStatus.None or HierarchyTrackingStatus.Untracked;
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
