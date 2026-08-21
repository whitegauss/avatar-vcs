using System;
using System.Collections.Generic;
using AvatarVcs.Editor.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// Edge-case and robustness tests for reflection utilities:
    /// TypeResolver, ReferenceResolver, and FieldCodec.
    /// Covers boundary values, nulls, invalid inputs, and caching.
    /// </summary>
    public class ReflectionEdgeCaseTests
    {
        private readonly List<GameObject> spawned = new();

        private GameObject Spawn(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent);
            spawned.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();
        }

        #region TypeResolver Tests

        [Test]
        public void TypeResolver_NullOrEmpty_ReturnsNull()
        {
            Assert.IsNull(TypeResolver.Resolve(null));
            Assert.IsNull(TypeResolver.Resolve(""));
            Assert.IsNull(TypeResolver.Resolve(string.Empty));
        }

        [Test]
        public void TypeResolver_ValidBuiltInAndUnityTypes_ResolveCorrectly()
        {
            Assert.AreEqual(typeof(string), TypeResolver.Resolve(typeof(string).FullName));
            Assert.AreEqual(typeof(Transform), TypeResolver.Resolve(typeof(Transform).FullName));
            Assert.AreEqual(typeof(GameObject), TypeResolver.Resolve(typeof(GameObject).FullName));
            Assert.AreEqual(typeof(SkinnedMeshRenderer), TypeResolver.Resolve(typeof(SkinnedMeshRenderer).FullName));
            Assert.AreEqual(typeof(BoxCollider), TypeResolver.Resolve(typeof(BoxCollider).FullName));
        }

        [Test]
        public void TypeResolver_NonExistentType_ReturnsNullAndCachesMiss()
        {
            const string bogusTypeName = "NonExistent.Namespace.FakeType_12345";
            Assert.IsNull(TypeResolver.Resolve(bogusTypeName));
            // Second call should return cached null without throwing or hanging
            Assert.IsNull(TypeResolver.Resolve(bogusTypeName));
        }

        #endregion

        #region ReferenceResolver Tests

        [Test]
        public void GetRelativePath_SameTargetAndRoot_ReturnsEmptyString()
        {
            var root = Spawn("Root");
            Assert.AreEqual(string.Empty, ReferenceResolver.GetRelativePath(root.transform, root.transform));
        }

        [Test]
        public void GetRelativePath_DirectAndNestedChildren_ReturnsCorrectSlashPath()
        {
            var root = Spawn("Root");
            var child = Spawn("Child", root.transform);
            var grandChild = Spawn("GrandChild", child.transform);

            Assert.AreEqual("Child", ReferenceResolver.GetRelativePath(child.transform, root.transform));
            Assert.AreEqual("Child/GrandChild", ReferenceResolver.GetRelativePath(grandChild.transform, root.transform));
        }

        [Test]
        public void GetRelativePath_NonDescendant_ThrowsArgumentException()
        {
            var rootA = Spawn("RootA");
            var rootB = Spawn("RootB");
            var unrelatedChild = Spawn("ChildB", rootB.transform);

            Assert.Throws<ArgumentException>(() =>
                ReferenceResolver.GetRelativePath(unrelatedChild.transform, rootA.transform));
        }

        [Test]
        public void GetRelativePath_NullArguments_ThrowsArgumentNullException()
        {
            var root = Spawn("Root");
            Assert.Throws<ArgumentNullException>(() => ReferenceResolver.GetRelativePath(null, root.transform));
            Assert.Throws<ArgumentNullException>(() => ReferenceResolver.GetRelativePath(root.transform, null));
        }

        [Test]
        public void ResolvePath_NullRoot_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ReferenceResolver.ResolvePath("Child", null));
        }

        [Test]
        public void ResolvePath_NullOrEmptyPath_ReturnsRoot()
        {
            var root = Spawn("Root");
            Assert.AreSame(root.transform, ReferenceResolver.ResolvePath(null, root.transform));
            Assert.AreSame(root.transform, ReferenceResolver.ResolvePath("", root.transform));
        }

        [Test]
        public void ResolvePath_NonExistentPath_ReturnsNull()
        {
            var root = Spawn("Root");
            Assert.IsNull(ReferenceResolver.ResolvePath("NonExistent/Child/Path", root.transform));
        }

        [Test]
        public void ResolveSceneReference_ResolvesGameObjectAndTransformAndComponent()
        {
            var targetGo = Spawn("Target");
            var boxCol = targetGo.AddComponent<BoxCollider>();

            var resolvedGo = ReferenceResolver.ResolveSceneReference(targetGo.transform, typeof(GameObject).FullName);
            Assert.AreSame(targetGo, resolvedGo);

            var resolvedTransform = ReferenceResolver.ResolveSceneReference(targetGo.transform, typeof(Transform).FullName);
            Assert.AreSame(targetGo.transform, resolvedTransform);

            var resolvedCol = ReferenceResolver.ResolveSceneReference(targetGo.transform, typeof(BoxCollider).FullName);
            Assert.AreSame(boxCol, resolvedCol);
        }

        [Test]
        public void ResolveSceneReference_MissingComponent_ReturnsNull()
        {
            var targetGo = Spawn("Target"); // No Rigidbody
            var resolved = ReferenceResolver.ResolveSceneReference(targetGo.transform, typeof(Rigidbody).FullName);
            Assert.IsNull(resolved);
        }

        [Test]
        public void ResolveSceneReference_NullOrEmptyInputs_ReturnsNull()
        {
            var targetGo = Spawn("Target");
            Assert.IsNull(ReferenceResolver.ResolveSceneReference(null, typeof(GameObject).FullName));
            Assert.IsNull(ReferenceResolver.ResolveSceneReference(targetGo.transform, null));
            Assert.IsNull(ReferenceResolver.ResolveSceneReference(targetGo.transform, ""));
        }

        #endregion

        #region ResolveAsset Tests

        [Test]
        public void ResolveAsset_NullOrEmptyGuid_ReturnsNull()
        {
            Assert.IsNull(ReferenceResolver.ResolveAsset(null, 0));
            Assert.IsNull(ReferenceResolver.ResolveAsset("", 0));
            Assert.IsNull(ReferenceResolver.ResolveAsset("non_existent_guid_0000000000000000", 0));
        }

        #endregion

        #region FieldCodec 64-bit Integer Tests

        // SerializedPropertyType.Integer covers int, long, and ulong alike;
        // a real component with a long/ulong field is needed to exercise the
        // .intValue-truncates-a-long bug, since no built-in Unity component
        // exposes one.
        private class LongFieldHolder : MonoBehaviour
        {
            public long longField;
            public ulong ulongField;
            public int intField;
        }

        [Test]
        public void FieldCodec_LongField_RoundTripsBeyondIntRange()
        {
            var go = Spawn("LongHolder");
            var holder = go.AddComponent<LongFieldHolder>();
            const long value = 5_000_000_000L; // beyond int.MaxValue (~2.1B)
            holder.longField = value;

            var so = new SerializedObject(holder);
            var prop = so.FindProperty(nameof(LongFieldHolder.longField));

            Assert.IsTrue(FieldCodec.TryEncode(prop, out var encoded, out var type));
            Assert.AreEqual("long", type);
            Assert.AreEqual(value.ToString(), encoded);

            holder.longField = 0;
            so.Update();
            prop = so.FindProperty(nameof(LongFieldHolder.longField));
            Assert.IsTrue(FieldCodec.TryDecode(prop, type, encoded));
            so.ApplyModifiedProperties();

            Assert.AreEqual(value, holder.longField);
        }

        [Test]
        public void FieldCodec_UlongField_RoundTripsBeyondLongRange()
        {
            var go = Spawn("UlongHolder");
            var holder = go.AddComponent<LongFieldHolder>();
            const ulong value = 18_000_000_000_000_000_000UL; // beyond long.MaxValue
            holder.ulongField = value;

            var so = new SerializedObject(holder);
            var prop = so.FindProperty(nameof(LongFieldHolder.ulongField));

            Assert.IsTrue(FieldCodec.TryEncode(prop, out var encoded, out var type));
            Assert.AreEqual("ulong", type);
            Assert.AreEqual(value.ToString(), encoded);

            holder.ulongField = 0;
            so.Update();
            prop = so.FindProperty(nameof(LongFieldHolder.ulongField));
            Assert.IsTrue(FieldCodec.TryDecode(prop, type, encoded));
            so.ApplyModifiedProperties();

            Assert.AreEqual(value, holder.ulongField);
        }

        [Test]
        public void FieldCodec_IntField_StillUsesIntValue_NotAffectedByLongHandling()
        {
            var go = Spawn("IntHolder");
            var holder = go.AddComponent<LongFieldHolder>();
            holder.intField = 42;

            var so = new SerializedObject(holder);
            var prop = so.FindProperty(nameof(LongFieldHolder.intField));

            Assert.IsTrue(FieldCodec.TryEncode(prop, out var encoded, out var type));
            Assert.AreEqual("int", type);
            Assert.AreEqual("42", encoded);
        }

        #endregion
    }
}
