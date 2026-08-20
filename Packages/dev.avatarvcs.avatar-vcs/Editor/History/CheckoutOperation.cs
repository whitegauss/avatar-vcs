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
        public bool IsSuccess => Kind == CheckoutResultKind.Success;

        private CheckoutResult(CheckoutResultKind kind, string autoCommitId, List<string> missingPrefabGuids)
        {
            Kind = kind;
            AutoCommitId = autoCommitId;
            MissingPrefabGuids = missingPrefabGuids;
        }

        public static CheckoutResult Success(string autoCommitId) => new(CheckoutResultKind.Success, autoCommitId, null);
        public static CheckoutResult MissingPrefabs(List<string> missingGuids) => new(CheckoutResultKind.MissingPrefabs, null, missingGuids);
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

            var missing = new List<string>();
            foreach (var container in commit.containers)
            {
                if (ContainerRestore.HasMissingPrefabs(container, out var containerMissing))
                    missing.AddRange(containerMissing);
            }
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

            foreach (var existing in ContainerManager.GetContainers(configRoot).ToList())
                Undo.DestroyObjectImmediate(existing.gameObject);

            foreach (var containerSnapshot in commit.containers)
                ContainerRestore.InstantiateContainer(containerSnapshot, configRoot);

            foreach (var reference in commit.avatarReferences)
                AvatarReferenceApplier.Apply(reference, avatarRoot.transform);

            foreach (var materialSetting in commit.materialSettings)
                MaterialSettingsApplier.Apply(materialSetting, avatarRoot);

            return CheckoutResult.Success(autoCommit.commitId);
        }
    }
}
