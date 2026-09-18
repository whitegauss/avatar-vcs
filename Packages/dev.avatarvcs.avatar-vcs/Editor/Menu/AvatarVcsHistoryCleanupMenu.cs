using System.Linq;
using AvatarVcs.Core.History;
using AvatarVcs.Editor.History;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.Menu
{
    /// <summary>
    /// "Clean Up Orphaned History": moves stored histories whose avatar no
    /// longer exists anywhere in the project to CommitPaths.TrashRoot.
    ///
    /// Deliberately a command the user runs, not something a commit does on
    /// its own. It moves version-control history out from under the tool, the
    /// scan that decides what is orphaned reads every scene and prefab in the
    /// project, and the whole situation only arises from repeated setup churn
    /// -- none of which is worth doing silently behind an unrelated action.
    /// </summary>
    public static class AvatarVcsHistoryCleanupMenu
    {
        // Under Tools/, not Window/AvatarVCS/: "Window/AvatarVCS" is already
        // a leaf item (AvatarVcsWindow.Open), and Unity can't have the same
        // path be both a command and a submenu -- adding children there
        // takes the window's own menu entry away.
        private const string CleanUpMenuPath = "Tools/AvatarVCS/Clean Up Orphaned History";

        [MenuItem(CleanUpMenuPath, false, 20)]
        private static void CleanUpMenuItem()
        {
            var histories = AvatarHistoryInventory.Scan();
            if (histories.Count == 0)
            {
                EditorUtility.DisplayDialog("AvatarVCS", "No stored avatar history was found.", "OK");
                return;
            }

            var plan = AvatarHistoryCleanupPlanner.Plan(histories);
            var toTrash = plan.Where(d => d.delete).ToList();
            var kept = plan.Count - toTrash.Count;

            if (toTrash.Count == 0)
            {
                EditorUtility.DisplayDialog("AvatarVCS",
                    $"Nothing to clean up.\n\n{kept} stored " + (kept == 1 ? "history" : "histories")
                    + " and every one of them is still in use (or is the most recent one, which is kept on purpose).",
                    "OK");
                return;
            }

            var body =
                $"Move {toTrash.Count} avatar " + (toTrash.Count == 1 ? "history" : "histories")
                + $" ({EditorUtility.FormatBytes(toTrash.Sum(d => d.history.byteSize))}) to the trash?\n\n"
                + "No avatar in this project carries these ids any more. Every scene and prefab was searched, "
                + "not just the open one.\n\n"
                + string.Join("\n", toTrash.Select(Describe))
                + $"\n\n{kept} kept where they are. Nothing is deleted: the folders move to "
                + $"'{CommitPaths.TrashRoot}' and stay there. If one of these turns out to belong to an avatar that "
                + $"is still around, move it back under '{CommitPaths.AvatarsRoot}' named with that avatar's "
                + "current id.";

            if (!EditorUtility.DisplayDialog("AvatarVCS — Clean Up Orphaned History", body, "Move to Trash", "Cancel"))
                return;

            var trashed = AvatarHistoryCleanup.Run(histories);

            Debug.Log($"[AvatarVCS] Moved {trashed.Count} orphaned avatar "
                + (trashed.Count == 1 ? "history" : "histories")
                + $" to '{CommitPaths.TrashRoot}' "
                + $"({EditorUtility.FormatBytes(trashed.Sum(h => h.byteSize))}): "
                + string.Join(", ", trashed.Select(h => h.avatarGuid)));
        }

        // The same sweep runs on its own once per Unity session when the
        // AvatarVCS window is opened, so the folder doesn't grow forever
        // without anyone remembering this command. Exposed as a toggle
        // because it deletes history, and because the check behind it reads
        // the project when it does run.
        private const string AutoMenuPath = "Tools/AvatarVCS/Clean Up Orphaned History Automatically";

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
