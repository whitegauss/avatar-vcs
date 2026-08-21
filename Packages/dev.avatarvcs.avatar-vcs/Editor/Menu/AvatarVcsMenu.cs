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

            var root = ContainerManager.EnsureRootWithDefaults(target);
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

        // No validate function: unlike Ensure Root/Create Container/Track
        // Properties, Open Window is always available with nothing
        // selected -- it just opens blank (same as Window/AvatarVCS), with
        // an ObjectField/"Use Selected" inside to assign the avatar
        // afterward. Previously required a selection here even though the
        // window itself never did (issue #47).
        [MenuItem("GameObject/AvatarVCS/Open Window", false, 2)]
        private static void OpenWindowMenuItem()
        {
            if (Selection.activeGameObject == null)
            {
                AvatarVcsWindow.Open();
                return;
            }

            var target = ResolveSelectionAsAvatarRoot("Open Window");
            if (target == null) return; // user cancelled the "start tracking?" confirmation

            AvatarVcsWindow.OpenFor(target);
        }

        // Design doc 1.4: the avatar body (e.g. "Body", "Armature", the
        // avatar root's own components like VRCAvatarDescriptor) stays
        // outside container management structurally -- no object/component
        // add-or-remove is ever tracked, and bone Transform pose is never
        // touched -- but the *existing values* of *existing components* on a
        // marked subtree (BlendShape weights, material slots by name/GUID,
        // and every other component's serialized fields, recursively) can
        // still be tracked and overwritten on restore. Opt in per-subtree
        // here rather than auto-tracking everything, since most of an
        // avatar's hierarchy isn't meant to be captured this way.
        [MenuItem("GameObject/AvatarVCS/Track Properties Here", false, 3)]
        private static void TrackReferenceMenuItem()
        {
            var target = Selection.activeGameObject;
            if (target == null || target.GetComponent<AvatarVcsTrackedReference>() != null) return;
            Undo.AddComponent<AvatarVcsTrackedReference>(target);
        }

        [MenuItem("GameObject/AvatarVCS/Track Properties Here", true)]
        private static bool ValidateTrackReferenceMenuItem()
        {
            var selection = Selection.activeGameObject;
            if (selection == null || selection.GetComponent<AvatarVcsTrackedReference>() != null) return false;

            // A container-managed subtree is destroyed and regenerated on
            // every checkout, so tracking would just be silently wiped every
            // time -- block it here rather than let the user discover that
            // as "why did my tracking disappear".
            return selection.GetComponentInParent<AvatarVcsRoot>(includeInactive: true) == null;
        }

        [MenuItem("GameObject/AvatarVCS/Untrack Properties Here", false, 4)]
        private static void UntrackReferenceMenuItem()
        {
            var marker = Selection.activeGameObject?.GetComponent<AvatarVcsTrackedReference>();
            if (marker != null) Undo.DestroyObjectImmediate(marker);
        }

        [MenuItem("GameObject/AvatarVCS/Untrack Properties Here", true)]
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
