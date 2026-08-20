using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using AvatarVcs.Editor.UI;
using AvatarVcs.Runtime;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.Menu
{
    /// <summary>
    /// Manual smoke-test entry points. The automated coverage lives in
    /// Tests/Editor/; this menu is for poking the tool by hand against a real
    /// avatar in the Hierarchy.
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

        [MenuItem("GameObject/AvatarVCS/Commit Current State", false, 2)]
        private static void CommitMenuItem()
        {
            var target = Selection.activeGameObject;
            if (target == null)
            {
                Debug.LogWarning("[AvatarVCS] Select the avatar root GameObject first.");
                return;
            }

            var commit = BranchManager.Commit(target, "Manual commit");
            Debug.Log($"[AvatarVCS] Committed '{commit.commitId}' on branch '{commit.branch}' ({commit.containers.Count} container(s)).");
        }

        [MenuItem("GameObject/AvatarVCS/Commit Current State", true)]
        private static bool ValidateCommitMenuItem() => Selection.activeGameObject != null;

        [MenuItem("GameObject/AvatarVCS/List Commits", false, 3)]
        private static void ListCommitsMenuItem()
        {
            var target = Selection.activeGameObject;
            if (target == null)
            {
                Debug.LogWarning("[AvatarVCS] Select the avatar root GameObject first.");
                return;
            }

            var avatarGuid = ContainerManager.GetAvatarGuid(target);
            var index = CommitStore.LoadIndex(avatarGuid);
            if (index.entries.Count == 0)
            {
                Debug.Log("[AvatarVCS] No commits yet.");
                return;
            }

            foreach (var entry in index.entries)
                Debug.Log($"[AvatarVCS] {entry.commitId} ({entry.branch}) {entry.timestamp}: {entry.message}");
        }

        [MenuItem("GameObject/AvatarVCS/List Commits", true)]
        private static bool ValidateListCommitsMenuItem() => Selection.activeGameObject != null;

        [MenuItem("GameObject/AvatarVCS/Open Window", false, 4)]
        private static void OpenWindowMenuItem()
        {
            var target = Selection.activeGameObject;
            if (target == null)
            {
                Debug.LogWarning("[AvatarVCS] Select the avatar root GameObject first.");
                return;
            }

            AvatarVcsWindow.OpenFor(target);
        }

        [MenuItem("GameObject/AvatarVCS/Open Window", true)]
        private static bool ValidateOpenWindowMenuItem() => Selection.activeGameObject != null;
    }
}
