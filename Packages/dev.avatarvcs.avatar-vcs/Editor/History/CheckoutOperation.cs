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
            // Flush in a finally so warnings collected before an unexpected
            // throw still reach the console, as they did pre-KAN-20.
            var log = new DiagnosticLog();
            try
            {
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

                return CheckoutResult.Success(autoCommit.commitId, versionWarnings, log.Entries);
            }
            finally
            {
                UnityDiagnosticSink.Flush(log);
            }
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

            // Flush in a finally so warnings collected before an unexpected
            // throw still reach the console (see Checkout above).
            var log = new DiagnosticLog();
            try
            {
                var versionWarnings = ApplyCommitToScene(commit, avatarRoot, configRoot, avatarGuid, log);
                return CheckoutResult.Success(null, versionWarnings, log.Entries);
            }
            finally
            {
                UnityDiagnosticSink.Flush(log);
            }
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

        // Every generatedGuid a checkout can populate/reuse: the Track
        // Properties materialSettings plus, since KAN-73, each container's own
        // inner materialSettings. Snapshotted before apply and re-read after
        // to decide whether the commit needs re-persisting.
        private static List<string> AllGeneratedMaterialGuids(Commit commit) =>
            (commit.materialSettings ?? new List<MaterialSettingsState>())
                .Where(m => m != null)
                .Select(m => m.generatedGuid)
                .Concat((commit.containers ?? new List<ContainerSnapshot>())
                    .Where(c => c != null)
                    .SelectMany(c => (c.materialSettings ?? new List<MaterialSettingsState>())
                        .Where(m => m != null)
                        .Select(m => m.generatedGuid)))
                .ToList();

        private static List<string> ApplyCommitToScene(Commit commit, GameObject avatarRoot, GameObject configRoot, string avatarGuid, DiagnosticLog log)
        {
            var priorGeneratedGuids = AllGeneratedMaterialGuids(commit);

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
            // Every list below is `?? new List<>()` + a null-element skip for
            // the reason CommitStore documents: commit JSON is hand-editable
            // and merge-corruptible, and JsonUtility turns an explicit
            // `"containers": null` / `[null]` into exactly that. Aborting here
            // is the worst possible moment -- the old containers are already
            // destroyed, so the avatar would be left gutted.
            var restoredContainers = (commit.containers ?? new List<ContainerSnapshot>())
                .Where(snapshot => snapshot != null)
                .Select(snapshot => (snapshot, go: ContainerRestore.InstantiateContainerStructure(snapshot, configRoot, log)))
                .ToList();
            foreach (var (snapshot, go) in restoredContainers)
                ContainerRestore.ApplyContainerComponents(snapshot, go, avatarRoot, log);

            foreach (var reference in commit.avatarReferences ?? new List<AvatarReferenceState>())
            {
                if (reference == null)
                {
                    log.Warn("[AvatarVCS] Null avatarReferences entry in the commit; skipped.");
                    continue;
                }

                AvatarReferenceApplier.Apply(reference, avatarRoot.transform, log);
            }

            foreach (var materialSetting in commit.materialSettings ?? new List<MaterialSettingsState>())
            {
                if (materialSetting == null)
                {
                    log.Warn("[AvatarVCS] Null materialSettings entry in the commit; skipped.");
                    continue;
                }

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

            // Apply (Track Properties and, since KAN-73, container-inner)
            // populates/reuses each entry's generatedGuid in place; if any of
            // them are new, persist the commit so future checkouts reuse the
            // same duplicates instead of generating more, and so DeleteCommit
            // can GC them.
            var generatedGuidsAfter = AllGeneratedMaterialGuids(commit);
            if (!generatedGuidsAfter.SequenceEqual(priorGeneratedGuids))
            {
                commit.generatedAssets = generatedGuidsAfter
                    .Where(g => !string.IsNullOrEmpty(g))
                    .Distinct()
                    .ToList();
                CommitStore.SaveCommit(avatarGuid, commit);
            }

            return AssetVersionChecker.CheckForChanges(commit.assetVersions);
        }
    }
}
