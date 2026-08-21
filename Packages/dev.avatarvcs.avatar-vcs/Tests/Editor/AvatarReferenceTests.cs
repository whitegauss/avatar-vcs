using System.Collections.Generic;
using AvatarVcs.Editor.AvatarReferences;
using AvatarVcs.Editor.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// Covers phase 2 tasks 6/7 from DesignDoc_avatar-vcs.md section 7.2:
    /// name-based blend shape record/restore, and material-slot GUID
    /// record/restore that never mutates the referenced material asset.
    ///
    /// Asset creation/deletion is done once per fixture (OneTimeSetUp/
    /// OneTimeTearDown) rather than per test: repeatedly creating and deleting
    /// an asset at the same path across many tests in quick succession trips
    /// Unity's "infinite import loop" detector.
    /// </summary>
    public class AvatarReferenceTests
    {
        private const string TestAssetDir = "Assets/AvatarVcsTests_AvatarRef_Temp";
        private readonly List<GameObject> spawned = new();
        private Mesh testMesh;
        private Material materialA;
        private Material materialB;
        private string materialAGuid;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (!AssetDatabase.IsValidFolder(TestAssetDir))
                AssetDatabase.CreateFolder("Assets", "AvatarVcsTests_AvatarRef_Temp");

            testMesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
            };
            testMesh.AddBlendShapeFrame("Shape_A", 100f, new[] { Vector3.zero, Vector3.zero, Vector3.zero }, null, null);
            testMesh.AddBlendShapeFrame("Shape_B", 100f, new[] { Vector3.zero, Vector3.zero, Vector3.zero }, null, null);

            materialA = new Material(Shader.Find("Standard"));
            materialB = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(materialA, $"{TestAssetDir}/MatA.mat");
            AssetDatabase.CreateAsset(materialB, $"{TestAssetDir}/MatB.mat");
            materialAGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(materialA));
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (testMesh != null) Object.DestroyImmediate(testMesh);

            if (AssetDatabase.IsValidFolder(TestAssetDir))
                AssetDatabase.DeleteAsset(TestAssetDir);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        private GameObject Spawn(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent);
            spawned.Add(go);
            return go;
        }

        [Test]
        public void Capture_RecordsAllBlendShapes_IncludingZero()
        {
            var avatarRoot = Spawn("Avatar");
            var body = Spawn("Body", avatarRoot.transform);
            var renderer = body.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = testMesh;
            renderer.SetBlendShapeWeight(0, 80f); // Shape_A; Shape_B left at 0

            var state = AvatarReferenceCapture.Capture(body.transform, avatarRoot.transform);

            Assert.AreEqual(2, state.blendShapes.Count);
            Assert.AreEqual("Shape_A", state.blendShapes[0].name);
            Assert.AreEqual(80f, state.blendShapes[0].weight, 0.0001f);
            Assert.AreEqual("Shape_B", state.blendShapes[1].name);
            Assert.AreEqual(0f, state.blendShapes[1].weight, 0.0001f);
        }

        [Test]
        public void CaptureThenApply_ExplicitZero_OverwritesNonZeroDrift()
        {
            // Simulates an outfit whose blend shape defaults to non-zero
            // (e.g. a "penetration guard" shape baked in at 100) that the
            // user explicitly turns down to 0. That choice must survive a
            // commit round trip instead of silently reverting to whatever
            // the mesh/prefab happens to default to.
            var avatarRoot = Spawn("Avatar");
            var body = Spawn("Body", avatarRoot.transform);
            var renderer = body.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = testMesh;
            renderer.SetBlendShapeWeight(0, 0f); // Shape_A explicitly zeroed

            var captured = AvatarReferenceCapture.Capture(body.transform, avatarRoot.transform);

            renderer.SetBlendShapeWeight(0, 100f); // simulate drift back up

            AvatarReferenceApplier.Apply(captured, avatarRoot.transform);

            Assert.AreEqual(0f, renderer.GetBlendShapeWeight(0), 0.0001f);
        }

        [Test]
        public void Apply_SetsNamedBlendShape_AndLeavesUnlistedShapesUntouched()
        {
            var avatarRoot = Spawn("Avatar");
            var body = Spawn("Body", avatarRoot.transform);
            var renderer = body.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = testMesh;
            renderer.SetBlendShapeWeight(1, 55f); // Shape_B pre-set, not in JSON

            var state = new AvatarReferenceState { path = "Body" };
            state.blendShapes.Add(new BlendShapeRef { name = "Shape_A", weight = 42f });

            AvatarReferenceApplier.Apply(state, avatarRoot.transform);

            Assert.AreEqual(42f, renderer.GetBlendShapeWeight(0), 0.0001f);
            Assert.AreEqual(55f, renderer.GetBlendShapeWeight(1), 0.0001f); // untouched
        }

        [Test]
        public void CaptureThenApply_MaterialReference_RoundTripsGuid_WithoutMutatingSource()
        {
            var avatarRoot = Spawn("Avatar");
            var body = Spawn("Body", avatarRoot.transform);
            var renderer = body.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { materialA };

            var captured = AvatarReferenceCapture.Capture(body.transform, avatarRoot.transform);
            Assert.AreEqual(1, captured.materials.Count);
            Assert.AreEqual(materialAGuid, captured.materials[0].guid);

            renderer.sharedMaterials = new[] { materialB }; // simulate drift

            AvatarReferenceApplier.Apply(captured, avatarRoot.transform);

            var appliedGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(renderer.sharedMaterials[0]));
            Assert.AreEqual(materialAGuid, appliedGuid);
        }
    }
}
