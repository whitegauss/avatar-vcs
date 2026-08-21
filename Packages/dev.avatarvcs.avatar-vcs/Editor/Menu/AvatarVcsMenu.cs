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
