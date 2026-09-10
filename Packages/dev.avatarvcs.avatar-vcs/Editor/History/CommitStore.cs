using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AvatarVcs.Core.History;
using AvatarVcs.Core.Model;
using AvatarVcs.Core.Naming;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.History
{
    /// <summary>
    /// File-based persistence for commits, the per-avatar index, and branch
    /// config. Storage layout follows design doc section 4:
    /// ProjectSettings/AvatarVcs/avatars/{avatarGuid}/{config,index}.json and
    /// commits/{commitId}.json. Plain File I/O, not AssetDatabase: this data
    /// lives in ProjectSettings, not Assets. Path/identifier shape rules
    /// live in AvatarVcs.Core.History.CommitPaths/CommitIdentifier; deletion
    /// planning lives in CommitDeletionPlanner. This class is the I/O half:
    /// it loads what a plan needs, hands it to the planner, and carries out
    /// what comes back.
    /// </summary>
    public static class CommitStore
    {
        /// <summary>
        /// Writes via a temp file in the same directory, then swaps it into
        /// place -- a crash or disk-full partway through leaves either the
        /// old content or the new content at path, never a truncated file.
        /// File.WriteAllText directly to the final path had no such
        /// guarantee, and a truncated JSON file permanently broke every
        /// future load for that avatar (JsonUtility.FromJson throwing, see
        /// StoredJson.Load).
        ///
        /// The temp file's bytes are flushed all the way to disk before the
        /// rename (KAN-18). File.WriteAllText returns once the data is in the
        /// OS page cache, so a power loss in the window before the rename
        /// commits can reorder them -- the rename reaching disk first -- and
        /// leave a zero-length or partial file at path, the exact truncated-
        /// JSON failure this method exists to prevent.
        /// </summary>
        private static void WriteAtomically(string path, string content) =>
            AtomicFile.WriteAllText(path, content);

        public static string GetAvatarDir(string avatarGuid) => CommitPaths.AvatarDir(avatarGuid);

        public static void SaveCommit(string avatarGuid, Commit commit)
        {
            if (commit == null) throw new ArgumentNullException(nameof(commit));
            CommitIdentifier.EnsureValid(commit.commitId, nameof(commit.commitId));

            var commitPath = CommitPaths.CommitFile(avatarGuid, commit.commitId);
            Directory.CreateDirectory(Path.GetDirectoryName(commitPath)!);
            WriteAtomically(commitPath, JsonUtility.ToJson(commit, true));

            var index = LoadIndex(avatarGuid);
            CommitIndexOps.Upsert(index, new CommitIndexEntry
            {
                commitId = commit.commitId,
                parentCommitId = commit.parentCommitId,
                branch = commit.branch,
                message = commit.message,
                timestamp = commit.timestamp,
            });
            SaveIndex(avatarGuid, index);
        }

        /// <summary>
        /// Returns null (same as "no commit with this id") for a malformed
        /// commitId instead of throwing -- unlike SaveCommit's commitId
        /// (always this tool's own freshly-generated guid), callers here
        /// often iterate ids straight from a possibly hand-edited/corrupted
        /// index.json (see DeleteCommit's shared-asset scan), and every
        /// existing call site already handles "commit not found" gracefully.
        /// Still the actual defense boundary: an invalid shape never reaches
        /// the path interpolation below.
        /// </summary>
        public static Commit LoadCommit(string avatarGuid, string commitId)
        {
            if (!CommitIdentifier.IsValidShape(commitId)) return null;
            var path = CommitPaths.CommitFile(avatarGuid, commitId);
            if (!File.Exists(path)) return null;

            // A commit written by a newer AvatarVCS may carry fields this
            // build doesn't deserialize; restoring it would silently drop
            // them, so StoredJson reports it as TooNew and this returns null
            // -- the same answer as for a corrupt file, which is what every
            // caller here already handles.
            var (commit, _) = StoredJson.Load<Commit>(path, Commit.CurrentSchemaVersion);
            return commit;
        }

        /// <summary>
        /// The commit list, rebuilt from the commits themselves when the file
        /// is gone or unreadable.
        ///
        /// index.json holds nothing the commit files don't: it exists so the
        /// history list doesn't have to read every multi-megabyte snapshot to
        /// draw itself. Treating a broken one as "this avatar has no history"
        /// was therefore both wrong and, once the next save wrote a one-entry
        /// index over it, permanent. This never writes -- recovering a view of
        /// the history is a read -- and the next real save puts the rebuilt
        /// index back on disk (see SaveIndex, which moves the broken file
        /// aside rather than onto).
        /// </summary>
        public static CommitIndex LoadIndex(string avatarGuid)
        {
            var path = CommitPaths.IndexFile(avatarGuid);
            var (index, status) = StoredJson.Load<CommitIndex>(path, CommitIndex.CurrentSchemaVersion);
            if (status == StoredFileStatus.Loaded) return index;

            // Written by a newer build. Rebuilding from the commits would be
            // no better -- they are that build's too -- and the file itself is
            // protected by SaveIndex refusing to write over it.
            if (status == StoredFileStatus.TooNew) return new CommitIndex();

            var rebuilt = CommitIndexOps.RebuildFrom(CommitsOnDisk(avatarGuid));
            if (rebuilt.entries.Count > 0)
            {
                Debug.LogWarning($"[AvatarVCS] '{path}' is "
                    + (status == StoredFileStatus.Missing ? "missing" : "unreadable")
                    + $"; rebuilt {rebuilt.entries.Count} entries from the commit files themselves. "
                    + "The history is intact.");
            }

            return rebuilt;
        }

        private static void SaveIndex(string avatarGuid, CommitIndex index)
        {
            var path = CommitPaths.IndexFile(avatarGuid);
            StoredJson.EnsureWritable<CommitIndex>(path, CommitIndex.CurrentSchemaVersion);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteAtomically(path, JsonUtility.ToJson(index, true));
        }

        /// <summary>
        /// Branch heads, rebuilt from the commits when the file is gone or
        /// unreadable (BranchConfigOps.RebuildFrom). Same reasoning as
        /// LoadIndex, with one gap that can't be closed: a branch created but
        /// never committed to leaves no trace in any commit.
        /// </summary>
        public static BranchConfig LoadConfig(string avatarGuid)
        {
            var path = CommitPaths.ConfigFile(avatarGuid);
            var (config, status) = StoredJson.Load<BranchConfig>(path, BranchConfig.CurrentSchemaVersion);
            if (status == StoredFileStatus.Loaded) return config;
            if (status == StoredFileStatus.TooNew) return new BranchConfig();

            var rebuilt = BranchConfigOps.RebuildFrom(CommitsOnDisk(avatarGuid));
            if (rebuilt.branches.Count > 0)
            {
                Debug.LogWarning($"[AvatarVCS] '{path}' is "
                    + (status == StoredFileStatus.Missing ? "missing" : "unreadable")
                    + $"; rebuilt {rebuilt.branches.Count} branch head(s) from the commit files, now on "
                    + $"'{rebuilt.currentBranch}'. A branch with no commits on it cannot be recovered this way.");
            }

            return rebuilt;
        }

        public static void SaveConfig(string avatarGuid, BranchConfig config)
        {
            var path = CommitPaths.ConfigFile(avatarGuid);
            StoredJson.EnsureWritable<BranchConfig>(path, BranchConfig.CurrentSchemaVersion);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteAtomically(path, JsonUtility.ToJson(config, true));
        }

        /// <summary>
        /// Every commit file in the avatar's commits/ directory, one at a time
        /// -- these are megabytes each, and a rebuild only ever needs five
        /// fields off each one, so they are yielded lazily rather than all
        /// held at once.
        ///
        /// The filename is the commit id, and LoadCommit re-checks it, so a
        /// stray file dropped in the directory is skipped rather than
        /// believed. A commit written by a newer build reads as null there and
        /// is skipped too: it is genuinely not summarisable by this build.
        /// </summary>
        private static IEnumerable<Commit> CommitsOnDisk(string avatarGuid)
        {
            var dir = Path.Combine(CommitPaths.AvatarDir(avatarGuid), "commits");
            if (!Directory.Exists(dir)) yield break;

            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            {
                var commit = LoadCommit(avatarGuid, Path.GetFileNameWithoutExtension(file));
                if (commit != null) yield return commit;
            }
        }

        /// <summary>
        /// Every commit index.entries points at, keyed by commitId and
        /// deduped (first entry for a given id wins) -- what
        /// CommitDeletionPlanner needs to work out which generated assets
        /// still have a surviving referrer. Tolerates a duplicate or empty
        /// commitId in index.entries (a corrupted/hand-edited index)
        /// instead of throwing or double-loading.
        /// </summary>
        private static Dictionary<string, Commit> LoadAllCommits(string avatarGuid, CommitIndex index)
        {
            var result = new Dictionary<string, Commit>();
            foreach (var e in index.entries)
            {
                if (!string.IsNullOrEmpty(e.commitId) && !result.ContainsKey(e.commitId))
                    result[e.commitId] = LoadCommit(avatarGuid, e.commitId);
            }
            return result;
        }

        /// <summary>
        /// Carries out a CommitDeletionPlan: deletes the generated assets it
        /// names, then each commit's JSON file, then removes all of them
        /// from index in one save.
        /// </summary>
        private static void ExecutePlan(string avatarGuid, CommitDeletionPlan plan, CommitIndex index)
        {
            // Nothing here may delete a material the scene is wearing right
            // now. The planner only knows whether another *commit* still
            // references a generated asset -- but a checkout points the
            // renderers themselves at these duplicates, so deleting the
            // commit that produced them takes the avatar's materials with it.
            // Harmless while the shader allowlist matched almost nothing and
            // generatedAssets was always empty; real from 0.5.0 on.
            var inUse = MaterialGuidsUsedInLoadedScenes();

            foreach (var guid in plan.AssetGuidsToDelete)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;

                if (inUse.Contains(guid))
                {
                    // Left behind on purpose. An orphaned .mat costs disk;
                    // deleting one out from under a renderer costs the user
                    // their avatar's appearance.
                    Debug.LogWarning($"[AvatarVCS] Not deleting '{path}': a renderer in an open scene is still "
                        + "using it. It will be cleaned up once nothing points at it.");
                    continue;
                }

                // generatedAssets comes from commit JSON, which this repo
                // treats as hand-editable / corruptible everywhere else
                // (StoredJson.Load, SnapshotDiffer's SafeToDictionary, ...) --
                // but this is the one path that hits AssetDatabase.DeleteAsset
                // on a user's real asset. Only delete something that matches
                // how MaterialSettingsApplier actually names its duplicates.
                if (!IsAvatarVcsGeneratedAsset(path))
                {
                    Debug.LogWarning($"[AvatarVCS] Not deleting '{path}': it's listed in a commit's generatedAssets but "
                        + "doesn't look AvatarVCS-generated (not a '_avatarvcs' .mat, and not under "
                        + $"{GeneratedAssetNaming.GeneratedFolder}/). A corrupt or hand-edited commit can name any GUID here.");
                    continue;
                }

                AssetDatabase.DeleteAsset(path);
            }

            foreach (var commitId in plan.CommitsToDelete)
            {
                var commitPath = CommitPaths.CommitFile(avatarGuid, commitId);
                if (File.Exists(commitPath)) File.Delete(commitPath);
            }

            CommitIndexOps.Remove(index, new HashSet<string>(plan.CommitsToDelete));
            SaveIndex(avatarGuid, index);
        }

        /// <summary>
        /// Whether assetPath is something MaterialSettingsApplier generated,
        /// and so may be deleted along with the commit that generated it.
        /// The naming/placement rules live in Core so the producer and this
        /// guard share one definition (GeneratedAssetNaming); the extra
        /// IsValidFolder check is here because it needs the AssetDatabase.
        ///
        /// A folder would already fail the .mat check, but ask outright too:
        /// AssetDatabase.DeleteAsset on a folder removes it *recursively*,
        /// which is the worst thing a corrupt generatedAssets entry could
        /// achieve, and a user folder can be named anything at all.
        /// </summary>
        /// <summary>
        /// GUIDs of every material currently assigned to a renderer in a
        /// loaded scene. FindObjectsOfTypeAll reaches inactive objects and
        /// every open scene, which is what matters: a hidden outfit's
        /// materials are just as much the user's as a visible one's.
        ///
        /// Only open scenes can be checked. A material used solely by a
        /// closed scene can still be deleted here -- the same limitation the
        /// rest of the tool has, and far better than the previous behaviour
        /// of not looking at the scene at all.
        /// </summary>
        private static HashSet<string> MaterialGuidsUsedInLoadedScenes()
        {
            var guids = new HashSet<string>();

            foreach (var renderer in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (renderer == null) continue;

                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null) continue;

                    var path = AssetDatabase.GetAssetPath(material);
                    if (string.IsNullOrEmpty(path)) continue;

                    var guid = AssetDatabase.AssetPathToGUID(path);
                    if (!string.IsNullOrEmpty(guid)) guids.Add(guid);
                }
            }

            return guids;
        }

        private static bool IsAvatarVcsGeneratedAsset(string assetPath) =>
            GeneratedAssetNaming.LooksGenerated(assetPath) && !AssetDatabase.IsValidFolder(assetPath);

        /// <summary>
        /// Deletes a single commit: its generated assets (design doc
        /// section 4/1.4.3 -- duplicate materials created while checking it
        /// out), its JSON file, and its index entry. Refuses to delete a
        /// commit that's currently a branch head unless force is true,
        /// since that would leave the branch pointing at nothing. Routed
        /// through the same CommitDeletionPlanner as the batch DeleteCommits
        /// below, so "still referenced elsewhere" is decided identically
        /// either way.
        /// </summary>
        public static void DeleteCommit(string avatarGuid, string commitId, bool force = false)
        {
            // A malformed commitId (e.g. from a corrupted index.json entry
            // the user selected in the UI) is treated the same as "no such
            // commit" -- consistent with the silent no-op this method already
            // does below when commitPath doesn't exist -- rather than
            // reaching the path interpolation further down. avatarGuid is
            // still validated (via GetAvatarDir, reached from LoadConfig/
            // LoadCommit just below): unlike commitId, it identifies which
            // avatar's history this call is even operating on, so a bad
            // value there can't be treated as a harmless no-op the same way.
            if (!CommitIdentifier.IsValidShape(commitId)) return;

            var config = LoadConfig(avatarGuid);
            var index = LoadIndex(avatarGuid);
            var loadedCommits = LoadAllCommits(avatarGuid, index);

            var plan = CommitDeletionPlanner.Plan(config, loadedCommits, new[] { commitId }, force);

            if (plan.Blocked.Count > 0)
            {
                var blocked = plan.Blocked[0];
                throw new InvalidOperationException(
                    $"Commit '{blocked.CommitId}' is the head of branch '{blocked.BranchName}'; move the branch first or pass force: true.");
            }

            ExecutePlan(avatarGuid, plan, index);
        }

        /// <summary>
        /// Batch counterpart to DeleteCommit, for deleting several commits at
        /// once (the UI's "Delete Selected" bulk action). DeleteCommit's
        /// shared-generated-asset scan reloads every OTHER commit from disk
        /// on every single call -- calling it once per id in a loop is
        /// O(k*n) file reads for k deletions across n total commits. This
        /// computes "still referenced by a surviving commit" once for the
        /// whole batch instead.
        ///
        /// Best-effort: a commit that's currently a branch head is skipped
        /// (its id is included in the returned list) rather than aborting
        /// the rest of the batch, since a mixed selection of deletable and
        /// head-blocked commits is a normal thing to select in the UI.
        /// </summary>
        public static List<string> DeleteCommits(string avatarGuid, IEnumerable<string> commitIds, bool force = false)
        {
            var requestedIds = commitIds.Where(CommitIdentifier.IsValidShape).Distinct().ToList();
            if (requestedIds.Count == 0) return new List<string>();

            var config = LoadConfig(avatarGuid);
            var index = LoadIndex(avatarGuid);
            var loadedCommits = LoadAllCommits(avatarGuid, index);

            var plan = CommitDeletionPlanner.Plan(config, loadedCommits, requestedIds, force);

            ExecutePlan(avatarGuid, plan, index);

            return plan.Blocked.Select(b => b.CommitId).ToList();
        }

        /// <summary>
        /// Deletes all stored history for one avatar. Mainly for test cleanup;
        /// not part of the normal user-facing flow.
        /// </summary>
        public static void DeleteAvatarHistory(string avatarGuid)
        {
            var dir = GetAvatarDir(avatarGuid);
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
