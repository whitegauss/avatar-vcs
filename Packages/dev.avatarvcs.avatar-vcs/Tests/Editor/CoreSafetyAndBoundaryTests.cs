using System;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using AvatarVcs.Editor.Model;
using AvatarVcs.Editor.Reflection;
using AvatarVcs.Editor.UI;
using AvatarVcs.Runtime;
using NUnit.Framework;
using UnityEngine;
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
            Assert.Throws<ArgumentNullException>(() => Operations.ContainerCapture.CaptureContainer(null));
        }

        [Test]
        public void ContainerCapture_CaptureContainer_MissingMarker_ThrowsArgumentException()
        {
            var plainGo = new GameObject("PlainObject");
            try
            {
                Assert.Throws<ArgumentException>(() => Operations.ContainerCapture.CaptureContainer(plainGo.transform));
            }
            finally
            {
                Object.DestroyImmediate(plainGo);
            }
        }

        [Test]
        public void ContainerRestore_InstantiateContainerStructure_NullSnapshotOrRoot_ThrowsArgumentNullException()
        {
            var snapshot = new ContainerSnapshot { containerId = "c1", containerGuid = "g1" };
            Assert.Throws<ArgumentNullException>(() => Operations.ContainerRestore.InstantiateContainerStructure(null, avatarRoot));
            Assert.Throws<ArgumentNullException>(() => Operations.ContainerRestore.InstantiateContainerStructure(snapshot, null));
        }

        [Test]
        public void ContainerRestore_ApplyContainerComponents_NullArguments_ThrowsArgumentNullException()
        {
            var snapshot = new ContainerSnapshot { containerId = "c1", containerGuid = "g1" };
            Assert.Throws<ArgumentNullException>(() => Operations.ContainerRestore.ApplyContainerComponents(null, avatarRoot, avatarRoot));
            Assert.Throws<ArgumentNullException>(() => Operations.ContainerRestore.ApplyContainerComponents(snapshot, null, avatarRoot));
        }

        [Test]
        public void ContainerRestore_HasMissingPrefabs_NullSnapshot_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => Operations.ContainerRestore.HasMissingPrefabs(null, out _));
        }

        [Test]
        public void CommitBuilder_CreateCommit_NullAvatarRoot_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => CommitBuilder.CreateCommit(null, "msg", "main", null));
        }

        [Test]
        public void AvatarReferenceCapture_NullArguments_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => AvatarReferences.AvatarReferenceCapture.Capture(null, avatarRoot.transform));
            Assert.Throws<ArgumentNullException>(() => AvatarReferences.AvatarReferenceCapture.Capture(avatarRoot.transform, null));
        }

        [Test]
        public void AvatarReferenceApplier_NullArguments_ThrowsArgumentNullException()
        {
            var state = new AvatarReferenceState { path = "Body" };
            Assert.Throws<ArgumentNullException>(() => AvatarReferences.AvatarReferenceApplier.Apply(null, avatarRoot.transform));
            Assert.Throws<ArgumentNullException>(() => AvatarReferences.AvatarReferenceApplier.Apply(state, null));
        }

        [Test]
        public void AvatarReferenceCollector_NullAvatarRoot_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => AvatarReferences.AvatarReferenceCollector.CollectFromTrackedTargets(null));
        }

        [Test]
        public void MaterialSettingsCapture_NullOrInvalidArguments_ThrowsExpectedExceptions()
        {
            Assert.Throws<ArgumentNullException>(() => MaterialSettings.MaterialSettingsCapture.Capture(null, "lilToon", "Body", 0));
            
            var mat = new Material(Shader.Find("Standard"));
            try
            {
                Assert.Throws<ArgumentException>(() => MaterialSettings.MaterialSettingsCapture.Capture(mat, null, "Body", 0));
                Assert.Throws<ArgumentException>(() => MaterialSettings.MaterialSettingsCapture.Capture(mat, "", "Body", 0));
                Assert.Throws<NotSupportedException>(() => MaterialSettings.MaterialSettingsCapture.Capture(mat, "UnsupportedShader_12345", "Body", 0));
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
            Assert.Throws<ArgumentNullException>(() => MaterialSettings.MaterialSettingsApplier.Apply(null, avatarRoot));
            Assert.Throws<ArgumentNullException>(() => MaterialSettings.MaterialSettingsApplier.Apply(state, null));
        }

        [Test]
        public void ComponentApplier_NullArguments_ThrowsArgumentNullException()
        {
            var state = new ComponentState { path = "", type = typeof(Light).FullName };
            Assert.Throws<ArgumentNullException>(() => Apply.ComponentApplier.Apply(null, avatarRoot));
            Assert.Throws<ArgumentNullException>(() => Apply.ComponentApplier.Apply(state, null));
        }

        [Test]
        public void ComponentCapturer_NullArguments_ThrowsArgumentNullException()
        {
            var light = avatarRoot.AddComponent<Light>();
            Assert.Throws<ArgumentNullException>(() => Capture.ComponentCapturer.Capture(null, avatarRoot.transform));
            Assert.Throws<ArgumentNullException>(() => Capture.ComponentCapturer.Capture(light, null));
        }

        [Test]
        public void TrackedReferenceHierarchyIcon_ShouldShowMarker_OnlyForTrackedGameObjects()
        {
            Assert.IsFalse(TrackedReferenceHierarchyIcon.ShouldShowMarker(null));
            Assert.IsFalse(TrackedReferenceHierarchyIcon.ShouldShowMarker(avatarRoot));

            avatarRoot.AddComponent<AvatarVcsTrackedReference>();
            Assert.IsTrue(TrackedReferenceHierarchyIcon.ShouldShowMarker(avatarRoot));
        }
    }
}
