using System;
using System.IO;
using AvatarVcs.Editor.Model;
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

        public static void SaveCommit(string avatarGuid, Commit commit)
        {
            if (string.IsNullOrEmpty(avatarGuid)) throw new ArgumentException("avatarGuid must not be empty.", nameof(avatarGuid));
            if (commit == null) throw new ArgumentNullException(nameof(commit));

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

        public static Commit LoadCommit(string avatarGuid, string commitId)
        {
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
