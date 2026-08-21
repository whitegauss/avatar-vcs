using System.Collections.Generic;
using AvatarVcs.Editor.Apply;
using AvatarVcs.Editor.Capture;
using AvatarVcs.Editor.Model;
using NUnit.Framework;
using UnityEngine;

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
    }
}
