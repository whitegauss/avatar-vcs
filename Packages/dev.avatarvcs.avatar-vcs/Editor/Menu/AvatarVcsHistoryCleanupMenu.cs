using System.Linq;
using AvatarVcs.Core.History;
using AvatarVcs.Editor.History;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.Menu
{
    /// <summary>
    /// "Clean Up Orphaned History": removes stored histories whose avatar no
    /// longer exists anywhere in the project.
    ///
    /// Deliberately a command the user runs, not something a commit does on
    /// its own. It deletes version-control history, the scan that decides what
    /// is orphaned reads every scene and prefab in the project, and the whole
    /// situation only arises from repeated setup churn -- none of which is
    /// worth doing silently behind an unrelated action.
    /// </summary>
    public static class AvatarVcsHistoryCleanupMenu
    {
        [MenuItem("Window/AvatarVCS/Clean Up Orphaned History", false, 20)]
        private static void CleanUpMenuItem()
        {
            var histories = AvatarHistoryInventory.Scan();
            if (histories.Count == 0)
            {
                EditorUtility.DisplayDialog("AvatarVCS", "No stored avatar history was found.", "OK");
                return;
            }

            var plan = AvatarHistoryCleanupPlanner.Plan(histories);
            var toDelete = plan.Where(d => d.delete).ToList();
            var kept = plan.Count - toDelete.Count;

            if (toDelete.Count == 0)
            {
                EditorUtility.DisplayDialog("AvatarVCS",
                    $"Nothing to clean up.\n\n{kept} stored " + (kept == 1 ? "history" : "histories")
                    + " and every one of them is still in use (or is the most recent one, which is kept on purpose).",
                    "OK");
                return;
            }

            var freed = toDelete.Sum(d => d.history.byteSize);
            var body =
                $"Delete {toDelete.Count} avatar " + (toDelete.Count == 1 ? "history" : "histories")
                + $" ({EditorUtility.FormatBytes(freed)})?\n\n"
                + "No avatar in this project carries these ids any more. Every scene and prefab was searched, "
                + "not just the open one.\n\n"
                + string.Join("\n", toDelete.Select(Describe))
                + $"\n\n{kept} kept. This cannot be undone.";

            if (!EditorUtility.DisplayDialog("AvatarVCS — Clean Up Orphaned History", body, "Delete", "Cancel"))
                return;

            var deleted = AvatarHistoryCleanup.Run(histories);

            Debug.Log($"[AvatarVCS] Deleted {deleted.Count} orphaned avatar "
                + (deleted.Count == 1 ? "history" : "histories")
                + $" ({EditorUtility.FormatBytes(freed)}): "
                + string.Join(", ", deleted.Select(h => h.avatarGuid)));
        }

        // The same sweep runs on its own once per Unity session when the
        // AvatarVCS window is opened, so the folder doesn't grow forever
        // without anyone remembering this command. Exposed as a toggle
        // because it deletes history, and because the check behind it reads
        // the project when it does run.
        private const string AutoMenuPath = "Window/AvatarVCS/Clean Up Orphaned History Automatically";

        [MenuItem(AutoMenuPath, false, 21)]
        private static void ToggleAutoCleanup() =>
            AvatarHistoryAutoCleanup.Enabled = !AvatarHistoryAutoCleanup.Enabled;

        [MenuItem(AutoMenuPath, true)]
        private static bool ToggleAutoCleanupValidate()
        {
            global::UnityEditor.Menu.SetChecked(AutoMenuPath, AvatarHistoryAutoCleanup.Enabled);
            return true;
        }

        private static string Describe(AvatarHistoryCleanupPlanner.Decision d) =>
            $"  {d.history.avatarGuid}  —  {d.history.commitCount} "
            + (d.history.commitCount == 1 ? "commit" : "commits")
            + $", {EditorUtility.FormatBytes(d.history.byteSize)}"
            + (string.IsNullOrEmpty(d.history.newestCommitTimestamp)
                ? ""
                : $", last {d.history.newestCommitTimestamp}");
    }
}
