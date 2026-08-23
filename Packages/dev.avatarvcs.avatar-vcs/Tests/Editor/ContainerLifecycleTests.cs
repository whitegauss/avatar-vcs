using System.Linq;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.Model;
using AvatarVcs.Editor.Operations;
using AvatarVcs.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

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
        public void EnsureRoot_NeverSeedsADefaultContainerOrTracking()
        {
            // Only EnsureRootWithDefaults (the user-facing "Ensure Root"
            // command) seeds either -- EnsureRoot itself is also called by
            // container-count/tracking-agnostic internal plumbing and must
            // stay exactly as before.
            var root = ContainerManager.EnsureRoot(avatarRoot);

            Assert.IsEmpty(ContainerManager.GetContainers(root));
            Assert.IsNull(avatarRoot.GetComponent<AvatarVcsTrackedReference>());
        }

        [Test]
        public void EnsureRootAndDefaultContainer_OnFirstCreation_SeedsOneContainer()
        {
            var root = ContainerManager.EnsureRootAndDefaultContainer(avatarRoot);

            var containers = ContainerManager.GetContainers(root);
            Assert.AreEqual(1, containers.Length);
            Assert.AreEqual(ContainerManager.DefaultContainerId, containers[0].name);
        }

        [Test]
        public void EnsureRootAndDefaultContainer_OnExistingRoot_DoesNotReSeedAfterManualDeletion()
        {
            var root = ContainerManager.EnsureRootAndDefaultContainer(avatarRoot);
            Object.DestroyImmediate(ContainerManager.GetContainers(root)[0].gameObject);
            Assert.IsEmpty(ContainerManager.GetContainers(root));

            // Rerunning against the now-existing root must not silently
            // bring the deleted default container back.
            var rerun = ContainerManager.EnsureRootAndDefaultContainer(avatarRoot);

            Assert.AreSame(root, rerun);
            Assert.IsEmpty(ContainerManager.GetContainers(rerun));
        }

        [Test]
        public void EnsureRootWithDefaultTracking_OnFirstCreation_TracksRootAndExistingTopLevelChildren()
        {
            var body = new GameObject("Body");
            body.transform.SetParent(avatarRoot.transform, false);
            var armature = new GameObject("Armature");
            armature.transform.SetParent(avatarRoot.transform, false);

            var root = ContainerManager.EnsureRootWithDefaultTracking(avatarRoot);

            Assert.IsNotNull(avatarRoot.GetComponent<AvatarVcsTrackedReference>(), "the avatar root itself must be tracked");
            Assert.IsNotNull(body.GetComponent<AvatarVcsTrackedReference>());
            Assert.IsNotNull(armature.GetComponent<AvatarVcsTrackedReference>());
            Assert.IsNull(root.GetComponent<AvatarVcsTrackedReference>(), "[AvatarVCS] itself must never be tracked");
        }

        [Test]
        public void EnsureRootWithDefaultTracking_OnExistingRoot_DoesNotReTrackAfterManualUntrack()
        {
            var body = new GameObject("Body");
            body.transform.SetParent(avatarRoot.transform, false);
            ContainerManager.EnsureRootWithDefaultTracking(avatarRoot);
            Object.DestroyImmediate(body.GetComponent<AvatarVcsTrackedReference>());
            Assert.IsNull(body.GetComponent<AvatarVcsTrackedReference>());

            // Rerunning against the now-existing root must not silently
            // bring the manually removed tracking back.
            ContainerManager.EnsureRootWithDefaultTracking(avatarRoot);

            Assert.IsNull(body.GetComponent<AvatarVcsTrackedReference>());
        }

        [Test]
        public void EnsureRootWithDefaultTracking_DoesNotDuplicateTrackingOnAlreadyTrackedObject()
        {
            var body = new GameObject("Body");
            body.transform.SetParent(avatarRoot.transform, false);
            body.AddComponent<AvatarVcsTrackedReference>(); // pre-existing, manual

            Assert.DoesNotThrow(() => ContainerManager.EnsureRootWithDefaultTracking(avatarRoot));
            Assert.AreEqual(1, body.GetComponents<AvatarVcsTrackedReference>().Length);
        }

        [Test]
        public void EnsureRootWithDefaults_OnFirstCreation_SeedsBothContainerAndTracking()
        {
            // The combined method the "Ensure Root" menu command actually
            // calls -- must seed both in one pass. Calling
            // EnsureRootAndDefaultContainer then EnsureRootWithDefaultTracking
            // back to back would NOT work here: each independently checks
            // "is this a new root?", and the first call already having
            // created the root would make the second one see it as
            // pre-existing and skip its own seeding.
            var body = new GameObject("Body");
            body.transform.SetParent(avatarRoot.transform, false);

            var root = ContainerManager.EnsureRootWithDefaults(avatarRoot);

            var containers = ContainerManager.GetContainers(root);
            Assert.AreEqual(1, containers.Length);
            Assert.AreEqual(ContainerManager.DefaultContainerId, containers[0].name);
            Assert.IsNotNull(avatarRoot.GetComponent<AvatarVcsTrackedReference>());
            Assert.IsNotNull(body.GetComponent<AvatarVcsTrackedReference>());
        }

        [Test]
        public void FindRoot_StillResolvesAfterManualRename()
        {
            var root = ContainerManager.EnsureRoot(avatarRoot);
            root.name = "Oops I Renamed This";

            var found = ContainerManager.FindRoot(avatarRoot);
            Assert.AreSame(root, found, "a renamed root must still be found by its marker component, not just by name");

            // And EnsureRoot must reuse it rather than creating a duplicate.
            var ensured = ContainerManager.EnsureRoot(avatarRoot);
            Assert.AreSame(root, ensured);
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
        public void ValidateContainers_DetectsDuplicateNameFromManualRename()
        {
            // CreateContainer itself already rejects this; the case that
            // actually needs guarding is a manual Hierarchy rename after
            // creation, which bypasses that check entirely.
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var a = ContainerManager.CreateContainer(root, "outfit_a");
            ContainerManager.CreateContainer(root, "outfit_b");
            a.name = "outfit_b";

            var ex = Assert.Throws<System.InvalidOperationException>(() =>
                ContainerManager.ValidateContainers(root));
            StringAssert.Contains("outfit_b", ex.Message);
        }

        [Test]
        public void ValidateContainers_DetectsManuallyNestedContainer()
        {
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var outer = ContainerManager.CreateContainer(root, "outfit_a");
            var innerGo = new GameObject("hair");
            innerGo.transform.SetParent(outer.transform, false);
            innerGo.AddComponent<AvatarVcsContainer>();

            var ex = Assert.Throws<System.InvalidOperationException>(() =>
                ContainerManager.ValidateContainers(root));
            StringAssert.Contains("nested", ex.Message);
        }

        [Test]
        public void ValidateContainers_AcceptsFlatValidStructure()
        {
            var root = ContainerManager.EnsureRoot(avatarRoot);
            ContainerManager.CreateContainer(root, "outfit_a");
            ContainerManager.CreateContainer(root, "hair");

            Assert.DoesNotThrow(() => ContainerManager.ValidateContainers(root));
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
        public void RestoreContainer_ReproducesActiveSelfAndLayer()
        {
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(root, "outfit_a");
            container.gameObject.SetActive(false);
            container.gameObject.layer = 3;

            var snapshot = ContainerCapture.CaptureContainer(container.transform);
            Assert.IsFalse(snapshot.activeSelf);
            Assert.AreEqual(3, snapshot.layer);

            var restored = ContainerRestore.InstantiateContainer(snapshot, root);
            Assert.IsFalse(restored.activeSelf);
            Assert.AreEqual(3, restored.layer);
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
        public void FindEnclosingAvatarRoot_FromInsideAnInactiveContainer_StillResolves()
        {
            // GetComponentInParent defaults to active-only; a toggled-off
            // container (now a legitimate state since PR #13 added
            // activeSelf) must not become invisible to this resolution.
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(root, "outfit_a");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(testPrefabSource, container.transform);
            container.SetActive(false);

            Assert.AreSame(avatarRoot, ContainerManager.FindEnclosingAvatarRoot(instance));
        }

        [Test]
        public void FindEnclosingAvatarRoot_FromDeepChildOutsideAnyContainer_ResolvesToTheAvatar()
        {
            // Reported bug: a child nested somewhere other than inside a
            // container (e.g. under Body/Armature) has no AvatarVcsRoot
            // ancestor -- AvatarVcsRoot lives on "[AvatarVCS]" itself, a
            // SIBLING of Body/Armature, not their ancestor -- so this used
            // to return null even though the avatar is already tracked,
            // letting Ensure Root spin up a second, nested root right there.
            ContainerManager.EnsureRoot(avatarRoot);
            var body = new GameObject("Body");
            body.transform.SetParent(avatarRoot.transform, false);
            var deepChild = new GameObject("DeepChild");
            deepChild.transform.SetParent(body.transform, false);

            Assert.AreSame(avatarRoot, ContainerManager.FindEnclosingAvatarRoot(deepChild),
                "a child nested anywhere under an already-tracked avatar, container or not, must resolve to that avatar");
        }

        [Test]
        public void EnsureRootWithDefaults_OnDeepChildOfAnAlreadyTrackedAvatar_ReusesTheExistingRoot_DoesNotNestANewOne()
        {
            var existingRoot = ContainerManager.EnsureRootWithDefaults(avatarRoot);
            var body = new GameObject("Body");
            body.transform.SetParent(avatarRoot.transform, false);
            var deepChild = new GameObject("DeepChild");
            deepChild.transform.SetParent(body.transform, false);

            var resolved = ContainerManager.ResolveAvatarRootWithConfirmation(deepChild, "test");
            Assert.AreSame(avatarRoot, resolved,
                "resolving from a deep, non-container child of an already-tracked avatar must find the real avatar, not treat the child as a brand new one");

            var root = ContainerManager.EnsureRootWithDefaults(resolved);
            Assert.AreSame(existingRoot, root, "must reuse the existing root instead of creating a nested duplicate");
            Assert.IsNull(deepChild.transform.Find(ContainerManager.RootName),
                "no second [AvatarVCS] should ever be created under the deep child");
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
        public void ResolveAvatarRootWithConfirmation_FromInsideAContainer_ResolvesWithoutPrompting()
        {
            // Both early-return paths never touch the confirmation dialog,
            // so they're safe to exercise headlessly; the "no existing
            // structure -> confirm" path isn't (DisplayDialog blocks on
            // user input), matching how AvatarVcsMenu/AvatarVcsWindow's use
            // of this are UI-layer and not directly unit tested either.
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(root, "outfit_a");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(testPrefabSource, container.transform);

            Assert.AreSame(avatarRoot, ContainerManager.ResolveAvatarRootWithConfirmation(instance, "test"));
            Assert.AreSame(avatarRoot, ContainerManager.ResolveAvatarRootWithConfirmation(container.gameObject, "test"));
        }

        [Test]
        public void ResolveAvatarRootWithConfirmation_AlreadyTheAvatarRoot_ResolvesWithoutPrompting()
        {
            var root = ContainerManager.EnsureRoot(avatarRoot);
            Assert.AreSame(avatarRoot, ContainerManager.ResolveAvatarRootWithConfirmation(avatarRoot, "test"));
            Assert.AreSame(avatarRoot, ContainerManager.ResolveAvatarRootWithConfirmation(root, "test"),
                "the [AvatarVCS] root itself, not just the avatar, already has an existing structure to resolve to");
        }

        [Test]
        public void ResolveAvatarRootWithConfirmation_NullSelection_ReturnsNull()
        {
            Assert.IsNull(ContainerManager.ResolveAvatarRootWithConfirmation(null, "test"));
        }

        [Test]
        public void IsUnderManagedAvatar_ReflectsWhetherGoIsTheAvatarRootOrSomewhereUnderneathIt()
        {
            Assert.IsFalse(ContainerManager.IsUnderManagedAvatar(null));
            Assert.IsFalse(ContainerManager.IsUnderManagedAvatar(avatarRoot), "no AvatarVCS structure yet");

            var root = ContainerManager.EnsureRoot(avatarRoot);
            Assert.IsTrue(ContainerManager.IsUnderManagedAvatar(avatarRoot), "the avatar root itself counts");

            var body = new GameObject("Body");
            body.transform.SetParent(avatarRoot.transform, false);
            Assert.IsTrue(ContainerManager.IsUnderManagedAvatar(body), "a sibling of [AvatarVCS], not just something inside it");

            var container = ContainerManager.CreateContainer(root, "outfit_a");
            Assert.IsTrue(ContainerManager.IsUnderManagedAvatar(container.gameObject));

            var unrelated = new GameObject("Unrelated");
            Assert.IsFalse(ContainerManager.IsUnderManagedAvatar(unrelated));
            Object.DestroyImmediate(unrelated);
        }

        [Test]
        public void CaptureContainer_WarnsAboutNonPrefabChild()
        {
            // Not a prefab instance, so it has no guid to regenerate it from
            // -- the container will silently lose it on the next checkout.
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(root, "outfit_a");
            new GameObject("RawLight").transform.SetParent(container.transform, false);

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("'RawLight' inside container 'outfit_a' is not a prefab instance"));

            ContainerCapture.CaptureContainer(container.transform);
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

        [Test]
        public void RestoreContainer_UndefinedTagInTagManager_LogsWarningAndLeavesUntagged()
        {
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var snapshot = new ContainerSnapshot
            {
                containerId = "tag_test",
                containerGuid = "0123456789abcdef0123456789abcdef",
                tag = "NonExistentTag_12345",
            };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Tag 'NonExistentTag_12345' .* is not defined in this project's Tag Manager"));
            var restored = ContainerRestore.InstantiateContainer(snapshot, root);

            Assert.IsNotNull(restored);
            Assert.AreEqual("Untagged", restored.tag);
        }

        [Test]
        public void RestoreContainer_MissingPrefabWithoutCheck_ThrowsInvalidOperationException()
        {
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var snapshot = new ContainerSnapshot
            {
                containerId = "missing_prefab_test",
                containerGuid = "0123456789abcdef0123456789abcdef",
                prefabGuids = { "unresolvable_prefab_guid_00000000" },
            };

            Assert.Throws<System.InvalidOperationException>(() =>
                ContainerRestore.InstantiateContainerStructure(snapshot, root));
        }
    }
}
