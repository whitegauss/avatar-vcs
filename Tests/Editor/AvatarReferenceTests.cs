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
    /// </summary>
    public class AvatarReferenceTests
    {
        private const string TestAssetDir = "Assets/AvatarVcsTests_AvatarRef_Temp";
        private readonly List<GameObject> spawned = new();
        private Mesh testMesh;
        private Material materialA;
        private Material materialB;
        private string materialAGuid;

        [SetUp]
        public void SetUp()
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

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();

            if (testMesh != null) Object.DestroyImmediate(testMesh);

            if (AssetDatabase.IsValidFolder(TestAssetDir))
                AssetDatabase.DeleteAsset(TestAssetDir);
        }

        private GameObject Spawn(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent);
            spawned.Add(go);
            return go;
        }

        [Test]
        public void Capture_OnlyRecordsNonZeroBlendShapes()
        {
            var avatarRoot = Spawn("Avatar");
            var body = Spawn("Body", avatarRoot.transform);
            var renderer = body.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = testMesh;
            renderer.SetBlendShapeWeight(0, 80f); // Shape_A; Shape_B left at 0

            var state = AvatarReferenceCapture.Capture(body.transform, avatarRoot.transform);

            Assert.AreEqual(1, state.blendShapes.Count);
            Assert.AreEqual("Shape_A", state.blendShapes[0].name);
            Assert.AreEqual(80f, state.blendShapes[0].weight, 0.0001f);
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
