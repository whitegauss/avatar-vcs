using System;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using AvatarVcs.Editor.Model;
using AvatarVcs.Editor.Serialization;
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
        public void TypeResolver_EmptyOrNull_ReturnsNotFound()
        {
            Assert.AreEqual(TypeResolutionResult.NotFound, TypeResolver.Resolve(""));
            Assert.AreEqual(TypeResolutionResult.NotFound, TypeResolver.Resolve("   "));
        }

        [Test]
        public void FieldCodec_Decode_NullOrInvalidString_ReturnsSafeDefault()
        {
            var strVal = FieldCodec.Decode(new FieldValue { key = "test", type = "string", value = null });
            Assert.IsNull(strVal);

            var intVal = FieldCodec.Decode(new FieldValue { key = "test", type = "int", value = "invalid_number" });
            Assert.AreEqual(0, intVal);

            var boolVal = FieldCodec.Decode(new FieldValue { key = "test", type = "bool", value = "invalid_bool" });
            Assert.AreEqual(false, boolVal);
        }
    }
}
