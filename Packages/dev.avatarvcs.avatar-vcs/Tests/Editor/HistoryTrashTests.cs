using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AvatarVcs.Core.History;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using NUnit.Framework;
using UnityEngine;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// The orphan sweep is not allowed to destroy history. It used to call
    /// CommitStore.DeleteAvatarHistory, and a real beta project lost four
    /// histories that way: the VRChat SDK's "Auto Fix" destroyed the
    /// AvatarVcsRoot carrying the avatarGuid, every history then looked
    /// orphaned, and the sweep -- on by default -- removed them for good.
    /// These tests pin the replacement: the folders move under
    /// CommitPaths.TrashRoot with their commits intact.
    /// </summary>
    public class HistoryTrashTests
    {
        private readonly List<GameObject> spawned = new();
        private readonly List<string> createdAvatarGuids = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var guid in createdAvatarGuids)
            {
                CommitStore.DeleteAvatarHistory(guid);

                // Production never prunes the trash (that is the whole point),
                // so a test that fills it has to carry its own leftovers out.
                foreach (var dir in TrashDirsFor(guid))
                    Directory.Delete(dir, recursive: true);
            }
            createdAvatarGuids.Clear();

            foreach (var go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();
        }

        private static string[] TrashDirsFor(string avatarGuid) =>
            Directory.Exists(CommitPaths.TrashRoot)
                ? Directory.GetDirectories(CommitPaths.TrashRoot, $"{avatarGuid}-*")
                : Array.Empty<string>();

        private string SpawnCommittedAvatar(string name, int commits)
        {
            var go = new GameObject(name);
            spawned.Add(go);

            var guid = ContainerManager.GetAvatarGuid(go);
            createdAvatarGuids.Add(guid);

            for (var i = 0; i < commits; i++)
                BranchManager.Commit(go, $"{name} commit {i + 1}");

            return guid;
        }

        private static AvatarHistoryInfo Orphan(string avatarGuid, string newestCommitTimestamp) => new()
        {
            avatarGuid = avatarGuid,
            isReferenced = false,
            commitCount = 1,
            newestCommitTimestamp = newestCommitTimestamp,
            byteSize = 1,
        };

        [Test]
        public void TrashAvatarHistory_MovesTheHistoryOutOfTheAvatarsFolderWithItsCommits()
        {
            var guid = SpawnCommittedAvatar("Trash_Move", commits: 2);
            Assert.IsTrue(Directory.Exists(CommitPaths.AvatarDir(guid)), "the avatar should have a stored history to begin with");

            var target = CommitStore.TrashAvatarHistory(guid);

            Assert.IsNotNull(target, "trashing an existing history should report where it went");
            StringAssert.StartsWith(CommitPaths.TrashRoot, target);
            Assert.IsFalse(Directory.Exists(CommitPaths.AvatarDir(guid)), "the history must no longer be where the tool looks for it");

            var commits = Directory.GetFiles(Path.Combine(target, "commits"), "*.json");
            Assert.AreEqual(2, commits.Length, "both commit files should have moved along with the folder");
            StringAssert.Contains(guid, File.ReadAllText(commits[0]), "the moved commit file should still be the same readable JSON");
            Assert.IsTrue(File.Exists(Path.Combine(target, "index.json")), "the index should have moved too");
        }

        [Test]
        public void TrashAvatarHistory_TwiceForTheSameAvatar_KeepsBothCopies()
        {
            var go = new GameObject("Trash_Twice");
            spawned.Add(go);
            var guid = ContainerManager.GetAvatarGuid(go);
            createdAvatarGuids.Add(guid);

            BranchManager.Commit(go, "first life");
            var first = CommitStore.TrashAvatarHistory(guid);

            // Committing again re-creates avatars/{guid} from scratch, which is
            // exactly what a user does after re-running Ensure Root -- and the
            // second trashing then lands in the same second as the first.
            BranchManager.Commit(go, "second life");
            var second = CommitStore.TrashAvatarHistory(guid);

            Assert.IsNotNull(first);
            Assert.IsNotNull(second);
            Assert.AreNotEqual(first, second, "the second copy must not overwrite the first");
            Assert.AreEqual(2, TrashDirsFor(guid).Length, "both trashed copies should still be on disk");
        }

        [Test]
        public void TrashAvatarHistory_WithNoStoredHistory_ReportsNothingAndCreatesNothing()
        {
            var guid = Guid.NewGuid().ToString("N");

            Assert.IsNull(CommitStore.TrashAvatarHistory(guid));
            Assert.IsEmpty(TrashDirsFor(guid), "there was nothing to move, so nothing should have been created");
        }

        [Test]
        public void TrashedHistory_IsNoLongerReportedAsStored()
        {
            var guid = SpawnCommittedAvatar("Trash_Inventory", commits: 1);
            Assert.IsTrue(AvatarHistoryInventory.Scan().Any(h => h.avatarGuid == guid),
                "a committed avatar's history should be in the inventory");

            CommitStore.TrashAvatarHistory(guid);

            Assert.IsFalse(AvatarHistoryInventory.Scan().Any(h => h.avatarGuid == guid),
                "the trash lives outside AvatarsRoot, so a trashed history must not look like a stored one");
        }

        [Test]
        public void Cleanup_Run_MovesTheSweptHistoryToTheTrashInsteadOfDeletingIt()
        {
            var older = SpawnCommittedAvatar("Trash_Sweep_Older", commits: 1);
            var newer = SpawnCommittedAvatar("Trash_Sweep_Newer", commits: 1);

            // Both unreferenced; the retention rule keeps the newest one, so
            // the older is the one the sweep acts on.
            var trashed = AvatarHistoryCleanup.Run(new[]
            {
                Orphan(older, "2026-01-01T00:00:00Z"),
                Orphan(newer, "2026-02-01T00:00:00Z"),
            });

            Assert.AreEqual(new[] { older }, trashed.Select(h => h.avatarGuid).ToArray(),
                "only the older orphan should have been swept");

            Assert.IsFalse(Directory.Exists(CommitPaths.AvatarDir(older)), "the swept history should have left the avatars folder");
            var moved = TrashDirsFor(older);
            Assert.AreEqual(1, moved.Length, "the swept history should be in the trash, not deleted");
            Assert.AreEqual(1, Directory.GetFiles(Path.Combine(moved[0], "commits"), "*.json").Length,
                "its commit must still be readable after the sweep");

            Assert.IsTrue(Directory.Exists(CommitPaths.AvatarDir(newer)), "the retained orphan should not have been touched");
            Assert.IsEmpty(TrashDirsFor(newer));
        }
    }
}
