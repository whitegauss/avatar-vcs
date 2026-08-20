using AvatarVcs.Editor.Core;
using AvatarVcs.Runtime;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.Menu
{
    /// <summary>
    /// Manual smoke-test entry points for phase 1 operations. The automated
    /// coverage lives in Tests/Editor/ContainerLifecycleTests.cs; this menu is
    /// for poking the tool by hand against a real avatar in the Hierarchy.
    /// </summary>
    public static class AvatarVcsMenu
    {
        [MenuItem("GameObject/AvatarVCS/Ensure Root", false, 0)]
        private static void EnsureRootMenuItem()
        {
            var target = Selection.activeGameObject;
            if (target == null)
            {
                Debug.LogWarning("[AvatarVCS] Select the avatar root GameObject first.");
                return;
            }

            var root = ContainerManager.EnsureRoot(target);
            Selection.activeGameObject = root;
        }

        [MenuItem("GameObject/AvatarVCS/Ensure Root", true)]
        private static bool ValidateEnsureRootMenuItem() => Selection.activeGameObject != null;

        [MenuItem("GameObject/AvatarVCS/Create Container", false, 1)]
        private static void CreateContainerMenuItem()
        {
            var root = Selection.activeGameObject;
            if (root == null || root.GetComponent<AvatarVcsRoot>() == null)
            {
                Debug.LogWarning($"[AvatarVCS] Select the '{ContainerManager.RootName}' root GameObject first.");
                return;
            }

            var containerId = "new_container";
            var container = ContainerManager.CreateContainer(root, containerId);
            Selection.activeGameObject = container;
        }

        [MenuItem("GameObject/AvatarVCS/Create Container", true)]
        private static bool ValidateCreateContainerMenuItem() =>
            Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<AvatarVcsRoot>() != null;
    }
}
