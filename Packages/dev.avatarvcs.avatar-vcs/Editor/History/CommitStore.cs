using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AvatarVcs.Editor.Model;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.History
{
    /// <summary>
    /// File-based persistence for commits, the per-avatar index, and branch
    /// config. Storage layout follows design doc section 4:
    /// ProjectSettings/AvatarVcs/avatars/{avatarGuid}/{config,index}.json and
    /// commits/{commitId}.json. Plain File I/O, not AssetDatabase: this data
    /// lives in ProjectSettings, not Assets.
    /// </summary>
    public static class CommitStore
    {
        private static string AvatarsRoot =>
            Path.Combine("ProjectSettings", "AvatarVcs", "avatars").Replace('\\', '/');

        /// <summary>
        /// Both avatarGuid and commitId are always Guid.NewGuid().ToString("N")
        /// in normal operation, but they're interpolated directly into
        /// filesystem paths below -- avatarGuid comes off a SerializeField
        /// that Unity deserializes directly (a hand-edited or shared scene/
        /// prefab could contain anything), and commitId is re-read from
        /// commit JSON on disk during checkout. This is the actual defense
        /// boundary against a value like "../../../outside" escaping
        /// ProjectSettings/AvatarVcs/ -- not AvatarVcsRoot.AssignGuid, which
        /// only guards this tool's own generation path.
        /// </summary>
        private static bool IsValidIdentifierShape(string value)
        {
            if (value == null || value.Length != 32) return false;
            foreach (var c in value)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }
            return true;
        }

        private static void EnsureValidIdentifier(string value, string paramName)
        {
            if (!IsValidIdentifierShape(value))
                throw new ArgumentException(
                    $"{paramName} must be a 32-character lowercase hex string (as produced by Guid.NewGuid().ToString(\"N\")); got '{value}'.",
                    paramName);
        }

        public static string GetAvatarDir(string avatarGuid)
        {
            EnsureValidIdentifier(avatarGuid, nameof(avatarGuid));
            return $"{AvatarsRoot}/{avatarGuid}";
        }

        /// <summary>
        /// Writes via a temp file in the same directory, then swaps it into
        /// place -- a crash or disk-full partway through leaves either the
        /// old content or the new content at path, never a truncated file.
        /// File.WriteAllText directly to the final path had no such
        /// guarantee, and a truncated JSON file permanently broke every
        /// future load for that avatar (JsonUtility.FromJson throwing, see
        /// TryLoadJson below).
        /// </summary>
        private static void WriteAtomically(string path, string content)
        {
            var tempPath = $"{path}.tmp";
            File.WriteAllText(tempPath, content);
            if (File.Exists(path))
                File.Replace(tempPath, path, null);
            else
                File.Move(tempPath, path);
        }

        /// <summary>
        /// JsonUtility.FromJson throws on malformed JSON (e.g. a file
        /// truncated by a crash mid-write, or a bad manual/merge edit).
        /// Returns default(T) and warns instead of propagating -- every
        /// caller already treats "file doesn't exist" as a recoverable,
        /// often totally normal case (a fresh avatar's history), so a
        /// corrupt-but-present file should degrade the same way rather than
        /// permanently breaking the window for that avatar.
        /// </summary>
        private static T TryLoadJson<T>(string path)
        {
            try
            {
                return JsonUtility.FromJson<T>(File.ReadAllText(path));
            }
            catch (Exception e) when (e is ArgumentException or IOException)
            {
                Debug.LogWarning($"[AvatarVCS] Could not parse '{path}' as {typeof(T).Name}; treating as missing. {e.Message}");
                return default;
            }
        }

        public static void SaveCommit(string avatarGuid, Commit commit)
        {
            if (commit == null) throw new ArgumentNullException(nameof(commit));
            EnsureValidIdentifier(commit.commitId, nameof(commit.commitId));

            var commitsDir = $"{GetAvatarDir(avatarGuid)}/commits";
            Directory.CreateDirectory(commitsDir);
            WriteAtomically($"{commitsDir}/{commit.commitId}.json", JsonUtility.ToJson(commit, true));

            var index = LoadIndex(avatarGuid);
            index.entries.RemoveAll(e => e.commitId == commit.commitId);
            index.entries.Add(new CommitIndexEntry
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
            if (!IsValidIdentifierShape(commitId)) return null;
            var path = $"{GetAvatarDir(avatarGuid)}/commits/{commitId}.json";
            return File.Exists(path) ? TryLoadJson<Commit>(path) : null;
        }

        public static CommitIndex LoadIndex(string avatarGuid)
        {
            var path = $"{GetAvatarDir(avatarGuid)}/index.json";
            return (File.Exists(path) ? TryLoadJson<CommitIndex>(path) : null) ?? new CommitIndex();
        }

        private static void SaveIndex(string avatarGuid, CommitIndex index)
        {
            var dir = GetAvatarDir(avatarGuid);
            Directory.CreateDirectory(dir);
            WriteAtomically($"{dir}/index.json", JsonUtility.ToJson(index, true));
        }

        public static BranchConfig LoadConfig(string avatarGuid)
        {
            var path = $"{GetAvatarDir(avatarGuid)}/config.json";
            return (File.Exists(path) ? TryLoadJson<BranchConfig>(path) : null) ?? new BranchConfig();
        }

        public static void SaveConfig(string avatarGuid, BranchConfig config)
        {
            var dir = GetAvatarDir(avatarGuid);
            Directory.CreateDirectory(dir);
            WriteAtomically($"{dir}/config.json", JsonUtility.ToJson(config, true));
        }

        /// <summary>
        /// Deletes a single commit: its generated assets (design doc section
        /// 4/1.4.3 -- duplicate materials created while checking it out),
        /// its JSON file, and its index entry. Refuses to delete a commit
        /// that's currently a branch head unless force is true, since that
        /// would leave the branch pointing at nothing.
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
            if (!IsValidIdentifierShape(commitId)) return;

            if (!force)
            {
                var headBranch = LoadConfig(avatarGuid).branches.FirstOrDefault(b => b.commitId == commitId);
                if (headBranch != null)
                    throw new InvalidOperationException(
                        $"Commit '{commitId}' is the head of branch '{headBranch.name}'; move the branch first or pass force: true.");
            }

            var commit = LoadCommit(avatarGuid, commitId);
            if (commit != null && commit.generatedAssets.Count > 0)
            {
                // A generated asset can be shared with other commits (e.g. a
                // branch created from this one that never regenerated its own
                // duplicate); only delete guids no other surviving commit
                // still references.
                var sharedElsewhere = LoadIndex(avatarGuid).entries
                    .Where(e => e.commitId != commitId)
                    .Select(e => LoadCommit(avatarGuid, e.commitId))
                    .Where(c => c != null)
                    .SelectMany(c => c.generatedAssets)
                    .ToHashSet();

                foreach (var guid in commit.generatedAssets)
                {
                    if (sharedElsewhere.Contains(guid)) continue;

                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(path))
                        AssetDatabase.DeleteAsset(path);
                }
            }

            var commitPath = $"{GetAvatarDir(avatarGuid)}/commits/{commitId}.json";
            if (File.Exists(commitPath)) File.Delete(commitPath);

            var index = LoadIndex(avatarGuid);
            index.entries.RemoveAll(e => e.commitId == commitId);
            SaveIndex(avatarGuid, index);
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
            var requestedIds = commitIds.Where(IsValidIdentifierShape).Distinct().ToList();
            var blocked = new List<string>();
            if (requestedIds.Count == 0) return blocked;

            var config = LoadConfig(avatarGuid);
            var index = LoadIndex(avatarGuid);

            var toDelete = new List<string>();
            foreach (var commitId in requestedIds)
            {
                if (!force && config.branches.Any(b => b.commitId == commitId))
                {
                    blocked.Add(commitId);
                    continue;
                }
                toDelete.Add(commitId);
            }
            var allCommits = new Dictionary<string, Commit>();
            foreach (var e in index.entries)
            {
                if (!string.IsNullOrEmpty(e.commitId) && !allCommits.ContainsKey(e.commitId))
                    allCommits[e.commitId] = LoadCommit(avatarGuid, e.commitId);
            }
            var toDeleteSet = new HashSet<string>(toDelete);

            // Every generated-asset guid still referenced by a commit that
            // will SURVIVE this batch (not just "any other commit right
            // now", since two commits sharing a guid could both be in the
            // same batch -- neither survives, so the asset has no more
            // referrers and should go too).
            var stillReferenced = allCommits
                .Where(kv => kv.Value != null && !toDeleteSet.Contains(kv.Key))
                .SelectMany(kv => kv.Value.generatedAssets)
                .ToHashSet();

            foreach (var commitId in toDelete)
            {
                if (allCommits.TryGetValue(commitId, out var commit) && commit != null)
                {
                    foreach (var guid in commit.generatedAssets)
                    {
                        if (stillReferenced.Contains(guid)) continue;
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        if (!string.IsNullOrEmpty(path))
                            AssetDatabase.DeleteAsset(path);
                    }
                }

                var commitPath = $"{GetAvatarDir(avatarGuid)}/commits/{commitId}.json";
                if (File.Exists(commitPath)) File.Delete(commitPath);
            }

            index.entries.RemoveAll(e => toDeleteSet.Contains(e.commitId));
            SaveIndex(avatarGuid, index);

            return blocked;
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
