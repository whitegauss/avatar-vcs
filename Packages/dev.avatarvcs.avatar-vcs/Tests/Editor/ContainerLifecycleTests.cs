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
    ///
    /// Asset creation/deletion is done once per fixture (OneTimeSetUp/
    /// OneTimeTearDown) rather than per test: repeatedly creating and deleting
    /// an asset at the same path across many tests in quick succession trips
    /// Unity's "infinite import loop" detector.
    /// </summary>
    public class ContainerLifecycleTests
    {
        private const string TestAssetDir = "Assets/AvatarVcsTests_Temp";

        private GameObject testPrefabSource;
        private string testPrefabPath;
        private string testPrefabGuid;
        private GameObject avatarRoot;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (!AssetDatabase.IsValidFolder(TestAssetDir))
                AssetDatabase.CreateFolder("Assets", "AvatarVcsTests_Temp");

            testPrefabPath = $"{TestAssetDir}/TestOutfit.prefab";
            var source = new GameObject("TestOutfit");
            testPrefabSource = PrefabUtility.SaveAsPrefabAsset(source, testPrefabPath);
            Object.DestroyImmediate(source);
            testPrefabGuid = AssetDatabase.AssetPathToGUID(testPrefabPath);
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
            if (avatarRoot != null)
                Object.DestroyImmediate(avatarRoot);
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
        public void RestoreContainer_ReproducesTag()
        {
            // "EditorOnly" is the common VRChat trick for keeping a
            // container out of the avatar upload; it must survive the
            // destroy-then-regenerate round trip like everything else.
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(root, "helper");
            container.tag = "EditorOnly";

            var snapshot = ContainerCapture.CaptureContainer(container.transform);
            Assert.AreEqual("EditorOnly", snapshot.tag);

            var restored = ContainerRestore.InstantiateContainer(snapshot, root);
            Assert.AreEqual("EditorOnly", restored.tag);
        }

        [Test]
        public void RestoreContainer_DefaultsToUntagged_ForSnapshotsRecordedBeforeTagExisted()
        {
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(root, "outfit_a");

            var snapshot = ContainerCapture.CaptureContainer(container.transform);
            snapshot.tag = null; // simulates JsonUtility deserializing an old commit with no "tag" key

            var restored = ContainerRestore.InstantiateContainer(snapshot, root);
            Assert.AreEqual("Untagged", restored.tag);
        }

        [Test]
        public void FindEnclosingAvatarRoot_FromInsideAContainer_ResolvesToTheAvatar()
        {
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(root, "outfit_a");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(testPrefabSource, container.transform);

            Assert.AreSame(avatarRoot, ContainerManager.FindEnclosingAvatarRoot(instance),
                "an object inside a container should resolve up to the avatar, not be mistaken for one itself");
            Assert.AreSame(avatarRoot, ContainerManager.FindEnclosingAvatarRoot(container),
                "the container itself should also resolve to the avatar");
            Assert.AreSame(avatarRoot, ContainerManager.FindEnclosingAvatarRoot(root),
                "the [AvatarVCS] root itself should resolve to the avatar (its parent)");
        }

        [Test]
        public void FindEnclosingAvatarRoot_UnrelatedObject_ReturnsNull()
        {
            var unrelated = new GameObject("SomeOutfitPieceNotYetTracked");
            try
            {
                Assert.IsNull(ContainerManager.FindEnclosingAvatarRoot(unrelated),
                    "an object with no AvatarVCS structure anywhere in its ancestry has nothing to resolve to");
            }
            finally
            {
                Object.DestroyImmediate(unrelated);
            }
        }

        [Test]
        public void HasMissingPrefabs_DetectsUnresolvableGuid()
        {
            // Uses its own private prefab (rather than the shared fixture one)
            // since this test deletes it -- sharing would break sibling tests.
            var privatePrefabPath = $"{TestAssetDir}/TestOutfit_ForMissingCheck.prefab";
            var privateSource = new GameObject("TestOutfit_ForMissingCheck");
            var privatePrefab = PrefabUtility.SaveAsPrefabAsset(privateSource, privatePrefabPath);
            Object.DestroyImmediate(privateSource);
            var privateGuid = AssetDatabase.AssetPathToGUID(privatePrefabPath);

            var root = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(root, "outfit_a");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(privatePrefab, container.transform);
            var snapshot = ContainerCapture.CaptureContainer(container.transform);
            Assert.Contains(privateGuid, snapshot.prefabGuids, "captured snapshot should reference the private prefab's guid");

            Object.DestroyImmediate(instance);
            AssetDatabase.DeleteAsset(privatePrefabPath);

            // AssetDatabase.GUIDToAssetPath alone can keep resolving a
            // just-deleted asset's path (confirmed against real CI), which is
            // exactly why HasMissingPrefabs also checks that the asset loads.
            var isMissing = ContainerRestore.HasMissingPrefabs(snapshot, out var missingGuids);
            Assert.IsTrue(isMissing);
            CollectionAssert.Contains(missingGuids, privateGuid);
        }
    }
}
