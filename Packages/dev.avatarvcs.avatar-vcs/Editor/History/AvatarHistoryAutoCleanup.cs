using System.Collections.Generic;
using System.IO;
using System.Linq;
using AvatarVcs.Core.History;
using AvatarVcs.Runtime;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.History
{
    /// <summary>
    /// Removes orphaned avatar histories on its own, so the folder doesn't
    /// grow forever without anyone remembering the menu command.
    ///
    /// The reason this isn't simply run everywhere is cost: deciding what is
    /// orphaned reads every scene and prefab in the project. So it is fenced
    /// behind conditions that are each cheap to evaluate, and the expensive
    /// part only happens when it could actually change something:
    ///
    ///   1. The user hasn't switched it off.
    ///   2. Once per Unity session, on opening the AvatarVCS window -- not on
    ///      every commit or domain reload, and never from a test.
    ///   3. There are at least two stored histories that no currently-loaded
    ///      avatar accounts for. Below that the retention rule would keep
    ///      everything anyway, so the scan cannot change the outcome and is
    ///      skipped outright. This is the condition that makes it free in the
    ///      normal case.
    ///
    /// Deletion itself keeps every guarantee the manual command has: an
    /// incomplete scan deletes nothing, and the most recently committed
    /// orphan is always kept.
    /// </summary>
    public static class AvatarHistoryAutoCleanup
    {
        private const string EnabledPref = "AvatarVcs.AutoCleanupOrphanedHistory";
        private const string RanThisSessionKey = "AvatarVcs.AutoCleanupRan";

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledPref, true);
            set => EditorPrefs.SetBool(EnabledPref, value);
        }

        /// <summary>
        /// Called when the AvatarVCS window opens. Returns without touching
        /// the disk unless every condition above holds.
        /// </summary>
        public static void RunIfDue()
        {
            if (!Enabled) return;
            if (SessionState.GetBool(RanThisSessionKey, false)) return;
            SessionState.SetBool(RanThisSessionKey, true);

            if (!CouldDeleteAnything()) return;

            var deleted = AvatarHistoryCleanup.Run(AvatarHistoryInventory.Scan());
            if (deleted.Count == 0) return;

            Debug.Log($"[AvatarVCS] Removed {deleted.Count} avatar "
                + (deleted.Count == 1 ? "history" : "histories")
                + $" no avatar in this project claims any more ({EditorUtility.FormatBytes(deleted.Sum(d => d.byteSize))}). "
                + "The most recent one was kept. Turn this off under "
                + "Window > AvatarVCS > Clean Up Orphaned History Automatically.");
        }

        /// <summary>
        /// Cheap pre-check: are there even enough unaccounted-for histories
        /// for the retention rule to delete one? Only counts what is already
        /// in memory, so it costs a directory listing and a scene-object
        /// lookup -- no file reads.
        ///
        /// "Unaccounted for" is not the same as "orphaned" (an avatar in a
        /// closed scene is unaccounted for here and found by the real scan),
        /// so this can only ever be an over-estimate -- which is what makes it
        /// safe to skip on.
        /// </summary>
        private static bool CouldDeleteAnything()
        {
            if (!Directory.Exists(CommitPaths.AvatarsRoot)) return false;

            var stored = Directory.GetDirectories(CommitPaths.AvatarsRoot)
                .Select(Path.GetFileName)
                .Where(CommitIdentifier.IsValidShape)
                .ToHashSet();
            if (stored.Count <= AvatarHistoryCleanupPlanner.DefaultKeepOrphans) return false;

            foreach (var root in Resources.FindObjectsOfTypeAll<AvatarVcsRoot>())
                if (root != null && !string.IsNullOrEmpty(root.AvatarGuid))
                    stored.Remove(root.AvatarGuid);

            return stored.Count > AvatarHistoryCleanupPlanner.DefaultKeepOrphans;
        }
    }

    /// <summary>
    /// The delete half, shared by the menu command and the automatic run so
    /// the two can't drift into different policies.
    /// </summary>
    public static class AvatarHistoryCleanup
    {
        public static List<AvatarHistoryInfo> Run(IEnumerable<AvatarHistoryInfo> histories)
        {
            var deleted = AvatarHistoryCleanupPlanner.Plan(histories)
                .Where(d => d.delete)
                .Select(d => d.history)
                .ToList();

            foreach (var history in deleted)
                CommitStore.DeleteAvatarHistory(history.avatarGuid);

            return deleted;
        }
    }
}
