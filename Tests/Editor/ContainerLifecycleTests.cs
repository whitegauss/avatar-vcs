using System.Linq;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.Operations;
using AvatarVcs.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// Covers the phase 1 completion criteria from DesignDoc_avatar-vcs.md
    /// section 7.1: root/container creation without duplication, prefab GUID
    /// capture, destroy-then-regenerate idempotency, and Transform round-trip.
    /// </summary>
    public class ContainerLifecycleTests
    {
        private const string TestAssetDir = "Assets/AvatarVcsTests_Temp";

        private GameObject avatarRoot;
        private GameObject testPrefabSource;
        private string testPrefabPath;
        private string testPrefabGuid;

        [SetUp]
        public void SetUp()
        {
            avatarRoot = new GameObject("TestAvatar");

            if (!AssetDatabase.IsValidFolder(TestAssetDir))
                AssetDatabase.CreateFolder("Assets", "AvatarVcsTests_Temp");

            testPrefabPath = $"{TestAssetDir}/TestOutfit.prefab";
            var source = new GameObject("TestOutfit");
            testPrefabSource = PrefabUtility.SaveAsPrefabAsset(source, testPrefabPath);
            Object.DestroyImmediate(source);
            testPrefabGuid = AssetDatabase.AssetPathToGUID(testPrefabPath);
        }

        [TearDown]
        public void TearDown()
        {
            if (avatarRoot != null)
                Object.DestroyImmediate(avatarRoot);

            if (AssetDatabase.IsValidFolder(TestAssetDir))
                AssetDatabase.DeleteAsset(TestAssetDir);
        }

        [Test]
        public void EnsureRoot_CreatesMarkerRoot_AndDoesNotDuplicateOnRerun()
        {
            var first = ContainerManager.EnsureRoot(avatarRoot);
            Assert.IsNotNull(first.GetComponent<AvatarVcsRoot>());
            Assert.AreEqual(ContainerManager.RootName, first.name);
            Assert.AreEqual(1, avatarRoot.transform.childCount);

            var second = ContainerManager.EnsureRoot(avatarRoot);
            Assert.AreSame(first, second);
            Assert.AreEqual(1, avatarRoot.transform.childCount);
        }

        [Test]
        public void GetPrefabGuid_ResolvesGuidOfCorrespondingSourcePrefab()
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(testPrefabSource);
            try
            {
                var guid = ContainerManager.GetPrefabGuid(instance);
                Assert.AreEqual(testPrefabGuid, guid);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void CreateContainer_RejectsDuplicateContainerId()
        {
            var root = ContainerManager.EnsureRoot(avatarRoot);
            ContainerManager.CreateContainer(root, "outfit_a");

            Assert.Throws<System.InvalidOperationException>(() =>
                ContainerManager.CreateContainer(root, "outfit_a"));
        }

        [Test]
        public void RestoreContainer_TwiceFromSameSnapshot_ProducesIdenticalResult()
        {
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(root, "outfit_a");
            PrefabUtility.InstantiatePrefab(testPrefabSource, container.transform);

            var snapshot = ContainerCapture.CaptureContainer(container.transform);

            ContainerRestore.InstantiateContainer(snapshot, root);
            AssertSingleOutfitAContainer(root, snapshot.containerGuid);

            ContainerRestore.InstantiateContainer(snapshot, root);
            AssertSingleOutfitAContainer(root, snapshot.containerGuid);
        }

        private static void AssertSingleOutfitAContainer(GameObject root, string expectedGuid)
        {
            Assert.AreEqual(1, root.transform.Cast<Transform>().Count(t => t.name == "outfit_a"));

            var container = root.transform.Find("outfit_a");
            Assert.AreEqual(1, container.childCount);
            Assert.AreEqual(expectedGuid, container.GetComponent<AvatarVcsContainer>().ContainerGuid);
        }

        [Test]
        public void RestoreContainer_ReproducesTransform()
        {
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(root, "hair_long");

            var expectedPosition = new Vector3(0.1f, 0.2f, 0.3f);
            var expectedRotation = Quaternion.Euler(10f, 20f, 30f);
            var expectedScale = new Vector3(1.1f, 1.2f, 1.3f);
            container.transform.localPosition = expectedPosition;
            container.transform.localRotation = expectedRotation;
            container.transform.localScale = expectedScale;

            var snapshot = ContainerCapture.CaptureContainer(container.transform);
            var restored = ContainerRestore.InstantiateContainer(snapshot, root);

            Assert.Less(Vector3.Distance(expectedPosition, restored.transform.localPosition), 0.0001f);
            Assert.Less(Quaternion.Angle(expectedRotation, restored.transform.localRotation), 0.01f);
            Assert.Less(Vector3.Distance(expectedScale, restored.transform.localScale), 0.0001f);
        }

        [Test]
        public void HasMissingPrefabs_DetectsUnresolvableGuid()
        {
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(root, "outfit_a");
            PrefabUtility.InstantiatePrefab(testPrefabSource, container.transform);
            var snapshot = ContainerCapture.CaptureContainer(container.transform);

            AssetDatabase.DeleteAsset(testPrefabPath);

            var isMissing = ContainerRestore.HasMissingPrefabs(snapshot, out var missingGuids);
            Assert.IsTrue(isMissing);
            CollectionAssert.Contains(missingGuids, testPrefabGuid);
        }
    }
}
