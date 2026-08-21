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

        public static string GetAvatarDir(string avatarGuid) =>
            $"{AvatarsRoot}/{avatarGuid}";

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
            if (string.IsNullOrEmpty(avatarGuid)) throw new ArgumentException("avatarGuid must not be empty.", nameof(avatarGuid));
            if (commit == null) throw new ArgumentNullException(nameof(commit));

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

        public static Commit LoadCommit(string avatarGuid, string commitId)
        {
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
            if (string.IsNullOrEmpty(avatarGuid)) throw new ArgumentException("avatarGuid must not be empty.", nameof(avatarGuid));
            if (string.IsNullOrEmpty(commitId)) throw new ArgumentException("commitId must not be empty.", nameof(commitId));

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
