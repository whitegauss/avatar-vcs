using System;
using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using AvatarVcs.Editor.Model;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// Robustness tests for BranchManager, CommitStore, and SnapshotDiffer:
    /// multi-avatar storage isolation, non-existent branches/commits, null diffs,
    /// and compound diff scenarios.
    /// </summary>
    public class HistoryRobustnessTests
    {
        private readonly List<GameObject> spawned = new();
        private readonly List<string> createdAvatarGuids = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var guid in createdAvatarGuids)
                CommitStore.DeleteAvatarHistory(guid);
            createdAvatarGuids.Clear();

            foreach (var go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();
        }

        private GameObject SpawnAvatar(string name)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            var guid = ContainerManager.GetAvatarGuid(go);
            createdAvatarGuids.Add(guid);
            return go;
        }

        #region CommitStore Isolation & Edge Cases

        [Test]
        public void CommitStore_MultiAvatarHistory_IsStrictlyIsolated()
        {
            var avatarA = SpawnAvatar("Avatar_A");
            var avatarB = SpawnAvatar("Avatar_B");

            var guidA = ContainerManager.GetAvatarGuid(avatarA);
            var guidB = ContainerManager.GetAvatarGuid(avatarB);
            Assert.AreNotEqual(guidA, guidB);

            BranchManager.Commit(avatarA, "commit A1");
            BranchManager.Commit(avatarA, "commit A2");

            BranchManager.Commit(avatarB, "commit B1");

            var indexA = CommitStore.LoadIndex(guidA);
            var indexB = CommitStore.LoadIndex(guidB);

            Assert.AreEqual(2, indexA.entries.Count);
            Assert.AreEqual(1, indexB.entries.Count);
            CollectionAssert.AreEquivalent(new[] { "commit A1", "commit A2" }, indexA.entries.Select(e => e.message));
            CollectionAssert.AreEquivalent(new[] { "commit B1" }, indexB.entries.Select(e => e.message));
        }

        [Test]
        public void CommitStore_NonExistentCommit_ReturnsNull()
        {
            var loaded = CommitStore.LoadCommit("non_existent_avatar_guid", "fake_commit_id");
            Assert.IsNull(loaded);
        }

        [Test]
        public void CommitStore_NonExistentIndexAndConfig_ReturnsEmptyDefaultsWithoutThrowing()
        {
            var index = CommitStore.LoadIndex("non_existent_avatar_guid");
            Assert.IsNotNull(index);
            Assert.IsEmpty(index.entries);

            var config = CommitStore.LoadConfig("non_existent_avatar_guid");
            Assert.IsNotNull(config);
            Assert.AreEqual("main", config.currentBranch);
            Assert.IsEmpty(config.branches);
        }

        #endregion

        #region BranchManager Edge Cases

        [Test]
        public void BranchManager_SwitchToNonExistentBranch_ThrowsInvalidOperationException()
        {
            var avatar = SpawnAvatar("Avatar");
            BranchManager.Commit(avatar, "init");

            Assert.Throws<InvalidOperationException>(() =>
                BranchManager.SwitchBranch(avatar, "non_existent_branch"));
        }

        [Test]
        public void BranchManager_RestoreToNonExistentCommit_ThrowsInvalidOperationException()
        {
            var avatar = SpawnAvatar("Avatar");
            BranchManager.Commit(avatar, "init");

            Assert.Throws<InvalidOperationException>(() =>
                BranchManager.RestoreToCommit(avatar, "invalid_commit_id_000000"));
        }

        [Test]
        public void BranchManager_CreateBranch_WithExistingName_IsIdempotent()
        {
            var avatar = SpawnAvatar("Avatar");
            var commit = BranchManager.Commit(avatar, "init");

            BranchManager.CreateBranch(avatar, "feature", commit.commitId);
            BranchManager.CreateBranch(avatar, "feature", commit.commitId); // Repeat

            var guid = ContainerManager.GetAvatarGuid(avatar);
            var config = CommitStore.LoadConfig(guid);

            Assert.AreEqual(1, config.branches.Count(b => b.name == "feature"));
        }

        #endregion

        #region SnapshotDiffer Edge Cases

        [Test]
        public void SnapshotDiffer_NullCommits_HandledSafely()
        {
            var diffsBothNull = SnapshotDiffer.Diff(null, null);
            Assert.IsNotNull(diffsBothNull);
            Assert.IsEmpty(diffsBothNull);

            var commit = new Commit
            {
                containers =
                {
                    new ContainerSnapshot { containerId = "c1", containerGuid = "g1" },
                },
            };

            var diffsBeforeNull = SnapshotDiffer.Diff(null, commit);
            Assert.AreEqual(1, diffsBeforeNull.Count);
            Assert.AreEqual(DiffKind.Added, diffsBeforeNull[0].kind);

            var diffsAfterNull = SnapshotDiffer.Diff(commit, null);
            Assert.AreEqual(1, diffsAfterNull.Count);
            Assert.AreEqual(DiffKind.Removed, diffsAfterNull[0].kind);
        }

        [Test]
        public void SnapshotDiffer_CompoundDiff_ClassifiesAllKindsSimultaneously()
        {
            var before = new Commit
            {
                containers =
                {
                    new ContainerSnapshot { containerId = "kept_unchanged", containerGuid = "g1" },
                    new ContainerSnapshot { containerId = "to_be_removed", containerGuid = "g2" },
                    new ContainerSnapshot { containerId = "to_be_changed", containerGuid = "g3", localPosition = Vector3.zero },
                },
            };

            var after = new Commit
            {
                containers =
                {
                    new ContainerSnapshot { containerId = "kept_unchanged", containerGuid = "g1" },
                    new ContainerSnapshot { containerId = "newly_added", containerGuid = "g4" },
                    new ContainerSnapshot { containerId = "to_be_changed", containerGuid = "g3", localPosition = Vector3.up },
                },
            };

            var diffs = SnapshotDiffer.Diff(before, after);

            Assert.AreEqual(4, diffs.Count);

            var unchanged = diffs.Single(d => d.containerId == "kept_unchanged");
            Assert.AreEqual(DiffKind.Unchanged, unchanged.kind);

            var removed = diffs.Single(d => d.containerId == "to_be_removed");
            Assert.AreEqual(DiffKind.Removed, removed.kind);

            var added = diffs.Single(d => d.containerId == "newly_added");
            Assert.AreEqual(DiffKind.Added, added.kind);

            var changed = diffs.Single(d => d.containerId == "to_be_changed");
            Assert.AreEqual(DiffKind.Changed, changed.kind);
            Assert.IsTrue(changed.changeNotes.Any(n => n.Contains("transform")));
        }

        #endregion

        #region CheckoutOperation Edge Cases

        [Test]
        public void CheckoutOperation_MaterialApplyFailure_WarnsButDoesNotAbortCheckout()
        {
            var avatar = SpawnAvatar("Avatar");
            var root = ContainerManager.EnsureRoot(avatar);
            ContainerManager.CreateContainer(root, "outfit_a");
            var commit = BranchManager.Commit(avatar, "init");

            // Deliberately unresolvable target -- MaterialSettingsApplier
            // throws for this instead of silently no-op'ing.
            commit.materialSettings.Add(new MaterialSettingsState
            {
                targetPath = "NonExistentPath",
                slot = 0,
                sourceMaterialGuid = "deadbeef00000000000000000000000",
                shader = "lilToon",
            });

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Failed to apply material settings"));

            var result = CheckoutOperation.CheckoutWithoutAutoCommit(commit, avatar);

            Assert.IsTrue(result.IsSuccess,
                "one bad material setting must not abort the whole checkout, leaving containers already destroyed/regenerated but nothing else applied");
        }

        #endregion
    }
}
