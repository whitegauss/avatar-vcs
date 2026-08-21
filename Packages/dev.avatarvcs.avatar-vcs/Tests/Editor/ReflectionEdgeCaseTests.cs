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

        // TryDecode's value ultimately comes from commit JSON on disk, which
        // can be malformed independent of tampering (crash mid-write, bad
        // merge). It must return false, never throw -- an uncaught exception
        // here would abort a checkout mid-way, after containers are already
        // destroyed (see CheckoutOperation.ApplyCommitToScene).

        [Test]
        public void FieldCodec_TryDecode_NonNumericIntValue_ReturnsFalseInsteadOfThrowing()
        {
            var go = Spawn("IntHolder");
            var holder = go.AddComponent<LongFieldHolder>();
            var so = new SerializedObject(holder);
            var prop = so.FindProperty(nameof(LongFieldHolder.intField));

            Assert.DoesNotThrow(() => FieldCodec.TryDecode(prop, "int", "not-a-number"));
            Assert.IsFalse(FieldCodec.TryDecode(prop, "int", "not-a-number"));
        }

        [Test]
        public void FieldCodec_TryDecode_TooFewVectorComponents_ReturnsFalseInsteadOfThrowing()
        {
            var go = Spawn("VectorHolder");
            var so = new SerializedObject(go.transform);
            var prop = so.FindProperty("m_LocalPosition"); // Transform's Vector3 field

            Assert.DoesNotThrow(() => FieldCodec.TryDecode(prop, "vector3", "1,2")); // needs 3 components
            Assert.IsFalse(FieldCodec.TryDecode(prop, "vector3", "1,2"));
        }

        [Test]
        public void FieldCodec_TryDecode_NullValue_ReturnsFalseInsteadOfThrowing()
        {
            var go = Spawn("IntHolder2");
            var holder = go.AddComponent<LongFieldHolder>();
            var so = new SerializedObject(holder);
            var prop = so.FindProperty(nameof(LongFieldHolder.intField));

            Assert.DoesNotThrow(() => FieldCodec.TryDecode(prop, "int", null));
            Assert.IsFalse(FieldCodec.TryDecode(prop, "int", null));
        }

        private class ArrayFieldHolder : MonoBehaviour
        {
            public int[] items = new int[3];
        }

        [Test]
        public void FieldCodec_TryDecode_ArraySize_RejectsHugeValue()
        {
            // A crafted/corrupted FieldValue targeting "items.Array.size"
            // could otherwise make Unity attempt a huge array resize (hang/
            // OOM) -- this key is resolvable via FindProperty independent of
            // whether it was ever legitimately captured.
            var go = Spawn("ArrayHolder");
            var holder = go.AddComponent<ArrayFieldHolder>();
            var so = new SerializedObject(holder);
            var prop = so.FindProperty("items.Array.size");

            Assert.IsFalse(FieldCodec.TryDecode(prop, "int", "2000000000"));
            Assert.AreEqual(3, holder.items.Length, "rejected write must leave the array untouched");
        }

        [Test]
        public void FieldCodec_TryDecode_ArraySize_AcceptsReasonableValue()
        {
            var go = Spawn("ArrayHolder2");
            var holder = go.AddComponent<ArrayFieldHolder>();
            var so = new SerializedObject(holder);
            var prop = so.FindProperty("items.Array.size");

            Assert.IsTrue(FieldCodec.TryDecode(prop, "int", "5"));
            so.ApplyModifiedProperties();
            Assert.AreEqual(5, holder.items.Length);
        }

        [Test]
        public void FieldCodec_TryDecode_PlainIntField_UnaffectedByArraySizeCap()
        {
            // The cap is scoped to prop.propertyType == ArraySize; a huge
            // value for a genuinely plain int field must still round-trip.
            var go = Spawn("IntHolder3");
            var holder = go.AddComponent<LongFieldHolder>();
            var so = new SerializedObject(holder);
            var prop = so.FindProperty(nameof(LongFieldHolder.intField));

            Assert.IsTrue(FieldCodec.TryDecode(prop, "int", "2000000000"));
            so.ApplyModifiedProperties();
            Assert.AreEqual(2000000000, holder.intField);
        }

        #endregion

        #region FieldCodec AnimationCurve/Gradient Tests

        private class CurveAndGradientFieldHolder : MonoBehaviour
        {
            public AnimationCurve curve = new();
            public Gradient gradient = new();
        }

        [Test]
        public void FieldCodec_AnimationCurve_RoundTripsKeysAndWrapModes()
        {
            var go = Spawn("CurveHolder");
            var holder = go.AddComponent<CurveAndGradientFieldHolder>();
            var original = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 1f, 0f, 0.5f),
                new Keyframe(1f, 2f, 1f, 0f, 0.5f, 0f))
            {
                preWrapMode = WrapMode.Loop,
                postWrapMode = WrapMode.PingPong,
            };
            holder.curve = original;

            var so = new SerializedObject(holder);
            var prop = so.FindProperty(nameof(CurveAndGradientFieldHolder.curve));

            Assert.IsTrue(FieldCodec.TryEncode(prop, out var encoded, out var type));
            Assert.AreEqual("animationCurve", type);

            holder.curve = new AnimationCurve();
            so.Update();
            prop = so.FindProperty(nameof(CurveAndGradientFieldHolder.curve));
            Assert.IsTrue(FieldCodec.TryDecode(prop, type, encoded));
            so.ApplyModifiedProperties();

            Assert.AreEqual(2, holder.curve.keys.Length);
            Assert.AreEqual(2f, holder.curve.keys[1].value, 0.0001f);
            Assert.AreEqual(WrapMode.Loop, holder.curve.preWrapMode);
            Assert.AreEqual(WrapMode.PingPong, holder.curve.postWrapMode);
        }

        [Test]
        public void FieldCodec_AnimationCurve_EmptyCurve_RoundTrips()
        {
            var go = Spawn("EmptyCurveHolder");
            var holder = go.AddComponent<CurveAndGradientFieldHolder>();
            holder.curve = new AnimationCurve();

            var so = new SerializedObject(holder);
            var prop = so.FindProperty(nameof(CurveAndGradientFieldHolder.curve));

            Assert.IsTrue(FieldCodec.TryEncode(prop, out var encoded, out var type));
            Assert.IsTrue(FieldCodec.TryDecode(prop, type, encoded));
            so.ApplyModifiedProperties();

            Assert.AreEqual(0, holder.curve.keys.Length);
        }

        [Test]
        public void FieldCodec_Gradient_RoundTripsColorAndAlphaKeysAndMode()
        {
            var go = Spawn("GradientHolder");
            var holder = go.AddComponent<CurveAndGradientFieldHolder>();
            var original = new Gradient();
            original.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.red, 0f),
                    new GradientColorKey(Color.blue, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 1f),
                });
            original.mode = GradientMode.Fixed;
            holder.gradient = original;

            var so = new SerializedObject(holder);
            var prop = so.FindProperty(nameof(CurveAndGradientFieldHolder.gradient));

            Assert.IsTrue(FieldCodec.TryEncode(prop, out var encoded, out var type));
            Assert.AreEqual("gradient", type);

            holder.gradient = new Gradient();
            so.Update();
            prop = so.FindProperty(nameof(CurveAndGradientFieldHolder.gradient));
            Assert.IsTrue(FieldCodec.TryDecode(prop, type, encoded));
            so.ApplyModifiedProperties();

            Assert.AreEqual(2, holder.gradient.colorKeys.Length);
            Assert.AreEqual(2, holder.gradient.alphaKeys.Length);
            Assert.Less(Vector3.Distance(
                new Vector3(Color.blue.r, Color.blue.g, Color.blue.b),
                new Vector3(holder.gradient.colorKeys[1].color.r, holder.gradient.colorKeys[1].color.g, holder.gradient.colorKeys[1].color.b)), 0.001f);
            Assert.AreEqual(1f, holder.gradient.alphaKeys[1].alpha, 0.0001f);
            Assert.AreEqual(GradientMode.Fixed, holder.gradient.mode);
        }

        [Test]
        public void FieldCodec_TryDecode_MalformedAnimationCurve_ReturnsFalseInsteadOfThrowing()
        {
            var go = Spawn("MalformedCurveHolder");
            var holder = go.AddComponent<CurveAndGradientFieldHolder>();
            var so = new SerializedObject(holder);
            var prop = so.FindProperty(nameof(CurveAndGradientFieldHolder.curve));

            Assert.DoesNotThrow(() => FieldCodec.TryDecode(prop, "animationCurve", "not-a-valid-curve"));
            Assert.IsFalse(FieldCodec.TryDecode(prop, "animationCurve", "not-a-valid-curve"));
        }

        [Test]
        public void FieldCodec_TryDecode_MalformedGradient_ReturnsFalseInsteadOfThrowing()
        {
            var go = Spawn("MalformedGradientHolder");
            var holder = go.AddComponent<CurveAndGradientFieldHolder>();
            var so = new SerializedObject(holder);
            var prop = so.FindProperty(nameof(CurveAndGradientFieldHolder.gradient));

            Assert.DoesNotThrow(() => FieldCodec.TryDecode(prop, "gradient", "garbage"));
            Assert.IsFalse(FieldCodec.TryDecode(prop, "gradient", "garbage"));
        }

        #endregion
    }
}
