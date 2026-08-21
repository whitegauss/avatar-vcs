using System;
using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Editor.AvatarReferences;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.MaterialSettings;
using AvatarVcs.Editor.Model;
using AvatarVcs.Editor.Operations;
using AvatarVcs.Runtime;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.History
{
    public enum CheckoutResultKind
    {
        Success,
        MissingPrefabs,
    }

    public class CheckoutResult
    {
        public CheckoutResultKind Kind { get; }
        public string AutoCommitId { get; }
        public List<string> MissingPrefabGuids { get; }
        public List<string> VersionWarnings { get; }
        public bool IsSuccess => Kind == CheckoutResultKind.Success;

        private CheckoutResult(CheckoutResultKind kind, string autoCommitId, List<string> missingPrefabGuids, List<string> versionWarnings)
        {
            Kind = kind;
            AutoCommitId = autoCommitId;
            MissingPrefabGuids = missingPrefabGuids;
            VersionWarnings = versionWarnings ?? new List<string>();
        }

        public static CheckoutResult Success(string autoCommitId, List<string> versionWarnings = null) =>
            new(CheckoutResultKind.Success, autoCommitId, null, versionWarnings);
        public static CheckoutResult MissingPrefabs(List<string> missingGuids) =>
            new(CheckoutResultKind.MissingPrefabs, null, missingGuids, null);
    }

    /// <summary>
    /// Restores the scene to match a commit. Design doc section 3.2: validate
    /// before destroying anything, auto-commit the current state as a safety
    /// net, then destroy and regenerate every container. Branch pointer
    /// bookkeeping is BranchManager's job, not this operation's.
    /// </summary>
    public static class CheckoutOperation
    {
        public static CheckoutResult Checkout(Commit commit, GameObject avatarRoot, string sourceBranch, string autoCommitParentId)
        {
            if (commit == null) throw new ArgumentNullException(nameof(commit));
            if (avatarRoot == null) throw new ArgumentNullException(nameof(avatarRoot));

            var missing = FindMissingPrefabs(commit);
            if (missing.Count > 0)
                return CheckoutResult.MissingPrefabs(missing);

            var configRoot = ContainerManager.EnsureRoot(avatarRoot);
            var avatarGuid = configRoot.GetComponent<AvatarVcsRoot>().AvatarGuid;

            var autoCommit = CommitBuilder.CreateCommit(
                avatarRoot,
                $"[auto] before checkout to {commit.commitId}",
                sourceBranch,
                autoCommitParentId);
            CommitStore.SaveCommit(avatarGuid, autoCommit);

            var versionWarnings = ApplyCommitToScene(commit, avatarRoot, configRoot, avatarGuid);

            return CheckoutResult.Success(autoCommit.commitId, versionWarnings);
        }

        /// <summary>
        /// Applies a commit to the scene without taking the "before checkout"
        /// auto-commit safety net or touching branch/config state at all.
        /// Design doc section 5.2 (compare mode): flipping between two
        /// commits to eyeball a difference must not spam the history with a
        /// commit per toggle. Callers that use this are responsible for
        /// their own return-to-safety plan (e.g. an auto-commit taken once
        /// before compare mode starts, not on every toggle).
        /// </summary>
        public static CheckoutResult CheckoutWithoutAutoCommit(Commit commit, GameObject avatarRoot)
        {
            if (commit == null) throw new ArgumentNullException(nameof(commit));
            if (avatarRoot == null) throw new ArgumentNullException(nameof(avatarRoot));

            var missing = FindMissingPrefabs(commit);
            if (missing.Count > 0)
                return CheckoutResult.MissingPrefabs(missing);

            var configRoot = ContainerManager.EnsureRoot(avatarRoot);
            var avatarGuid = configRoot.GetComponent<AvatarVcsRoot>().AvatarGuid;

            var versionWarnings = ApplyCommitToScene(commit, avatarRoot, configRoot, avatarGuid);

            return CheckoutResult.Success(null, versionWarnings);
        }

        private static List<string> FindMissingPrefabs(Commit commit)
        {
            var missing = new List<string>();
            foreach (var container in commit.containers)
            {
                if (ContainerRestore.HasMissingPrefabs(container, out var containerMissing))
                    missing.AddRange(containerMissing);
            }
            return missing;
        }

        private static List<string> ApplyCommitToScene(Commit commit, GameObject avatarRoot, GameObject configRoot, string avatarGuid)
        {
            foreach (var existing in ContainerManager.GetContainers(configRoot).ToList())
                Undo.DestroyObjectImmediate(existing.gameObject);

            foreach (var containerSnapshot in commit.containers)
                ContainerRestore.InstantiateContainer(containerSnapshot, configRoot);

            foreach (var reference in commit.avatarReferences)
                AvatarReferenceApplier.Apply(reference, avatarRoot.transform);

            var priorGeneratedGuids = commit.materialSettings.Select(m => m.generatedGuid).ToList();
            foreach (var materialSetting in commit.materialSettings)
            {
                // One slot failing (unsupported shader, an out-of-range slot
                // after the target's material list shrank, an unresolvable
                // source guid, ...) must not abort the checkout with the
                // rest of materialSettings/avatarReferences left unapplied
                // and containers already destroyed/regenerated.
                try
                {
                    MaterialSettingsApplier.Apply(materialSetting, avatarRoot);
                }
                catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
                {
                    Debug.LogWarning($"[AvatarVCS] Failed to apply material settings for slot {materialSetting.slot} "
                        + $"on '{materialSetting.targetPath}': {e.Message}");
                }
            }

            // Apply populates/reuses each entry's generatedGuid in place; if
            // any of them are new, persist the commit so future checkouts of
            // it reuse the same duplicates instead of generating more.
            var generatedChanged = !commit.materialSettings.Select(m => m.generatedGuid).SequenceEqual(priorGeneratedGuids);
            if (generatedChanged)
            {
                commit.generatedAssets = commit.materialSettings
                    .Select(m => m.generatedGuid)
                    .Where(g => !string.IsNullOrEmpty(g))
                    .Distinct()
                    .ToList();
                CommitStore.SaveCommit(avatarGuid, commit);
            }

            return AssetVersionChecker.CheckForChanges(commit.assetVersions);
        }
    }
}
