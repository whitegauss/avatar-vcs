using System;
using System.Linq;
using AvatarVcs.Editor.AvatarReferences;
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
            var (avatarReferences, materialSettings) = AvatarReferenceCollector.CollectFromTrackedTargets(avatarRoot);
            var commit = CommitBuilder.CreateCommit(
                avatarRoot, message, config.currentBranch, currentHead, avatarReferences, materialSettings);
            CommitStore.SaveCommit(avatarGuid, commit);

            SetBranchHead(config, config.currentBranch, commit.commitId);
            CommitStore.SaveConfig(avatarGuid, config);

            return commit;
        }

        /// <summary>
        /// Creates a new branch pointing at fromCommitId (defaults to the
        /// current branch's head) without switching to it. Idempotent when
        /// called again with the same name and the same starting commit;
        /// throws if the name exists but points elsewhere (a genuine
        /// conflict, not a repeat of the same call).
        /// </summary>
        public static void CreateBranch(GameObject avatarRoot, string branchName, string fromCommitId = null)
        {
            if (string.IsNullOrEmpty(branchName)) throw new ArgumentException("branchName must not be empty.", nameof(branchName));
            if (!IsValidBranchName(branchName))
                throw new ArgumentException(
                    $"'{branchName}' is not a valid branch name. Avoid /, \\, :, *, ?, \", <, >, |, control "
                    + "characters, leading/trailing whitespace, and a leading '.' or '-'.",
                    nameof(branchName));

            var avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var config = CommitStore.LoadConfig(avatarGuid);

            var existing = FindEntry(config, branchName);
            var startCommitId = fromCommitId ?? FindEntry(config, config.currentBranch)?.commitId;

            if (existing != null)
            {
                if (existing.commitId == startCommitId) return;
                throw new InvalidOperationException($"Branch '{branchName}' already exists and points at a different commit.");
            }

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

        /// <summary>
        /// Restores the current branch to an arbitrary past commit (not
        /// necessarily its current head) -- e.g. picking an older checkpoint
        /// from history in the UI. The current branch's head moves to
        /// commitId; the auto-commit taken beforehand is preserved but left
        /// orphaned (design doc 4: orphan commit GC is out of MVP scope).
        /// </summary>
        public static CheckoutResult RestoreToCommit(GameObject avatarRoot, string commitId)
        {
            var avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var config = CommitStore.LoadConfig(avatarGuid);

            var targetCommit = CommitStore.LoadCommit(avatarGuid, commitId);
            if (targetCommit == null)
                throw new InvalidOperationException($"Commit '{commitId}' could not be loaded.");

            var currentHead = FindEntry(config, config.currentBranch)?.commitId;
            var result = CheckoutOperation.Checkout(targetCommit, avatarRoot, config.currentBranch, currentHead);
            if (!result.IsSuccess) return result;

            SetBranchHead(config, config.currentBranch, commitId);
            CommitStore.SaveConfig(avatarGuid, config);

            return result;
        }

        // Branch names aren't currently used as filesystem paths anywhere
        // (storage is keyed by avatarGuid/commitId), but restricting them
        // now avoids painting into a corner if that ever changes, and rules
        // out control characters and stray whitespace regardless.
        private static readonly char[] ForbiddenChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

        public static bool IsValidBranchName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name != name.Trim()) return false;
            if (name.StartsWith(".") || name.StartsWith("-")) return false;
            return name.All(c => !ForbiddenChars.Contains(c) && !char.IsControl(c));
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
