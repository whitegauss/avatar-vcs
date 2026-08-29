using System.Collections.Generic;
using AvatarVcs.Editor.AvatarReferences;
using AvatarVcs.Core.Model;
using NUnit.Framework;
using UnityEngine;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// Covers issue #58: standalone BlendShape preset export/import, kept
    /// entirely separate from the commit/checkout system. Meant for sharing
    /// a BlendShape configuration outside this tool -- applied by name onto
    /// whatever mesh the importer has, not tied to any avatarGuid/commit.
    /// </summary>
    public class BlendShapePresetIOTests
    {
        private readonly List<GameObject> spawned = new();
        private readonly List<Mesh> meshes = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();

            foreach (var mesh in meshes)
                if (mesh != null) Object.DestroyImmediate(mesh);
            meshes.Clear();
        }

        private SkinnedMeshRenderer SpawnRenderer(string name, params string[] blendShapeNames)
        {
            var go = new GameObject(name);
            spawned.Add(go);

            var mesh = new Mesh
            {
                name = $"{name}Mesh",
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
            };
            foreach (var shapeName in blendShapeNames)
                mesh.AddBlendShapeFrame(shapeName, 100f, new[] { Vector3.zero, Vector3.zero, Vector3.zero }, null, null);
            meshes.Add(mesh);

            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            return renderer;
        }

        [Test]
        public void Capture_RecordsEveryBlendShape_IncludingZero()
        {
            var renderer = SpawnRenderer("Source", "Shape_A", "Shape_B");
            renderer.SetBlendShapeWeight(0, 80f); // Shape_B left at 0

            var preset = BlendShapePresetIO.Capture(renderer);

            Assert.AreEqual("SourceMesh", preset.meshName);
            Assert.AreEqual(2, preset.blendShapes.Count);
            Assert.AreEqual("Shape_A", preset.blendShapes[0].name);
            Assert.AreEqual(80f, preset.blendShapes[0].weight, 0.0001f);
            Assert.AreEqual("Shape_B", preset.blendShapes[1].name);
            Assert.AreEqual(0f, preset.blendShapes[1].weight, 0.0001f);
        }

        [Test]
        public void Apply_SetsMatchingBlendShapesByName()
        {
            var target = SpawnRenderer("Target", "Shape_A", "Shape_B");
            var preset = new BlendShapePreset { meshName = "Source" };
            preset.blendShapes.Add(new BlendShapeRef { name = "Shape_A", weight = 42f });
            preset.blendShapes.Add(new BlendShapeRef { name = "Shape_B", weight = 13f });

            var skipped = BlendShapePresetIO.Apply(preset, target);

            Assert.IsEmpty(skipped);
            Assert.AreEqual(42f, target.GetBlendShapeWeight(0), 0.0001f);
            Assert.AreEqual(13f, target.GetBlendShapeWeight(1), 0.0001f);
        }

        [Test]
        public void Apply_UnmatchedName_IsSkippedAndReported_WithoutThrowing()
        {
            // The whole point of a preset is importing onto a mesh that's
            // similar but not necessarily identical to the one it was
            // exported from -- an extra shape key the buyer doesn't have
            // must not abort the whole import.
            var target = SpawnRenderer("Target", "Shape_A");
            var preset = new BlendShapePreset { meshName = "Source" };
            preset.blendShapes.Add(new BlendShapeRef { name = "Shape_A", weight = 10f });
            preset.blendShapes.Add(new BlendShapeRef { name = "Shape_DoesNotExist", weight = 99f });

            List<string> skipped = null;
            Assert.DoesNotThrow(() => skipped = BlendShapePresetIO.Apply(preset, target));

            Assert.AreEqual(1, skipped.Count);
            Assert.AreEqual("Shape_DoesNotExist", skipped[0]);
            Assert.AreEqual(10f, target.GetBlendShapeWeight(0), 0.0001f);
        }

        [Test]
        public void CaptureThenApply_RoundTripsThroughJson()
        {
            // Exercises the exact serialization path the menu commands use
            // (JsonUtility.ToJson/FromJson), not just the in-memory model.
            var source = SpawnRenderer("Source", "Shape_A", "Shape_B");
            source.SetBlendShapeWeight(0, 25f);
            source.SetBlendShapeWeight(1, 75f);
            var target = SpawnRenderer("Target", "Shape_A", "Shape_B");

            var captured = BlendShapePresetIO.Capture(source);
            var json = JsonUtility.ToJson(captured);
            var roundTripped = JsonUtility.FromJson<BlendShapePreset>(json);

            var skipped = BlendShapePresetIO.Apply(roundTripped, target);

            Assert.IsEmpty(skipped);
            Assert.AreEqual(25f, target.GetBlendShapeWeight(0), 0.0001f);
            Assert.AreEqual(75f, target.GetBlendShapeWeight(1), 0.0001f);
        }

        [Test]
        public void Capture_NullRenderer_ThrowsArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(() => BlendShapePresetIO.Capture(null));
        }

        [Test]
        public void Capture_RendererWithNullSharedMesh_ThrowsArgumentException()
        {
            var go = new GameObject("NoMesh");
            spawned.Add(go);
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = null;

            Assert.Throws<System.ArgumentException>(() => BlendShapePresetIO.Capture(renderer));
        }

        [Test]
        public void Apply_NullPresetOrRenderer_ThrowsArgumentNullException()
        {
            var renderer = SpawnRenderer("Target", "Shape_A");
            var preset = new BlendShapePreset();

            Assert.Throws<System.ArgumentNullException>(() => BlendShapePresetIO.Apply(null, renderer));
            Assert.Throws<System.ArgumentNullException>(() => BlendShapePresetIO.Apply(preset, null));
        }

        [Test]
        public void Apply_RendererWithNullSharedMesh_ThrowsArgumentException()
        {
            var go = new GameObject("NoMeshTarget");
            spawned.Add(go);
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = null;
            var preset = new BlendShapePreset { meshName = "Source" };

            Assert.Throws<System.ArgumentException>(() => BlendShapePresetIO.Apply(preset, renderer));
        }

        [Test]
        public void Apply_EmptyBlendShapes_ReturnsEmptySkippedList()
        {
            var target = SpawnRenderer("Target", "Shape_A");
            var preset = new BlendShapePreset { meshName = "EmptyPreset" };

            var skipped = BlendShapePresetIO.Apply(preset, target);

            Assert.IsNotNull(skipped);
            Assert.IsEmpty(skipped);
        }

        [Test]
        public void Apply_ExtremeBlendShapeWeights_AppliesWithoutThrowing()
        {
            var target = SpawnRenderer("Target", "Shape_A", "Shape_B");
            var preset = new BlendShapePreset { meshName = "Extreme" };
            preset.blendShapes.Add(new BlendShapeRef { name = "Shape_A", weight = -50f });
            preset.blendShapes.Add(new BlendShapeRef { name = "Shape_B", weight = 200f });

            var skipped = BlendShapePresetIO.Apply(preset, target);

            Assert.IsEmpty(skipped);
            Assert.AreEqual(-50f, target.GetBlendShapeWeight(0), 0.0001f);
            Assert.AreEqual(200f, target.GetBlendShapeWeight(1), 0.0001f);
        }

        [Test]
        public void Apply_EntryWithMissingName_IsSkippedAndReported_WithoutThrowing()
        {
            // A hand-edited/corrupted preset file can be missing the "name"
            // key on one entry entirely -- JsonUtility leaves the field null
            // rather than failing to parse -- and GetBlendShapeIndex(null)
            // would throw if not guarded against.
            var target = SpawnRenderer("Target", "Shape_A");
            var preset = new BlendShapePreset { meshName = "Source" };
            preset.blendShapes.Add(new BlendShapeRef { name = "Shape_A", weight = 10f });
            preset.blendShapes.Add(new BlendShapeRef { name = null, weight = 99f });

            List<string> skipped = null;
            Assert.DoesNotThrow(() => skipped = BlendShapePresetIO.Apply(preset, target));

            Assert.AreEqual(1, skipped.Count);
            Assert.AreEqual(10f, target.GetBlendShapeWeight(0), 0.0001f);
        }
    }
}
