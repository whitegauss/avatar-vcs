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
            var target = ResolveAvatarRootWithConfirmation(Selection.activeGameObject, "Ensure Root");
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
            var target = ResolveAvatarRootWithConfirmation(Selection.activeGameObject, "Open Window");
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
        /// Resolves the avatar to operate on from a raw Hierarchy selection.
        /// If selection is already inside an existing AvatarVCS structure (a
        /// container, something inside one, or the "[AvatarVCS]" root
        /// itself), walks up to the actual owning avatar automatically. If
        /// selection has no existing structure at all, confirms with the
        /// user before treating it as a brand new avatar root -- it could
        /// just as easily be a single outfit item as the avatar itself.
        /// </summary>
        private static GameObject ResolveAvatarRootWithConfirmation(GameObject selection, string actionLabel)
        {
            if (selection == null)
            {
                Debug.LogWarning("[AvatarVCS] Select the avatar root GameObject first.");
                return null;
            }

            var enclosing = ContainerManager.FindEnclosingAvatarRoot(selection);
            if (enclosing != null) return enclosing;

            if (ContainerManager.FindRoot(selection) != null) return selection;

            return EditorUtility.DisplayDialog("Start Tracking This Object?",
                    $"'{selection.name}' has no AvatarVCS history yet. {actionLabel} will start tracking IT as the avatar.\n\n"
                    + "If you meant to select your actual avatar's root GameObject (or something inside its existing containers), cancel and select that instead.",
                    "Start Tracking", "Cancel")
                ? selection
                : null;
        }
    }
}
