using System;
using AvatarVcs.Editor.Apply;
using AvatarVcs.Editor.AvatarReferences;
using AvatarVcs.Editor.Capture;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using AvatarVcs.Editor.MaterialSettings;
using AvatarVcs.Core.Model;
using AvatarVcs.Editor.Operations;
using AvatarVcs.Core.Reflection;
using AvatarVcs.Editor.UI;
using AvatarVcs.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// Tier 1: Core safety and invariant tests.
    /// These tests execute pure logic and boundary checks with minimal
    /// scene overhead, making them ideal to run locally before every commit.
    /// Filter by Category "Core" in the Unity Test Runner for instant verification.
    /// </summary>
    [Category("Core")]
    public class CoreSafetyAndBoundaryTests
    {
        private GameObject avatarRoot;

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
        public void FindEnclosingAvatarRoot_NullInput_ReturnsNull()
        {
            Assert.IsNull(ContainerManager.FindEnclosingAvatarRoot(null));
        }

        [Test]
        public void FindEnclosingAvatarRoot_DeeplyNestedChild_ResolvesToAvatarRoot()
        {
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(root, "outfit");
            
            // Create a deeply nested hierarchy inside the container:
            // container -> child -> grandchild -> greatGrandchild
            var child = new GameObject("Child");
            child.transform.SetParent(container.transform, false);
            var grandchild = new GameObject("Grandchild");
            grandchild.transform.SetParent(child.transform, false);
            var greatGrandchild = new GameObject("GreatGrandchild");
            greatGrandchild.transform.SetParent(grandchild.transform, false);

            Assert.AreSame(avatarRoot, ContainerManager.FindEnclosingAvatarRoot(greatGrandchild),
                "deeply nested objects inside a container must resolve all the way up to the avatar root");
        }

        [Test]
        public void CheckoutOperation_NullCommit_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                CheckoutOperation.Checkout(null, avatarRoot, "main", null));
        }

        [Test]
        public void CheckoutOperation_NullAvatarRoot_ThrowsArgumentNullException()
        {
            var commit = new Commit();
            Assert.Throws<ArgumentNullException>(() =>
                CheckoutOperation.Checkout(commit, null, "main", null));
        }

        [Test]
        public void CheckoutWithoutAutoCommit_NullArguments_ThrowsArgumentNullException()
        {
            var commit = new Commit();
            Assert.Throws<ArgumentNullException>(() =>
                CheckoutOperation.CheckoutWithoutAutoCommit(null, avatarRoot));
            Assert.Throws<ArgumentNullException>(() =>
                CheckoutOperation.CheckoutWithoutAutoCommit(commit, null));
        }

        [Test]
        public void ContainerManager_EnsureRoot_NullAvatarRoot_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ContainerManager.EnsureRoot(null));
            Assert.Throws<ArgumentNullException>(() => ContainerManager.FindRoot(null));
            Assert.Throws<ArgumentNullException>(() => ContainerManager.GetAvatarGuid(null));
        }

        [Test]
        public void ContainerManager_CreateContainer_NullOrEmptyId_ThrowsArgumentException()
        {
            var root = ContainerManager.EnsureRoot(avatarRoot);
            Assert.Throws<ArgumentException>(() => ContainerManager.CreateContainer(root, null));
            Assert.Throws<ArgumentException>(() => ContainerManager.CreateContainer(root, ""));
        }

        [Test]
        public void ContainerManager_CreateContainer_DuplicateId_ThrowsInvalidOperationException()
        {
            var root = ContainerManager.EnsureRoot(avatarRoot);
            ContainerManager.CreateContainer(root, "hair");

            Assert.Throws<InvalidOperationException>(() =>
                ContainerManager.CreateContainer(root, "hair"),
                "creating two containers with the same name under the same root must be forbidden");
        }

        [Test]
        public void ContainerManager_GetPrefabGuid_NullInstance_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ContainerManager.GetPrefabGuid(null));
        }

        [Test]
        public void TypeResolver_EmptyOrNull_ReturnsNull()
        {
            Assert.IsNull(TypeResolver.Resolve(null));
            Assert.IsNull(TypeResolver.Resolve(""));
            Assert.IsNull(TypeResolver.Resolve("   "));
        }

        [Test]
        public void ContainerManager_CreateContainer_RootMissingAvatarVcsRootMarker_ThrowsArgumentException()
        {
            var nonRoot = new GameObject("NotARoot");
            try
            {
                Assert.Throws<ArgumentException>(() => ContainerManager.CreateContainer(nonRoot, "container_1"));
            }
            finally
            {
                Object.DestroyImmediate(nonRoot);
            }
        }

        [Test]
        public void ContainerManager_ValidateContainers_NullRoot_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ContainerManager.ValidateContainers(null));
        }

        [Test]
        public void ContainerManager_GetContainers_NullRoot_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ContainerManager.GetContainers(null));
        }

        [Test]
        public void ContainerCapture_CaptureContainer_NullContainer_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ContainerCapture.CaptureContainer(null));
        }

        [Test]
        public void ContainerCapture_CaptureContainer_MissingMarker_ThrowsArgumentException()
        {
            var plainGo = new GameObject("PlainObject");
            try
            {
                Assert.Throws<ArgumentException>(() => ContainerCapture.CaptureContainer(plainGo.transform));
            }
            finally
            {
                Object.DestroyImmediate(plainGo);
            }
        }

        [Test]
        public void ContainerCapture_SameNameSiblingsInContainer_LogsOneCombinedWarning()
        {
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(root, "outfit_a");
            foreach (var name in new[] { "Dup", "Dup", "Two", "Two" })
                new GameObject(name).transform.SetParent(container.transform);

            // Two duplicated name groups on one node -> exactly one warning
            // naming both. (A second Expect here that never matches would
            // fail the test, so this also pins "one, not per-group".)
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex(@"'outfit_a' has same-named children \(.*'Dup' x2.*'Two' x2.*\)"));
            // Each loose child also trips the existing non-prefab warning.
            for (var i = 0; i < 4; i++)
                LogAssert.Expect(LogType.Warning,
                    new System.Text.RegularExpressions.Regex("inside container 'outfit_a' is not a prefab instance"));

            Assert.DoesNotThrow(() => ContainerCapture.CaptureContainer(container.transform));
        }

        [Test]
        public void ContainerRestore_InstantiateContainerStructure_NullSnapshotOrRoot_ThrowsArgumentNullException()
        {
            var snapshot = new ContainerSnapshot { containerId = "c1", containerGuid = "g1" };
            Assert.Throws<ArgumentNullException>(() => ContainerRestore.InstantiateContainerStructure(null, avatarRoot));
            Assert.Throws<ArgumentNullException>(() => ContainerRestore.InstantiateContainerStructure(snapshot, null));
        }

        [Test]
        public void ContainerRestore_ApplyContainerComponents_NullArguments_ThrowsArgumentNullException()
        {
            var snapshot = new ContainerSnapshot { containerId = "c1", containerGuid = "g1" };
            Assert.Throws<ArgumentNullException>(() => ContainerRestore.ApplyContainerComponents(null, avatarRoot, avatarRoot));
            Assert.Throws<ArgumentNullException>(() => ContainerRestore.ApplyContainerComponents(snapshot, null, avatarRoot));
        }

        [Test]
        public void ContainerRestore_HasMissingPrefabs_NullSnapshot_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ContainerRestore.HasMissingPrefabs(null, out _));
        }

        [Test]
        public void CommitBuilder_CreateCommit_NullAvatarRoot_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => CommitBuilder.CreateCommit(null, "msg", "main", null));
        }

        [Test]
        public void AvatarReferenceCapture_NullArguments_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => AvatarReferenceCapture.Capture(null, avatarRoot.transform));
            Assert.Throws<ArgumentNullException>(() => AvatarReferenceCapture.Capture(avatarRoot.transform, null));
        }

        [Test]
        public void AvatarReferenceApplier_NullArguments_ThrowsArgumentNullException()
        {
            var state = new AvatarReferenceState { path = "Body" };
            Assert.Throws<ArgumentNullException>(() => AvatarReferenceApplier.Apply(null, avatarRoot.transform));
            Assert.Throws<ArgumentNullException>(() => AvatarReferenceApplier.Apply(state, null));
        }

        [Test]
        public void AvatarReferenceCollector_NullAvatarRoot_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => AvatarReferenceCollector.CollectFromTrackedTargets(null));
        }

        [Test]
        public void MaterialSettingsCapture_NullOrInvalidArguments_ThrowsExpectedExceptions()
        {
            Assert.Throws<ArgumentNullException>(() => MaterialSettingsCapture.Capture(null, "lilToon", "Body", 0));
            
            var mat = new Material(Shader.Find("Standard"));
            try
            {
                Assert.Throws<ArgumentException>(() => MaterialSettingsCapture.Capture(mat, null, "Body", 0));
                Assert.Throws<ArgumentException>(() => MaterialSettingsCapture.Capture(mat, "", "Body", 0));
                Assert.Throws<NotSupportedException>(() => MaterialSettingsCapture.Capture(mat, "UnsupportedShader_12345", "Body", 0));
            }
            finally
            {
                Object.DestroyImmediate(mat);
            }
        }

        [Test]
        public void MaterialSettingsApplier_NullArguments_ThrowsArgumentNullException()
        {
            var state = new MaterialSettingsState { targetPath = "Body", slot = 0, shader = "lilToon", sourceMaterialGuid = "guid" };
            Assert.Throws<ArgumentNullException>(() => MaterialSettingsApplier.Apply(null, avatarRoot));
            Assert.Throws<ArgumentNullException>(() => MaterialSettingsApplier.Apply(state, null));
        }

        [Test]
        public void ComponentApplier_NullArguments_ThrowsArgumentNullException()
        {
            var state = new ComponentState { path = "", type = typeof(Light).FullName };
            Assert.Throws<ArgumentNullException>(() => ComponentApplier.Apply(null, avatarRoot));
            Assert.Throws<ArgumentNullException>(() => ComponentApplier.Apply(state, null));
        }

        [Test]
        public void ComponentCapturer_NullArguments_ThrowsArgumentNullException()
        {
            var light = avatarRoot.AddComponent<Light>();
            Assert.Throws<ArgumentNullException>(() => ComponentCapturer.Capture(null, avatarRoot.transform));
            Assert.Throws<ArgumentNullException>(() => ComponentCapturer.Capture(light, null));
        }

        [Test]
        public void HierarchyTrackingStatusIcon_GetTrackingStatus_ReflectsBothMechanisms()
        {
            Assert.AreEqual(HierarchyTrackingStatus.None, HierarchyTrackingStatusIcon.GetTrackingStatus(null));
            Assert.AreEqual(HierarchyTrackingStatus.None, HierarchyTrackingStatusIcon.GetTrackingStatus(avatarRoot));

            avatarRoot.AddComponent<AvatarVcsTrackedReference>();
            Assert.AreEqual(HierarchyTrackingStatus.TrackedReference, HierarchyTrackingStatusIcon.GetTrackingStatus(avatarRoot));

            var trackedChild = new GameObject("TrackedChild");
            trackedChild.transform.SetParent(avatarRoot.transform, false);
            Assert.AreEqual(HierarchyTrackingStatus.TrackedReference, HierarchyTrackingStatusIcon.GetTrackingStatus(trackedChild),
                "a descendant with no marker of its own is still covered by an ancestor's marker");

            var containerRootGo = new GameObject("[AvatarVCS]");
            containerRootGo.AddComponent<AvatarVcsRoot>();
            Assert.AreEqual(HierarchyTrackingStatus.ContainerManaged, HierarchyTrackingStatusIcon.GetTrackingStatus(containerRootGo));

            var containerChild = new GameObject("Container");
            containerChild.transform.SetParent(containerRootGo.transform, false);
            Assert.AreEqual(HierarchyTrackingStatus.ContainerManaged, HierarchyTrackingStatusIcon.GetTrackingStatus(containerChild));

            var strayMarkerUnderContainer = new GameObject("StrayMarker");
            strayMarkerUnderContainer.transform.SetParent(containerRootGo.transform, false);
            strayMarkerUnderContainer.AddComponent<AvatarVcsTrackedReference>();
            Assert.AreEqual(HierarchyTrackingStatus.ContainerManaged, HierarchyTrackingStatusIcon.GetTrackingStatus(strayMarkerUnderContainer),
                "container-managed status must win over a stray marker underneath [AvatarVCS], matching AvatarReferenceCapture's real skip-everything-under-the-root behavior");

            Object.DestroyImmediate(containerRootGo);
        }

        [Test]
        public void HierarchyTrackingStatusIcon_ShouldShowUntrackedMarker_OnlyFlagsUncoveredObjectsInsideAManagedAvatar()
        {
            Assert.IsFalse(HierarchyTrackingStatusIcon.ShouldShowUntrackedMarker(null));

            // Unrelated object, not part of any AvatarVCS-managed avatar --
            // must never be flagged, or every random object in the scene
            // would light up.
            var unrelated = new GameObject("UnrelatedSceneObject");
            Assert.IsFalse(HierarchyTrackingStatusIcon.ShouldShowUntrackedMarker(unrelated));
            Object.DestroyImmediate(unrelated);

            // avatarRoot has no AvatarVCS structure yet at all -- not managed,
            // so still not flagged even though it's technically untracked.
            Assert.IsFalse(HierarchyTrackingStatusIcon.ShouldShowUntrackedMarker(avatarRoot));

            ContainerManager.EnsureRoot(avatarRoot);
            var untrackedChild = new GameObject("Untracked");
            untrackedChild.transform.SetParent(avatarRoot.transform, false);

            // Now avatarRoot has AvatarVCS structure -- an untracked child of
            // it IS the exception worth flagging.
            Assert.IsTrue(HierarchyTrackingStatusIcon.ShouldShowUntrackedMarker(untrackedChild));

            untrackedChild.AddComponent<AvatarVcsTrackedReference>();
            Assert.IsFalse(HierarchyTrackingStatusIcon.ShouldShowUntrackedMarker(untrackedChild),
                "once tracked, no longer the exception");
        }
    }
}
