using System.Linq;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using AvatarVcs.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// Covers phase 3 task 9 from DesignDoc_avatar-vcs.md section 7.3:
    /// commits are persisted file-based (ProjectSettings/AvatarVcs) and
    /// listable via the index without loading every commit's full body.
    /// </summary>
    public class CommitHistoryTests
    {
        private GameObject avatarRoot;
        private string avatarGuid;

        [SetUp]
        public void SetUp()
        {
            avatarRoot = new GameObject("TestAvatar");
        }

        [TearDown]
        public void TearDown()
        {
            if (avatarGuid != null)
                CommitStore.DeleteAvatarHistory(avatarGuid);
            if (avatarRoot != null)
                Object.DestroyImmediate(avatarRoot);
        }

        [Test]
        public void SaveCommit_ThenLoadCommit_RoundTrips()
        {
            var root = ContainerManager.EnsureRoot(avatarRoot);
            ContainerManager.CreateContainer(root, "outfit_a");
            avatarGuid = root.GetComponent<AvatarVcsRoot>().AvatarGuid;

            var commit = CommitBuilder.CreateCommit(avatarRoot, "first commit", "main", null);
            CommitStore.SaveCommit(avatarGuid, commit);

            var loaded = CommitStore.LoadCommit(avatarGuid, commit.commitId);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(commit.commitId, loaded.commitId);
            Assert.AreEqual("first commit", loaded.message);
            Assert.AreEqual(1, loaded.containers.Count);
            Assert.AreEqual("outfit_a", loaded.containers[0].containerId);
        }

        [Test]
        public void SaveCommit_UpdatesIndex_ListableWithoutLoadingFullBody()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);

            var first = CommitBuilder.CreateCommit(avatarRoot, "first", "main", null);
            CommitStore.SaveCommit(avatarGuid, first);
            var second = CommitBuilder.CreateCommit(avatarRoot, "second", "main", first.commitId);
            CommitStore.SaveCommit(avatarGuid, second);

            var index = CommitStore.LoadIndex(avatarGuid);

            Assert.AreEqual(2, index.entries.Count);
            CollectionAssert.AreEquivalent(new[] { "first", "second" }, index.entries.Select(e => e.message));
            Assert.AreEqual(first.commitId, index.entries.First(e => e.message == "second").parentCommitId);
        }

        [Test]
        public void BranchManagerCommit_UpdatesCurrentBranchHead()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);

            var commit = BranchManager.Commit(avatarRoot, "via BranchManager");

            var config = CommitStore.LoadConfig(avatarGuid);
            var head = config.branches.First(b => b.name == "main");
            Assert.AreEqual(commit.commitId, head.commitId);
        }
    }
}
