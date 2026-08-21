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

        public static void SaveCommit(string avatarGuid, Commit commit)
        {
            if (commit == null) throw new ArgumentNullException(nameof(commit));
            EnsureValidIdentifier(commit.commitId, nameof(commit.commitId));

            var commitsDir = $"{GetAvatarDir(avatarGuid)}/commits";
            Directory.CreateDirectory(commitsDir);
            File.WriteAllText($"{commitsDir}/{commit.commitId}.json", JsonUtility.ToJson(commit, true));

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
            return File.Exists(path) ? JsonUtility.FromJson<Commit>(File.ReadAllText(path)) : null;
        }

        public static CommitIndex LoadIndex(string avatarGuid)
        {
            var path = $"{GetAvatarDir(avatarGuid)}/index.json";
            return File.Exists(path) ? JsonUtility.FromJson<CommitIndex>(File.ReadAllText(path)) : new CommitIndex();
        }

        private static void SaveIndex(string avatarGuid, CommitIndex index)
        {
            var dir = GetAvatarDir(avatarGuid);
            Directory.CreateDirectory(dir);
            File.WriteAllText($"{dir}/index.json", JsonUtility.ToJson(index, true));
        }

        public static BranchConfig LoadConfig(string avatarGuid)
        {
            var path = $"{GetAvatarDir(avatarGuid)}/config.json";
            return File.Exists(path) ? JsonUtility.FromJson<BranchConfig>(File.ReadAllText(path)) : new BranchConfig();
        }

        public static void SaveConfig(string avatarGuid, BranchConfig config)
        {
            var dir = GetAvatarDir(avatarGuid);
            Directory.CreateDirectory(dir);
            File.WriteAllText($"{dir}/config.json", JsonUtility.ToJson(config, true));
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
