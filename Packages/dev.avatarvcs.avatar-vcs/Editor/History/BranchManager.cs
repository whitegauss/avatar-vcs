using System;
using AvatarVcs.Core.Diagnostics;
using AvatarVcs.Core.History;
using AvatarVcs.Editor.AvatarReferences;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.Diagnostics;
using AvatarVcs.Core.Model;
using UnityEngine;

namespace AvatarVcs.Editor.History
{
    /// <summary>
    /// Branch pointer bookkeeping on top of CheckoutOperation/CommitStore.
    /// Design doc section 2.2: branches are just named pointers to commit ids.
    /// Lookup/head-update/name-validation logic itself lives in
    /// AvatarVcs.Core.History.BranchConfigOps; this class is the I/O half.
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

            var currentHead = BranchConfigOps.HeadOf(config, config.currentBranch);

            // KAN-20: one DiagnosticLog for the whole commit; capture helpers
            // append to it and it is flushed to the console once at the end.
            // Flush in a finally so warnings collected before a throw (e.g.
            // CommitBuilder's container validation) still reach the console.
            var log = new DiagnosticLog();
            try
            {
                var (avatarReferences, materialSettings) = AvatarReferenceCollector.CollectFromTrackedTargets(avatarRoot, log);
                var commit = CommitBuilder.CreateCommit(
                    avatarRoot, message, config.currentBranch, currentHead, avatarReferences, materialSettings, log);
                CommitStore.SaveCommit(avatarGuid, commit);

                BranchConfigOps.SetHead(config, config.currentBranch, commit.commitId);
                CommitStore.SaveConfig(avatarGuid, config);

                return commit;
            }
            finally
            {
                UnityDiagnosticSink.Flush(log);
            }
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

            var existing = BranchConfigOps.Find(config, branchName);
            var startCommitId = fromCommitId ?? BranchConfigOps.HeadOf(config, config.currentBranch);

            if (existing != null)
            {
                if (existing.commitId == startCommitId) return;
                throw new InvalidOperationException($"Branch '{branchName}' already exists and points at a different commit.");
            }

            config.branches.Add(new BranchEntry { name = branchName, commitId = startCommitId });
            CommitStore.SaveConfig(avatarGuid, config);
        }

        /// <summary>
        /// Checks out targetBranch's head commit and makes it current.
        /// Doesn't take a safety-net auto-commit first -- unlike a
        /// container's destroy/regenerate, checkout only overwrites
        /// GameObjects/values Unity's own Undo already tracks, so Ctrl+Z is
        /// the recovery path for uncommitted work, not another commit.
        /// </summary>
        public static CheckoutResult SwitchBranch(GameObject avatarRoot, string targetBranch)
        {
            var avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var config = CommitStore.LoadConfig(avatarGuid);

            var targetEntry = BranchConfigOps.Find(config, targetBranch);
            if (targetEntry == null)
                throw new InvalidOperationException($"Branch '{targetBranch}' does not exist.");
            if (string.IsNullOrEmpty(targetEntry.commitId))
                throw new InvalidOperationException($"Branch '{targetBranch}' has no commits yet.");

            var targetCommit = CommitStore.LoadCommit(avatarGuid, targetEntry.commitId);
            if (targetCommit == null)
                throw new InvalidOperationException($"Commit '{targetEntry.commitId}' for branch '{targetBranch}' could not be loaded.");

            var result = CheckoutOperation.CheckoutWithoutAutoCommit(targetCommit, avatarRoot);
            if (!result.IsSuccess) return result;

            config.currentBranch = targetBranch;
            CommitStore.SaveConfig(avatarGuid, config);

            return result;
        }

        /// <summary>
        /// Restores the current branch to an arbitrary past commit (not
        /// necessarily its current head) -- e.g. picking an older checkpoint
        /// from history in the UI. The current branch's head moves to
        /// commitId. No safety-net auto-commit first; see SwitchBranch.
        /// </summary>
        public static CheckoutResult RestoreToCommit(GameObject avatarRoot, string commitId)
        {
            var avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var config = CommitStore.LoadConfig(avatarGuid);

            var targetCommit = CommitStore.LoadCommit(avatarGuid, commitId);
            if (targetCommit == null)
                throw new InvalidOperationException($"Commit '{commitId}' could not be loaded.");

            var result = CheckoutOperation.CheckoutWithoutAutoCommit(targetCommit, avatarRoot);
            if (!result.IsSuccess) return result;

            BranchConfigOps.SetHead(config, config.currentBranch, commitId);
            CommitStore.SaveConfig(avatarGuid, config);

            return result;
        }

        public static bool IsValidBranchName(string name) => BranchConfigOps.IsValidBranchName(name);
    }
}
