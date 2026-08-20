using System;
using System.Linq;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.Model;
using UnityEngine;

namespace AvatarVcs.Editor.History
{
    /// <summary>
    /// Branch pointer bookkeeping on top of CheckoutOperation/CommitStore.
    /// Design doc section 2.2: branches are just named pointers to commit ids.
    /// </summary>
    public static class BranchManager
    {
        /// <summary>
        /// Commits the avatar's current state onto its current branch.
        /// </summary>
        public static Commit Commit(GameObject avatarRoot, string message)
        {
            var avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var config = CommitStore.LoadConfig(avatarGuid);

            var currentHead = FindEntry(config, config.currentBranch)?.commitId;
            var commit = CommitBuilder.CreateCommit(avatarRoot, message, config.currentBranch, currentHead);
            CommitStore.SaveCommit(avatarGuid, commit);

            SetBranchHead(config, config.currentBranch, commit.commitId);
            CommitStore.SaveConfig(avatarGuid, config);

            return commit;
        }

        /// <summary>
        /// Creates a new branch pointing at fromCommitId (defaults to the
        /// current branch's head) without switching to it.
        /// </summary>
        public static void CreateBranch(GameObject avatarRoot, string branchName, string fromCommitId = null)
        {
            if (string.IsNullOrEmpty(branchName)) throw new ArgumentException("branchName must not be empty.", nameof(branchName));

            var avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var config = CommitStore.LoadConfig(avatarGuid);

            if (FindEntry(config, branchName) != null)
                throw new InvalidOperationException($"Branch '{branchName}' already exists.");

            var startCommitId = fromCommitId ?? FindEntry(config, config.currentBranch)?.commitId;
            config.branches.Add(new BranchEntry { name = branchName, commitId = startCommitId });
            CommitStore.SaveConfig(avatarGuid, config);
        }

        /// <summary>
        /// Checks out targetBranch's head commit and makes it current. The
        /// source branch's head is updated to the auto-commit taken before
        /// switching, so in-progress work on it isn't lost.
        /// </summary>
        public static CheckoutResult SwitchBranch(GameObject avatarRoot, string targetBranch)
        {
            var avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var config = CommitStore.LoadConfig(avatarGuid);

            var targetEntry = FindEntry(config, targetBranch);
            if (targetEntry == null)
                throw new InvalidOperationException($"Branch '{targetBranch}' does not exist.");
            if (string.IsNullOrEmpty(targetEntry.commitId))
                throw new InvalidOperationException($"Branch '{targetBranch}' has no commits yet.");

            var targetCommit = CommitStore.LoadCommit(avatarGuid, targetEntry.commitId);
            if (targetCommit == null)
                throw new InvalidOperationException($"Commit '{targetEntry.commitId}' for branch '{targetBranch}' could not be loaded.");

            var sourceBranch = config.currentBranch;
            var currentHead = FindEntry(config, sourceBranch)?.commitId;

            var result = CheckoutOperation.Checkout(targetCommit, avatarRoot, sourceBranch, currentHead);
            if (!result.IsSuccess) return result;

            SetBranchHead(config, sourceBranch, result.AutoCommitId);
            config.currentBranch = targetBranch;
            CommitStore.SaveConfig(avatarGuid, config);

            return result;
        }

        private static BranchEntry FindEntry(BranchConfig config, string branchName) =>
            config.branches.FirstOrDefault(b => b.name == branchName);

        private static void SetBranchHead(BranchConfig config, string branchName, string commitId)
        {
            var entry = FindEntry(config, branchName);
            if (entry != null)
                entry.commitId = commitId;
            else
                config.branches.Add(new BranchEntry { name = branchName, commitId = commitId });
        }
    }
}
