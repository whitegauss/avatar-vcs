using System.Linq;
using AvatarVcs.Core.History;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// Covers DesignDoc_avatar-vcs.md section 5.2 (branch comparison mode):
    /// toggling between two commits to eyeball a difference must not create
    /// a commit per toggle, but still needs to apply each side's content
    /// correctly and still enforce the same missing-prefab pre-flight check
    /// as a normal checkout.
    /// </summary>
    public class CompareModeTests
    {
        private const string TestAssetDir = "Assets/AvatarVcsTests_Compare_Temp";
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
                AssetDatabase.CreateFolder("Assets", "AvatarVcsTests_Compare_Temp");

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
        public void CheckoutWithoutAutoCommit_TogglingBackAndForth_NeverCreatesAnyCommit()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var root = ContainerManager.FindRoot(avatarRoot);

            var hairLong = ContainerManager.CreateContainer(root, "hair");
            PrefabUtility.InstantiatePrefab(prefabSourceLong, hairLong.transform);
            var commitA = BranchManager.Commit(avatarRoot, "hair long");

            Object.DestroyImmediate(root.transform.Find("hair").gameObject);
            var hairShort = ContainerManager.CreateContainer(root, "hair");
            PrefabUtility.InstantiatePrefab(prefabSourceShort, hairShort.transform);
            var commitB = BranchManager.Commit(avatarRoot, "hair short");

            var commitCountBefore = CommitStore.LoadIndex(avatarGuid).entries.Count;

            for (var i = 0; i < 4; i++)
            {
                var toA = CheckoutOperation.CheckoutWithoutAutoCommit(commitA, avatarRoot);
                Assert.IsTrue(toA.IsSuccess);
                Assert.IsNull(toA.AutoCommitId, "no auto-commit should be taken while toggling");
                AssertHairPrefab(prefabLongPath);

                var toB = CheckoutOperation.CheckoutWithoutAutoCommit(commitB, avatarRoot);
                Assert.IsTrue(toB.IsSuccess);
                Assert.IsNull(toB.AutoCommitId);
                AssertHairPrefab(prefabShortPath);
            }

            var commitCountAfter = CommitStore.LoadIndex(avatarGuid).entries.Count;
            Assert.AreEqual(commitCountBefore, commitCountAfter,
                "toggling with CheckoutWithoutAutoCommit must not add any commits to the index");
        }

        [Test]
        public void CheckoutWithoutAutoCommit_MissingPrefab_ReturnsMissingPrefabsWithoutTouchingScene()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var root = ContainerManager.FindRoot(avatarRoot);

            var missingPrefabPath = $"{TestAssetDir}/ToBeDeleted.prefab";
            var missingSource = new GameObject("ToBeDeleted");
            var missingPrefab = PrefabUtility.SaveAsPrefabAsset(missingSource, missingPrefabPath);
            Object.DestroyImmediate(missingSource);

            var container = ContainerManager.CreateContainer(root, "outfit");
            PrefabUtility.InstantiatePrefab(missingPrefab, container.transform);
            var commit = BranchManager.Commit(avatarRoot, "with soon-to-be-missing prefab");

            AssetDatabase.DeleteAsset(missingPrefabPath);

            var result = CheckoutOperation.CheckoutWithoutAutoCommit(commit, avatarRoot);

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(CheckoutResultKind.MissingPrefabs, result.Kind);
            Assert.IsTrue(result.MissingPrefabGuids.Count > 0);
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
