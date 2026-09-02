using System;
using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Core.Diagnostics;
using AvatarVcs.Core.History;
using AvatarVcs.Core.Model;
using AvatarVcs.Editor.AvatarReferences;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.Diagnostics;
using AvatarVcs.Editor.MaterialSettings;
using AvatarVcs.Editor.Operations;
using AvatarVcs.Runtime;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.History
{
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

            // KAN-20: one DiagnosticLog for the whole operation. Every helper
            // appends to it instead of calling Debug.LogWarning directly; we
            // flush it to the console (unchanged behaviour) and also hand its
            // entries to the CheckoutResult so a caller/test can inspect them.
            var log = new DiagnosticLog();

            // Same capture BranchManager.Commit uses -- this safety-net
            // commit must preserve tracked BlendShape/material state too, or
            // it's silently lost the moment the checkout below overwrites it.
            var (autoAvatarReferences, autoMaterialSettings) = AvatarReferenceCollector.CollectFromTrackedTargets(avatarRoot, log);
            var autoCommit = CommitBuilder.CreateCommit(
                avatarRoot,
                $"[auto] before checkout to {commit.commitId}",
                sourceBranch,
                autoCommitParentId,
                autoAvatarReferences,
                autoMaterialSettings,
                log);
            CommitStore.SaveCommit(avatarGuid, autoCommit);

            var versionWarnings = ApplyCommitToScene(commit, avatarRoot, configRoot, avatarGuid, log);

            UnityDiagnosticSink.Flush(log);
            return CheckoutResult.Success(autoCommit.commitId, versionWarnings, log.Entries);
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

            var log = new DiagnosticLog();
            var versionWarnings = ApplyCommitToScene(commit, avatarRoot, configRoot, avatarGuid, log);

            UnityDiagnosticSink.Flush(log);
            return CheckoutResult.Success(null, versionWarnings, log.Entries);
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

        private static List<string> ApplyCommitToScene(Commit commit, GameObject avatarRoot, GameObject configRoot, string avatarGuid, DiagnosticLog log)
        {
            foreach (var existing in ContainerManager.GetContainers(configRoot).ToList())
                Undo.DestroyObjectImmediate(existing.gameObject);

            // Two passes across all containers, not one pass per container:
            // a component on one container's root can reference an object
            // inside a *different* container (design doc allows arbitrary
            // component references there), which only resolves once every
            // container's structure already exists -- instantiating and
            // immediately applying one container's components before the
            // next container is even created would fail to resolve such a
            // reference depending on commit.containers' order.
            var restoredContainers = commit.containers
                .Select(snapshot => (snapshot, go: ContainerRestore.InstantiateContainerStructure(snapshot, configRoot, log)))
                .ToList();
            foreach (var (snapshot, go) in restoredContainers)
                ContainerRestore.ApplyContainerComponents(snapshot, go, avatarRoot, log);

            foreach (var reference in commit.avatarReferences)
                AvatarReferenceApplier.Apply(reference, avatarRoot.transform, log);

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
                    MaterialSettingsApplier.Apply(materialSetting, avatarRoot, log);
                }
                catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
                {
                    log.Warn($"[AvatarVCS] Failed to apply material settings for slot {materialSetting.slot} "
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
