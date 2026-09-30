using System.Collections.Generic;
using System.IO;
using System.Linq;
using AvatarVcs.Core.History;
using AvatarVcs.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AvatarVcs.Editor.History
{
    /// <summary>
    /// Moves orphaned avatar histories to the trash on its own, so the
    /// avatars folder doesn't grow forever without anyone remembering the
    /// menu command. Nothing here deletes anything: see
    /// CommitStore.TrashAvatarHistory for why the sweep is not allowed to.
    ///
    /// The reason this isn't simply run everywhere is cost: deciding what is
    /// orphaned reads every scene and prefab in the project. So it is fenced
    /// behind conditions that are each cheap to evaluate, and the expensive
    /// part only happens when it could actually change something:
    ///
    ///   1. The user hasn't switched it off, and this isn't a batch-mode run
    ///      (which is where the test suite executes).
    ///   2. Once per Unity session, and only from the entry points where the
    ///      user actually asked for the window -- not from OnEnable, which
    ///      also fires when a domain reload re-creates an already-open
    ///      window, and not from a commit.
    ///   3. There are at least two stored histories that no currently-loaded
    ///      avatar accounts for. Below that the retention rule would keep
    ///      everything anyway, so the scan cannot change the outcome and is
    ///      skipped outright. This is the condition that makes it free in the
    ///      normal case.
    ///
    /// The sweep keeps every guarantee the manual command has: an incomplete
    /// scan moves nothing, and the most recently committed orphan is always
    /// left in place.
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
        /// Called when the user opens the AvatarVCS window. Returns without
        /// touching the disk unless every condition above holds.
        /// </summary>
        public static void RunIfDue()
        {
            if (!Enabled) return;

            // A test run must never have history deleted out from under it:
            // the fixtures create real avatar histories that no scene
            // references, which is exactly the shape this deletes. The
            // trigger is a user opening the window, which no test does, but
            // that is a convention -- this is the check that enforces it
            // where our tests actually run.
            if (Application.isBatchMode) return;

            if (SessionState.GetBool(RanThisSessionKey, false)) return;
            SessionState.SetBool(RanThisSessionKey, true);

            if (!CouldDeleteAnything()) return;

            if (ProjectLooksMidUpgrade(out var why))
            {
                Debug.Log($"[AvatarVCS] Skipped the automatic cleanup of orphaned history: {why} "
                    + "Whether a history is orphaned is decided from what the scenes reference, and that is exactly "
                    + "what a half-finished package update makes unreliable. Run Tools > AvatarVCS > "
                    + "Clean Up Orphaned History by hand once the project is sound.");
                return;
            }

            var trashed = AvatarHistoryCleanup.Run(AvatarHistoryInventory.Scan());
            if (trashed.Count == 0) return;

            Debug.Log($"[AvatarVCS] Moved {trashed.Count} avatar "
                + (trashed.Count == 1 ? "history" : "histories")
                + $" no avatar in this project claims any more to '{CommitPaths.TrashRoot}' "
                + $"({EditorUtility.FormatBytes(trashed.Sum(d => d.byteSize))}): "
                + string.Join(", ", trashed.Select(d => d.avatarGuid)) + ". "
                + "The most recent one was kept where it was. Nothing was deleted -- if one of these belongs to an "
                + "avatar that is still around, rename its folder back under "
                + $"'{CommitPaths.AvatarsRoot}' with that avatar's current id. Turn this off under "
                + "Tools > AvatarVCS > Clean Up Orphaned History Automatically.");
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

        /// <summary>
        /// A GameObject with a missing script in an open scene means some
        /// package's components can't be resolved right now -- and updating
        /// AvatarVCS itself used to do that to every AvatarVcsRoot in the
        /// project, because the package shipped without .meta files and Unity
        /// re-generated the script GUIDs on each install.
        ///
        /// That state is precisely where this must not run. An avatar whose
        /// AvatarVcsRoot is missing still keeps its history alive here, because
        /// AvatarHistoryInventory finds the avatarGuid in the scene's text --
        /// but the moment the user tidies the missing components away and
        /// saves, the last evidence of that avatarGuid is gone and its history
        /// looks orphaned. Moving it aside then would be silent, and silently
        /// moving a live avatar's history is still a bad afternoon even though
        /// the trash makes it recoverable -- so nothing is swept automatically
        /// while any missing script is around, whoever's it is.
        /// </summary>
        private static bool ProjectLooksMidUpgrade(out string why)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var transform in root.GetComponentsInChildren<Transform>(includeInactive: true))
                    {
                        if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0) continue;

                        why = $"'{transform.name}' in scene '{scene.name}' has a missing script.";
                        return true;
                    }
                }
            }

            why = null;
            return false;
        }
    }

    /// <summary>
    /// The half that touches the disk, shared by the menu command and the
    /// automatic run so the two can't drift into different policies.
    /// </summary>
    public static class AvatarHistoryCleanup
    {
        /// <summary>
        /// Moves every history the planner marked to the trash and returns the
        /// ones that actually moved -- a history whose folder is locked stays
        /// where it is and is left out, so callers report what happened rather
        /// than what was intended.
        /// </summary>
        public static List<AvatarHistoryInfo> Run(IEnumerable<AvatarHistoryInfo> histories)
        {
            var trashed = new List<AvatarHistoryInfo>();

            foreach (var history in AvatarHistoryCleanupPlanner.Plan(histories).Where(d => d.delete).Select(d => d.history))
            {
                if (CommitStore.TrashAvatarHistory(history.avatarGuid) != null)
                    trashed.Add(history);
            }

            return trashed;
        }
    }
}
