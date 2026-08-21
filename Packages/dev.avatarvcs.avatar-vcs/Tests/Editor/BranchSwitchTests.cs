using System.Linq;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// Covers phase 3 task 10 from DesignDoc_avatar-vcs.md section 7.3:
    /// switching back and forth between two branches restores each one's
    /// correct state.
    /// </summary>
    public class BranchSwitchTests
    {
        private const string TestAssetDir = "Assets/AvatarVcsTests_Branch_Temp";
        private GameObject prefabSourceLong;
        private GameObject prefabSourceShort;
        private string prefabLongPath;
        private string prefabShortPath;

        private GameObject avatarRoot;
        private string avatarGuid;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (!AssetDatabase.IsValidFolder(TestAssetDir))
                AssetDatabase.CreateFolder("Assets", "AvatarVcsTests_Branch_Temp");

            prefabLongPath = $"{TestAssetDir}/HairLong.prefab";
            var sourceLong = new GameObject("HairLong");
            prefabSourceLong = PrefabUtility.SaveAsPrefabAsset(sourceLong, prefabLongPath);
            Object.DestroyImmediate(sourceLong);

            prefabShortPath = $"{TestAssetDir}/HairShort.prefab";
            var sourceShort = new GameObject("HairShort");
            prefabSourceShort = PrefabUtility.SaveAsPrefabAsset(sourceShort, prefabShortPath);
            Object.DestroyImmediate(sourceShort);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (AssetDatabase.IsValidFolder(TestAssetDir))
                AssetDatabase.DeleteAsset(TestAssetDir);
        }

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
        public void SwitchBranch_RoundTrips_BetweenTwoBranches_RestoringCorrectState()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var root = ContainerManager.FindRoot(avatarRoot);

            var hairLong = ContainerManager.CreateContainer(root, "hair");
            PrefabUtility.InstantiatePrefab(prefabSourceLong, hairLong.transform);
            var mainCommit = BranchManager.Commit(avatarRoot, "hair long");

            BranchManager.CreateBranch(avatarRoot, "hair-short", mainCommit.commitId);
            var switchToShort = BranchManager.SwitchBranch(avatarRoot, "hair-short");
            Assert.IsTrue(switchToShort.IsSuccess, "switch to hair-short should succeed");

            var rootAfterSwitch = ContainerManager.FindRoot(avatarRoot);
            Object.DestroyImmediate(rootAfterSwitch.transform.Find("hair").gameObject);
            var hairShort = ContainerManager.CreateContainer(rootAfterSwitch, "hair");
            PrefabUtility.InstantiatePrefab(prefabSourceShort, hairShort.transform);
            BranchManager.Commit(avatarRoot, "hair short");

            var backToMain = BranchManager.SwitchBranch(avatarRoot, "main");
            Assert.IsTrue(backToMain.IsSuccess);
            AssertHairPrefab(prefabLongPath);

            var toShortAgain = BranchManager.SwitchBranch(avatarRoot, "hair-short");
            Assert.IsTrue(toShortAgain.IsSuccess);
            AssertHairPrefab(prefabShortPath);
        }

        [Test]
        public void RestoreToCommit_ReturnsToAnOlderCommit_OnTheSameBranch()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var root = ContainerManager.FindRoot(avatarRoot);

            var hairLong = ContainerManager.CreateContainer(root, "hair");
            PrefabUtility.InstantiatePrefab(prefabSourceLong, hairLong.transform);
            var commitA = BranchManager.Commit(avatarRoot, "hair long");

            Object.DestroyImmediate(root.transform.Find("hair").gameObject);
            var hairShort = ContainerManager.CreateContainer(root, "hair");
            PrefabUtility.InstantiatePrefab(prefabSourceShort, hairShort.transform);
            BranchManager.Commit(avatarRoot, "hair short");

            var result = BranchManager.RestoreToCommit(avatarRoot, commitA.commitId);

            Assert.IsTrue(result.IsSuccess);
            AssertHairPrefab(prefabLongPath);

            var config = CommitStore.LoadConfig(avatarGuid);
            Assert.AreEqual("main", config.currentBranch, "RestoreToCommit stays on the current branch");
            Assert.AreEqual(commitA.commitId, config.branches.First(b => b.name == "main").commitId,
                "the branch head moves to the restored commit, not an auto-commit safety snapshot");
        }

        // Checkout no longer takes a safety-net auto-commit (Ctrl+Z is the
        // recovery path for uncommitted work instead) -- these guard against
        // that regressing back to spamming history with [auto] commits.

        [Test]
        public void SwitchBranch_DoesNotCreateAnAutoCommit()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var root = ContainerManager.FindRoot(avatarRoot);
            ContainerManager.CreateContainer(root, "hair");
            var mainCommit = BranchManager.Commit(avatarRoot, "hair long");
            BranchManager.CreateBranch(avatarRoot, "hair-short", mainCommit.commitId);

            var countBefore = CommitStore.LoadIndex(avatarGuid).entries.Count;
            var result = BranchManager.SwitchBranch(avatarRoot, "hair-short");

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.AutoCommitId);
            Assert.AreEqual(countBefore, CommitStore.LoadIndex(avatarGuid).entries.Count);
        }

        [Test]
        public void RestoreToCommit_DoesNotCreateAnAutoCommit()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var root = ContainerManager.FindRoot(avatarRoot);
            ContainerManager.CreateContainer(root, "hair");
            var commitA = BranchManager.Commit(avatarRoot, "hair long");
            BranchManager.Commit(avatarRoot, "hair long again");

            var countBefore = CommitStore.LoadIndex(avatarGuid).entries.Count;
            var result = BranchManager.RestoreToCommit(avatarRoot, commitA.commitId);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.AutoCommitId);
            Assert.AreEqual(countBefore, CommitStore.LoadIndex(avatarGuid).entries.Count);
        }

        private void AssertHairPrefab(string expectedPrefabPath)
        {
            var root = ContainerManager.FindRoot(avatarRoot);
            var hairContainer = root.transform.Find("hair");
            Assert.IsNotNull(hairContainer, "hair container should exist after checkout");
            Assert.AreEqual(1, hairContainer.childCount);

            var instanceGuid = ContainerManager.GetPrefabGuid(hairContainer.GetChild(0).gameObject);
            var expectedGuid = AssetDatabase.AssetPathToGUID(expectedPrefabPath);
            Assert.AreEqual(expectedGuid, instanceGuid);
        }
    }
}
