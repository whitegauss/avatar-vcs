using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.UI;
using AvatarVcs.Runtime;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.Menu
{
    /// <summary>
    /// GameObject context-menu entry points for initial container setup and
    /// opening the main UI. Commit/checkout/branch history live only in
    /// AvatarVcsWindow now -- they need the window's diff/branch context to
    /// use safely, and duplicating them here as one-shot commands was also
    /// how a raw Hierarchy selection (e.g. a single outfit item, not the
    /// avatar) could silently become the tracked "avatar" for a whole new,
    /// unrelated commit history.
    /// </summary>
    public static class AvatarVcsMenu
    {
        [MenuItem("GameObject/AvatarVCS/Ensure Root", false, 0)]
        private static void EnsureRootMenuItem()
        {
            var target = ResolveSelectionAsAvatarRoot("Ensure Root");
            if (target == null) return;

            var root = ContainerManager.EnsureRoot(target);
            Selection.activeGameObject = root;
        }

        [MenuItem("GameObject/AvatarVCS/Ensure Root", true)]
        private static bool ValidateEnsureRootMenuItem() => Selection.activeGameObject != null;

        [MenuItem("GameObject/AvatarVCS/Create Container", false, 1)]
        private static void CreateContainerMenuItem()
        {
            var root = ResolveExistingRoot(Selection.activeGameObject);
            if (root == null)
            {
                Debug.LogWarning($"[AvatarVCS] Select the avatar root (or its '{ContainerManager.RootName}' child) first. "
                    + "Run Ensure Root first if it doesn't exist yet.");
                return;
            }

            var containerId = "new_container";
            var container = ContainerManager.CreateContainer(root, containerId);
            Selection.activeGameObject = container;
        }

        [MenuItem("GameObject/AvatarVCS/Create Container", true)]
        private static bool ValidateCreateContainerMenuItem() =>
            ResolveExistingRoot(Selection.activeGameObject) != null;

        [MenuItem("GameObject/AvatarVCS/Open Window", false, 2)]
        private static void OpenWindowMenuItem()
        {
            var target = ResolveSelectionAsAvatarRoot("Open Window");
            if (target == null) return;

            AvatarVcsWindow.OpenFor(target);
        }

        [MenuItem("GameObject/AvatarVCS/Open Window", true)]
        private static bool ValidateOpenWindowMenuItem() => Selection.activeGameObject != null;

        // Design doc 1.4: the avatar body itself (e.g. "Body") stays outside
        // container management, but its BlendShape weights and material
        // references can still be tracked -- opt in per-target here rather
        // than auto-tracking everything, since most of an avatar's hierarchy
        // isn't meant to be captured this way.
        [MenuItem("GameObject/AvatarVCS/Track Body Properties Here", false, 3)]
        private static void TrackReferenceMenuItem()
        {
            var target = Selection.activeGameObject;
            if (target == null || target.GetComponent<AvatarVcsTrackedReference>() != null) return;
            Undo.AddComponent<AvatarVcsTrackedReference>(target);
        }

        [MenuItem("GameObject/AvatarVCS/Track Body Properties Here", true)]
        private static bool ValidateTrackReferenceMenuItem() =>
            Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<AvatarVcsTrackedReference>() == null;

        [MenuItem("GameObject/AvatarVCS/Untrack Body Properties Here", false, 4)]
        private static void UntrackReferenceMenuItem()
        {
            var marker = Selection.activeGameObject?.GetComponent<AvatarVcsTrackedReference>();
            if (marker != null) Undo.DestroyObjectImmediate(marker);
        }

        [MenuItem("GameObject/AvatarVCS/Untrack Body Properties Here", true)]
        private static bool ValidateUntrackReferenceMenuItem() =>
            Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<AvatarVcsTrackedReference>() != null;

        /// <summary>
        /// selection accepted as-is if it already IS the "[AvatarVCS]" root;
        /// otherwise resolved from the avatar root that owns it, if any.
        /// Returns null if neither exists yet.
        /// </summary>
        private static GameObject ResolveExistingRoot(GameObject selection)
        {
            if (selection == null) return null;
            if (selection.GetComponent<AvatarVcsRoot>() != null) return selection;
            return ContainerManager.FindRoot(selection);
        }

        /// <summary>
        /// Menu-specific wrapper around ContainerManager's shared
        /// resolve-or-confirm logic: warns (rather than silently no-op'ing)
        /// when nothing is selected, since a menu command has no other way
        /// to tell the user why it did nothing.
        /// </summary>
        private static GameObject ResolveSelectionAsAvatarRoot(string actionLabel)
        {
            var selection = Selection.activeGameObject;
            if (selection == null)
            {
                Debug.LogWarning("[AvatarVCS] Select the avatar root GameObject first.");
                return null;
            }

            return ContainerManager.ResolveAvatarRootWithConfirmation(
                selection, $"{actionLabel} will start tracking IT as the avatar.");
        }
    }
}
