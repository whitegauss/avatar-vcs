using System;
using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Core.Diff;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using AvatarVcs.Core.Model;
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

        // Valid-shaped (32-char lowercase hex, as CommitStore now requires --
        // see IsValidIdentifierShape) but nonexistent ids: these tests are
        // about "no history written yet for this avatar", not about
        // malformed-id handling (covered separately below).
        private const string NonExistentAvatarGuid = "deadbeefdeadbeefdeadbeefdeadbeef";
        private static readonly string NonExistentCommitId = new('0', 32);

        [Test]
        public void CommitStore_NonExistentCommit_ReturnsNull()
        {
            var loaded = CommitStore.LoadCommit(NonExistentAvatarGuid, NonExistentCommitId);
            Assert.IsNull(loaded);
        }

        [Test]
        public void CommitStore_NonExistentIndexAndConfig_ReturnsEmptyDefaultsWithoutThrowing()
        {
            var index = CommitStore.LoadIndex(NonExistentAvatarGuid);
            Assert.IsNotNull(index);
            Assert.IsEmpty(index.entries);

            var config = CommitStore.LoadConfig(NonExistentAvatarGuid);
            Assert.IsNotNull(config);
            Assert.AreEqual("main", config.currentBranch);
            Assert.IsEmpty(config.branches);
        }

        [Test]
        public void CommitStore_MalformedIdentifierShape_TreatedAsNotFound_WithoutThrowing()
        {
            // Defense against path-traversal-shaped values (e.g. from a
            // hand-edited or corrupted commit/index file): CommitStore
            // treats these the same as "not found", not an exception.
            Assert.IsNull(CommitStore.LoadCommit(NonExistentAvatarGuid, "../../../outside"));
            Assert.DoesNotThrow(() => CommitStore.DeleteCommit(NonExistentAvatarGuid, "../../../outside"));
        }

        [Test]
        public void CommitStore_GetAvatarDir_RejectsPathTraversalShapedAvatarGuid()
        {
            // avatarGuid identifies *which* avatar's history a call operates
            // on, so unlike commitId it can't be treated as a harmless
            // "not found" -- GetAvatarDir (and everything built on it) must
            // refuse to turn a malformed value into a path at all.
            Assert.Throws<ArgumentException>(() => CommitStore.GetAvatarDir("../../../outside"));
            Assert.Throws<ArgumentException>(() => CommitStore.LoadIndex("../../../outside"));
        }

        [Test]
        public void CommitStore_CorruptCommitFile_ReturnsNullInsteadOfThrowing()
        {
            // Simulates a crash mid-write or a bad manual/merge edit leaving
            // truncated/malformed JSON on disk.
            var avatar = SpawnAvatar("Avatar");
            var commit = BranchManager.Commit(avatar, "init");
            var avatarGuid = ContainerManager.GetAvatarGuid(avatar);
            var commitPath = $"{CommitStore.GetAvatarDir(avatarGuid)}/commits/{commit.commitId}.json";
            System.IO.File.WriteAllText(commitPath, "{ not valid json");

            Commit loaded = null;
            Assert.DoesNotThrow(() => loaded = CommitStore.LoadCommit(avatarGuid, commit.commitId));
            Assert.IsNull(loaded);
        }

        [Test]
        public void CommitStore_CommitFromNewerSchema_ReturnsNullWithWarning()
        {
            var avatar = SpawnAvatar("Avatar");
            var commit = BranchManager.Commit(avatar, "init");
            var avatarGuid = ContainerManager.GetAvatarGuid(avatar);
            var commitPath = $"{CommitStore.GetAvatarDir(avatarGuid)}/commits/{commit.commitId}.json";

            var json = System.IO.File.ReadAllText(commitPath)
                .Replace($"\"schemaVersion\": {Commit.CurrentSchemaVersion}", "\"schemaVersion\": 99");
            System.IO.File.WriteAllText(commitPath, json);

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("schemaVersion 99, newer than this build supports"));

            Commit loaded = null;
            Assert.DoesNotThrow(() => loaded = CommitStore.LoadCommit(avatarGuid, commit.commitId));
            Assert.IsNull(loaded);
        }

        [Test]
        public void CommitStore_CorruptIndexFile_ReturnsEmptyIndexInsteadOfThrowing()
        {
            var avatar = SpawnAvatar("Avatar");
            BranchManager.Commit(avatar, "init");
            var avatarGuid = ContainerManager.GetAvatarGuid(avatar);
            var indexPath = $"{CommitStore.GetAvatarDir(avatarGuid)}/index.json";
            System.IO.File.WriteAllText(indexPath, "not json at all");

            CommitIndex index = null;
            Assert.DoesNotThrow(() => index = CommitStore.LoadIndex(avatarGuid));
            Assert.IsNotNull(index);
            Assert.IsEmpty(index.entries);
        }

        [Test]
        public void CommitStore_SaveCommit_DoesNotLeaveTempFileBehind()
        {
            var avatar = SpawnAvatar("Avatar");
            var commit = BranchManager.Commit(avatar, "init");
            var avatarGuid = ContainerManager.GetAvatarGuid(avatar);
            var commitPath = $"{CommitStore.GetAvatarDir(avatarGuid)}/commits/{commit.commitId}.json";

            Assert.IsTrue(System.IO.File.Exists(commitPath));
            Assert.IsFalse(System.IO.File.Exists($"{commitPath}.tmp"), "the atomic-write temp file must be swapped away, not left behind");
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

        [Test]
        public void BranchManager_IsValidBranchName_RejectsUnsafeNames()
        {
            Assert.IsFalse(BranchManager.IsValidBranchName(null));
            Assert.IsFalse(BranchManager.IsValidBranchName(""));
            Assert.IsFalse(BranchManager.IsValidBranchName("  padded  "));
            Assert.IsFalse(BranchManager.IsValidBranchName(".hidden"));
            Assert.IsFalse(BranchManager.IsValidBranchName("-flag-like"));
            Assert.IsFalse(BranchManager.IsValidBranchName("has/slash"));
            Assert.IsFalse(BranchManager.IsValidBranchName("has\\backslash"));
            Assert.IsFalse(BranchManager.IsValidBranchName("has:colon"));
            Assert.IsFalse(BranchManager.IsValidBranchName("has*star"));
            Assert.IsFalse(BranchManager.IsValidBranchName("has\"quote"));
            Assert.IsFalse(BranchManager.IsValidBranchName("has\tcontrol"));
        }

        [Test]
        public void BranchManager_IsValidBranchName_AcceptsSafeNames()
        {
            Assert.IsTrue(BranchManager.IsValidBranchName("main"));
            Assert.IsTrue(BranchManager.IsValidBranchName("hair-long"));
            Assert.IsTrue(BranchManager.IsValidBranchName("outfit_v2"));
            Assert.IsTrue(BranchManager.IsValidBranchName("髪ロング")); // Japanese is fine
        }

        [Test]
        public void BranchManager_CreateBranch_RejectsUnsafeName()
        {
            var avatar = SpawnAvatar("Avatar");
            BranchManager.Commit(avatar, "init");

            Assert.Throws<ArgumentException>(() => BranchManager.CreateBranch(avatar, "has/slash"));
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

        #region CommitStore & BranchManager Additional Error Cases

        [Test]
        public void CommitStore_SaveCommit_NullCommit_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => CommitStore.SaveCommit(NonExistentAvatarGuid, null));
        }

        [Test]
        public void CommitStore_SaveCommit_InvalidCommitIdShape_ThrowsArgumentException()
        {
            var commit = new Commit { commitId = "too_short_id" };
            Assert.Throws<ArgumentException>(() => CommitStore.SaveCommit(NonExistentAvatarGuid, commit));
        }

        [Test]
        public void CommitStore_DeleteCommit_BranchHeadWithoutForce_ThrowsInvalidOperationException()
        {
            var avatar = SpawnAvatar("Avatar");
            var commit = BranchManager.Commit(avatar, "init");
            var avatarGuid = ContainerManager.GetAvatarGuid(avatar);

            Assert.Throws<InvalidOperationException>(() => CommitStore.DeleteCommit(avatarGuid, commit.commitId, force: false));
        }

        [Test]
        public void CommitStore_DeleteCommits_DuplicateCommitIdsInIndex_DeletesSafelyWithoutCrashing()
        {
            var avatar = SpawnAvatar("Avatar");
            var first = BranchManager.Commit(avatar, "first");
            var second = BranchManager.Commit(avatar, "second");
            var avatarGuid = ContainerManager.GetAvatarGuid(avatar);

            // Corrupt index to contain duplicate entries
            var index = CommitStore.LoadIndex(avatarGuid);
            index.entries.Add(new CommitIndexEntry
            {
                commitId = first.commitId,
                message = "duplicate entry",
                timestamp = first.timestamp,
            });
            var dir = CommitStore.GetAvatarDir(avatarGuid);
            System.IO.File.WriteAllText($"{dir}/index.json", JsonUtility.ToJson(index, true));

            List<string> blocked = null;
            Assert.DoesNotThrow(() => blocked = CommitStore.DeleteCommits(avatarGuid, new[] { first.commitId }, force: true));
            Assert.IsNotNull(blocked);
            Assert.IsEmpty(blocked);
            Assert.IsNull(CommitStore.LoadCommit(avatarGuid, first.commitId));
        }

        [Test]
        public void BranchManager_CreateBranch_NullOrEmptyName_ThrowsArgumentException()
        {
            var avatar = SpawnAvatar("Avatar");
            BranchManager.Commit(avatar, "init");

            Assert.Throws<ArgumentException>(() => BranchManager.CreateBranch(avatar, null));
            Assert.Throws<ArgumentException>(() => BranchManager.CreateBranch(avatar, ""));
        }

        [Test]
        public void BranchManager_CreateBranch_ExistingBranchDifferentCommit_ThrowsInvalidOperationException()
        {
            var avatar = SpawnAvatar("Avatar");
            var commit1 = BranchManager.Commit(avatar, "init 1");
            var commit2 = BranchManager.Commit(avatar, "init 2");

            BranchManager.CreateBranch(avatar, "feature", commit1.commitId);
            Assert.Throws<InvalidOperationException>(() => BranchManager.CreateBranch(avatar, "feature", commit2.commitId));
        }

        [Test]
        public void BranchManager_SwitchBranch_BranchHasNoCommits_ThrowsInvalidOperationException()
        {
            var avatar = SpawnAvatar("Avatar");
            var avatarGuid = ContainerManager.GetAvatarGuid(avatar);
            var config = CommitStore.LoadConfig(avatarGuid);
            config.branches.Add(new BranchEntry { name = "empty-branch", commitId = null });
            CommitStore.SaveConfig(avatarGuid, config);

            Assert.Throws<InvalidOperationException>(() => BranchManager.SwitchBranch(avatar, "empty-branch"));
        }

        [Test]
        public void BranchManager_SwitchBranch_CommitCouldNotBeLoaded_ThrowsInvalidOperationException()
        {
            var avatar = SpawnAvatar("Avatar");
            var avatarGuid = ContainerManager.GetAvatarGuid(avatar);
            var config = CommitStore.LoadConfig(avatarGuid);
            config.branches.Add(new BranchEntry { name = "missing-commit-branch", commitId = new string('a', 32) });
            CommitStore.SaveConfig(avatarGuid, config);

            Assert.Throws<InvalidOperationException>(() => BranchManager.SwitchBranch(avatar, "missing-commit-branch"));
        }

        #endregion
    }
}
