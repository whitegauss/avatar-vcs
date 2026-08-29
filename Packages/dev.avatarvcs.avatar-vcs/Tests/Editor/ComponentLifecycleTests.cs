using System.Collections.Generic;
using AvatarVcs.Editor.Apply;
using AvatarVcs.Editor.Capture;
using AvatarVcs.Core.Model;
using AvatarVcs.Core.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// Covers phase 2 task 5 from DesignDoc_avatar-vcs.md section 7.2: the v1
    /// SerializedObject capture/apply technique works against the v2 two-tier
    /// (container -> components) structure. Uses the built-in Light component
    /// as a stand-in for MA/AAO components, since the technique is fully
    /// reflective and does not depend on any specific component type.
    /// </summary>
    public class ComponentLifecycleTests
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
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        [Test]
        public void Capture_ThenApply_ReproducesFieldsOnAnotherComponent()
        {
            var sourceRoot = Spawn("SourceRoot");
            var sourceChild = Spawn("Source", sourceRoot.transform);
            var sourceLight = sourceChild.AddComponent<Light>();
            sourceLight.type = LightType.Point;
            var expectedColor = new Color(0.2f, 0.4f, 0.6f, 1f);
            sourceLight.color = expectedColor;
            sourceLight.intensity = 3.5f;
            sourceLight.range = 12f;

            var state = ComponentCapturer.Capture(sourceLight, sourceRoot.transform);

            var targetRoot = Spawn("TargetRoot");
            var targetChild = Spawn("Source", targetRoot.transform);
            var targetLight = targetChild.AddComponent<Light>();

            var result = ComponentApplier.Apply(state, targetRoot, createIfMissing: false);

            Assert.IsTrue(result.IsSuccess, result.Message);
            Assert.AreEqual(LightType.Point, targetLight.type);
            Assert.AreEqual(3.5f, targetLight.intensity, 0.0001f);
            Assert.AreEqual(12f, targetLight.range, 0.0001f);
            Assert.Less(Vector4.Distance(expectedColor, targetLight.color), 0.001f);
        }

        [Test]
        public void Apply_WithCreateIfMissing_AddsComponentWhenAbsent()
        {
            var sourceRoot = Spawn("SourceRoot");
            var sourceChild = Spawn("Source", sourceRoot.transform);
            var sourceLight = sourceChild.AddComponent<Light>();
            sourceLight.intensity = 1.23f;

            var state = ComponentCapturer.Capture(sourceLight, sourceRoot.transform);

            var targetRoot = Spawn("TargetRoot");
            Spawn("Source", targetRoot.transform); // no Light yet

            var result = ComponentApplier.Apply(state, targetRoot, createIfMissing: true);

            Assert.IsTrue(result.IsSuccess, result.Message);
            var added = targetRoot.transform.Find("Source").GetComponent<Light>();
            Assert.IsNotNull(added);
            Assert.AreEqual(1.23f, added.intensity, 0.0001f);
        }

        [Test]
        public void Apply_ComponentMissing_WithoutCreateIfMissing_ReturnsFailure()
        {
            var targetRoot = Spawn("TargetRoot");
            Spawn("Source", targetRoot.transform);

            var state = new ComponentState
            {
                path = "Source",
                type = typeof(Light).FullName,
            };

            var result = ComponentApplier.Apply(state, targetRoot, createIfMissing: false);

            Assert.AreEqual(ApplyResultKind.ComponentMissing, result.Kind);
        }

        [Test]
        public void Capture_ThenApply_DisambiguatesMultipleSameTypeComponents_ByIndex()
        {
            var sourceRoot = Spawn("SourceRoot");
            var sourceChild = Spawn("Source", sourceRoot.transform);
            var firstSourceAudio = sourceChild.AddComponent<AudioSource>();
            firstSourceAudio.volume = 0.25f;
            var secondSourceAudio = sourceChild.AddComponent<AudioSource>();
            secondSourceAudio.volume = 0.75f;

            var firstState = ComponentCapturer.Capture(firstSourceAudio, sourceRoot.transform);
            var secondState = ComponentCapturer.Capture(secondSourceAudio, sourceRoot.transform);
            Assert.AreEqual(0, firstState.componentIndex);
            Assert.AreEqual(1, secondState.componentIndex);

            var targetRoot = Spawn("TargetRoot");
            var targetChild = Spawn("Source", targetRoot.transform);
            targetChild.AddComponent<AudioSource>();
            targetChild.AddComponent<AudioSource>();

            var firstResult = ComponentApplier.Apply(firstState, targetRoot, createIfMissing: false);
            var secondResult = ComponentApplier.Apply(secondState, targetRoot, createIfMissing: false);

            Assert.IsTrue(firstResult.IsSuccess, firstResult.Message);
            Assert.IsTrue(secondResult.IsSuccess, secondResult.Message);

            var targetAudios = targetChild.GetComponents<AudioSource>();
            Assert.AreEqual(0.25f, targetAudios[0].volume, 0.0001f, "the first captured instance must apply onto the first target instance, not overwrite the same one twice");
            Assert.AreEqual(0.75f, targetAudios[1].volume, 0.0001f);
        }

        [Test]
        public void Apply_NonComponentType_ReturnsFailureInsteadOfThrowing()
        {
            var targetRoot = Spawn("TargetRoot");
            Spawn("Source", targetRoot.transform);

            var state = new ComponentState
            {
                path = "Source",
                type = typeof(string).FullName, // resolvable type, but not a Component
            };

            var result = ComponentApplier.Apply(state, targetRoot, createIfMissing: true);

            Assert.AreEqual(ApplyResultKind.ComponentTypeUnresolved, result.Kind);
        }

        [Test]
        public void Capture_UsesRelativePathFromGivenRoot()
        {
            var containerRoot = Spawn("Container");
            var nested = Spawn("Child", containerRoot.transform);
            var light = nested.AddComponent<Light>();

            var state = ComponentCapturer.Capture(light, containerRoot.transform);

            Assert.AreEqual("Child", state.path);
        }

        private class ReservedPropertyTestBehaviour : MonoBehaviour
        {
        }

        private class OtherReservedPropertyTestBehaviour : MonoBehaviour
        {
        }

        [Test]
        public void Capture_NeverIncludes_ReservedPropertyNames()
        {
            var root = Spawn("Root");
            var behaviour = root.AddComponent<ReservedPropertyTestBehaviour>();

            var state = ComponentCapturer.Capture(behaviour, root.transform);

            foreach (var reserved in ReservedPropertyNames.Names)
            {
                Assert.IsFalse(state.fields.Exists(f => f.key == reserved), $"'{reserved}' must never be captured (fields)");
                Assert.IsFalse(state.assetRefs.Exists(a => a.key == reserved), $"'{reserved}' must never be captured (assetRefs)");
            }
        }

        [Test]
        public void Apply_RefusesToWrite_MScript_EvenIfPresentInState()
        {
            // Simulates a hand-edited or corrupted commit file: a
            // ComponentState whose assetRefs targets "m_Script" -- something
            // ComponentCapturer itself would never produce (see the test
            // above), but ComponentApplier must still defend against on the
            // way in, since a value here would silently swap which script
            // drives an existing component.
            var root = Spawn("Root");
            var behaviour = root.AddComponent<ReservedPropertyTestBehaviour>();
            var originalScript = MonoScript.FromMonoBehaviour(behaviour);

            // A different script's guid, so the assertion below actually
            // fails if the reserved-property guard regresses (rather than
            // trivially passing because "the other script" happens to be
            // the same one).
            var otherGo = new GameObject("Other");
            var otherBehaviour = otherGo.AddComponent<OtherReservedPropertyTestBehaviour>();
            var otherScript = MonoScript.FromMonoBehaviour(otherBehaviour);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(otherScript, out var otherScriptGuid, out long otherLocalId);
            Object.DestroyImmediate(otherGo);

            var maliciousState = new ComponentState
            {
                path = string.Empty,
                type = typeof(ReservedPropertyTestBehaviour).FullName,
                assetRefs = { new AssetRef { key = "m_Script", guid = otherScriptGuid, localId = otherLocalId } },
            };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Refusing to write reserved property.*"));
            var result = ComponentApplier.Apply(maliciousState, root, createIfMissing: false);

            Assert.IsTrue(result.IsSuccess); // the reserved field is skipped, not a hard failure
            var so = new SerializedObject(behaviour);
            Assert.AreSame(originalScript, so.FindProperty("m_Script").objectReferenceValue,
                "m_Script must be left exactly as it was, never overwritten from commit data");
        }
    }
}
