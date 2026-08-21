using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Editor.AvatarReferences;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using AvatarVcs.Editor.Model;
using AvatarVcs.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

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
        private GameObject accessoryPrefab;

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

            var accessorySource = new GameObject("Accessory");
            accessoryPrefab = PrefabUtility.SaveAsPrefabAsset(accessorySource, $"{TestAssetDir}/Accessory.prefab");
            Object.DestroyImmediate(accessorySource);
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

        private class ExtraComponent : MonoBehaviour
        {
            public float value;
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

        // AvatarReferenceCollector is the piece that used to be entirely
        // missing: capture/apply worked, but nothing ever called them for a
        // real commit, so a tracked Body's BlendShapes were silently never
        // recorded. These tests cover the marker-driven collection path
        // BranchManager.Commit now uses.

        [Test]
        public void CollectFromTrackedTargets_FindsMarkedTarget_CapturesBlendShapesAndMaterials()
        {
            var avatarRoot = Spawn("Avatar");
            var body = Spawn("Body", avatarRoot.transform);
            body.AddComponent<AvatarVcsTrackedReference>();
            var renderer = body.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = testMesh;
            renderer.SetBlendShapeWeight(0, 33f);
            renderer.sharedMaterials = new[] { materialA };

            var (avatarReferences, _) = AvatarReferenceCollector.CollectFromTrackedTargets(avatarRoot);

            Assert.AreEqual(1, avatarReferences.Count);
            Assert.AreEqual("Body", avatarReferences[0].path);
            Assert.AreEqual(33f, avatarReferences[0].blendShapes[0].weight, 0.0001f);
            Assert.AreEqual(materialAGuid, avatarReferences[0].materials[0].guid);
        }

        [Test]
        public void CollectFromTrackedTargets_IgnoresUntrackedTargets()
        {
            var avatarRoot = Spawn("Avatar");
            var body = Spawn("Body", avatarRoot.transform);
            var renderer = body.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = testMesh; // no AvatarVcsTrackedReference added

            var (avatarReferences, materialSettings) = AvatarReferenceCollector.CollectFromTrackedTargets(avatarRoot);

            Assert.AreEqual(0, avatarReferences.Count);
            Assert.AreEqual(0, materialSettings.Count);
        }

        [Test]
        public void CollectFromTrackedTargets_SkipsDescendantWhenAncestorAlreadyTracked()
        {
            // Tracking both an ancestor and one of its descendants would
            // otherwise capture the descendant's fields twice (once via the
            // ancestor's recursive walk, once as its own independent entry)
            // -- duplicate data, duplicate diff rows, no new information.
            var avatarRoot = Spawn("Avatar");
            avatarRoot.AddComponent<AvatarVcsTrackedReference>();
            var body = Spawn("Body", avatarRoot.transform);
            body.AddComponent<AvatarVcsTrackedReference>();
            body.AddComponent<SkinnedMeshRenderer>().sharedMesh = testMesh;

            var (avatarReferences, _) = AvatarReferenceCollector.CollectFromTrackedTargets(avatarRoot);

            Assert.AreEqual(1, avatarReferences.Count);
            Assert.AreEqual(string.Empty, avatarReferences[0].path); // the avatar root itself, not Body
        }

        [Test]
        public void CollectFromTrackedTargets_UnsupportedShader_SkipsMaterialSettingsButKeepsMaterialReference()
        {
            // materialA uses the built-in Standard shader (see OneTimeSetUp),
            // which ShaderPropertyMap doesn't map.
            // avatarReferences' material *reference* tracking is shader-
            // agnostic and must still see it; only the shader-settings
            // duplication bonus is skipped.
            var avatarRoot = Spawn("Avatar");
            var body = Spawn("Body", avatarRoot.transform);
            body.AddComponent<AvatarVcsTrackedReference>();
            var renderer = body.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { materialA };

            var (avatarReferences, materialSettings) = AvatarReferenceCollector.CollectFromTrackedTargets(avatarRoot);

            Assert.AreEqual(1, avatarReferences[0].materials.Count);
            Assert.AreEqual(0, materialSettings.Count);
        }

        [Test]
        public void BranchManagerCommit_CapturesTrackedBlendShapeWeight()
        {
            var avatarRoot = Spawn("Avatar");
            var body = Spawn("Body", avatarRoot.transform);
            body.AddComponent<AvatarVcsTrackedReference>();
            var renderer = body.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = testMesh;
            renderer.SetBlendShapeWeight(0, 77f);

            var avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            try
            {
                var commit = BranchManager.Commit(avatarRoot, "with tracked blend shape");

                var loaded = CommitStore.LoadCommit(avatarGuid, commit.commitId);
                var bodyRef = loaded.avatarReferences.Single(r => r.path == "Body");
                Assert.AreEqual(77f, bodyRef.blendShapes.First(b => b.name == "Shape_A").weight, 0.0001f);
            }
            finally
            {
                CommitStore.DeleteAvatarHistory(avatarGuid);
            }
        }

        [Test]
        public void RestoreToCommit_RestoresTrackedBlendShapeWeight_AfterDrift()
        {
            var avatarRoot = Spawn("Avatar");
            var body = Spawn("Body", avatarRoot.transform);
            body.AddComponent<AvatarVcsTrackedReference>();
            var renderer = body.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = testMesh;
            renderer.SetBlendShapeWeight(0, 60f);

            var avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            try
            {
                var commit = BranchManager.Commit(avatarRoot, "checkpoint");

                renderer.SetBlendShapeWeight(0, 0f); // simulate drift after committing

                var result = BranchManager.RestoreToCommit(avatarRoot, commit.commitId);

                Assert.IsTrue(result.IsSuccess);
                Assert.AreEqual(60f, renderer.GetBlendShapeWeight(0), 0.0001f);
            }
            finally
            {
                CommitStore.DeleteAvatarHistory(avatarGuid);
            }
        }

        // Broadened tracking (design doc 1.4, revised): a marked target's
        // whole subtree, not just its own BlendShape/material, is captured
        // generically (same ComponentCapturer/ComponentApplier containers
        // use), overwrite-only. These tests cover that recursive path.

        [Test]
        public void Capture_RecursivelyCapturesComponentFieldsOnDescendants()
        {
            var avatarRoot = Spawn("Avatar");
            var body = Spawn("Body", avatarRoot.transform);
            var child = Spawn("Extra", body.transform);
            child.AddComponent<ExtraComponent>().value = 5f;

            var state = AvatarReferenceCapture.Capture(body.transform, avatarRoot.transform);

            var captured = state.components.Single(c => c.type == typeof(ExtraComponent).FullName);
            Assert.AreEqual("Extra", captured.path);
            Assert.AreEqual("5", captured.fields.Single(f => f.key == "value").value);
        }

        [Test]
        public void Capture_IncludesTargetsOwnNonRendererComponents()
        {
            var avatarRoot = Spawn("Avatar");
            var body = Spawn("Body", avatarRoot.transform);
            body.AddComponent<ExtraComponent>().value = 9f;

            var state = AvatarReferenceCapture.Capture(body.transform, avatarRoot.transform);

            var captured = state.components.Single(c => c.type == typeof(ExtraComponent).FullName);
            Assert.AreEqual(string.Empty, captured.path); // on target itself
            Assert.AreEqual("9", captured.fields.Single(f => f.key == "value").value);
        }

        [Test]
        public void Capture_TracksPositionOfPrefabInstanceDescendant_ButNotOfNonPrefabDescendant()
        {
            // Issue #53: an accessory placed directly under a tracked
            // target (e.g. attached to an Armature bone, bypassing
            // container management) is a real, GUID-recoverable prefab
            // instance -- its position is worth restoring. A plain
            // (non-prefab) child, standing in for a bone, must not have its
            // Transform captured at all.
            var avatarRoot = Spawn("Avatar");
            var armature = Spawn("Armature", avatarRoot.transform);
            var bone = Spawn("Hip", armature.transform); // stand-in for a bone: not a prefab instance
            var accessory = (GameObject)PrefabUtility.InstantiatePrefab(accessoryPrefab, bone.transform);
            spawned.Add(accessory);
            accessory.transform.localPosition = new Vector3(0.1f, 0.2f, 0.3f);

            var state = AvatarReferenceCapture.Capture(armature.transform, avatarRoot.transform);

            var transformStates = state.components.Where(c => c.type == typeof(Transform).FullName).ToList();
            Assert.AreEqual(1, transformStates.Count, "only the prefab instance's Transform should be captured");
            var captured = transformStates.Single();
            Assert.AreEqual("Hip/Accessory", captured.path);
            var position = captured.fields.Single(f => f.key == "m_LocalPosition").value.Split(',').Select(float.Parse).ToArray();
            Assert.Less(Vector3.Distance(new Vector3(0.1f, 0.2f, 0.3f), new Vector3(position[0], position[1], position[2])), 0.0001f);
        }

        [Test]
        public void Capture_NeverCapturesTargetsOwnTransform_EvenIfTargetIsAPrefabInstance()
        {
            // target's own placement is out of scope here even when it
            // happens to be a prefab instance itself -- only descendants'
            // positions are tracked, not the deliberately-chosen tracking
            // anchor's own scene placement.
            var avatarRoot = Spawn("Avatar");
            var target = (GameObject)PrefabUtility.InstantiatePrefab(accessoryPrefab, avatarRoot.transform);
            spawned.Add(target);
            target.transform.localPosition = new Vector3(1f, 2f, 3f);

            var state = AvatarReferenceCapture.Capture(target.transform, avatarRoot.transform);

            Assert.IsFalse(state.components.Any(c => c.type == typeof(Transform).FullName));
        }

        [Test]
        public void BranchManagerCommit_And_RestoreToCommit_RoundTripsPrefabInstancePositionUnderArmature()
        {
            var avatarRoot = Spawn("Avatar");
            var armature = Spawn("Armature", avatarRoot.transform);
            armature.AddComponent<AvatarVcsTrackedReference>();
            var bone = Spawn("Hip", armature.transform);
            var accessory = (GameObject)PrefabUtility.InstantiatePrefab(accessoryPrefab, bone.transform);
            spawned.Add(accessory);
            accessory.transform.localPosition = new Vector3(0.5f, 0f, 0f);

            var avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            try
            {
                var commit = BranchManager.Commit(avatarRoot, "with accessory position");

                accessory.transform.localPosition = Vector3.zero; // simulate drift

                var result = BranchManager.RestoreToCommit(avatarRoot, commit.commitId);

                Assert.IsTrue(result.IsSuccess);
                Assert.Less(Vector3.Distance(new Vector3(0.5f, 0f, 0f), accessory.transform.localPosition), 0.0001f);
            }
            finally
            {
                CommitStore.DeleteAvatarHistory(avatarGuid);
            }
        }

        [Test]
        public void Capture_ExcludesAvatarVcsRootSubtree_WhenTargetIsAvatarRoot()
        {
            var avatarRoot = Spawn("Avatar");
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(root, "outfit_a");
            container.AddComponent<ExtraComponent>().value = 1f;

            var state = AvatarReferenceCapture.Capture(avatarRoot.transform, avatarRoot.transform);

            Assert.IsFalse(state.components.Any(c => c.type == typeof(ExtraComponent).FullName),
                "components inside [AvatarVCS] must never be captured by the avatar-side path");
        }

        [Test]
        public void Capture_ExcludesAvatarVcsRootSubtree_SurvivesRename()
        {
            var avatarRoot = Spawn("Avatar");
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(root, "outfit_a");
            container.AddComponent<ExtraComponent>().value = 1f;
            root.name = "Renamed_AvatarVCS_Root";

            var state = AvatarReferenceCapture.Capture(avatarRoot.transform, avatarRoot.transform);

            Assert.IsFalse(state.components.Any(c => c.type == typeof(ExtraComponent).FullName),
                "exclusion must hold even after a manual rename of [AvatarVCS]");
        }

        [Test]
        public void Capture_StripsBlendShapeAndMaterialFields_FromGenericRendererCapture()
        {
            var avatarRoot = Spawn("Avatar");
            var body = Spawn("Body", avatarRoot.transform);
            var renderer = body.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = testMesh;
            renderer.sharedMaterials = new[] { materialA };

            var state = AvatarReferenceCapture.Capture(body.transform, avatarRoot.transform);

            var rendererState = state.components.SingleOrDefault(c => c.type == typeof(SkinnedMeshRenderer).FullName);
            if (rendererState != null)
            {
                Assert.IsFalse(rendererState.fields.Any(f => f.key.StartsWith("m_BlendShapeWeights")));
                Assert.IsFalse(rendererState.assetRefs.Any(a => a.key.StartsWith("m_Materials")));
            }
            // The narrow, robust path must still own these regardless:
            Assert.AreEqual(2, state.blendShapes.Count);
            Assert.AreEqual(1, state.materials.Count);
        }

        [Test]
        public void Apply_RestoresGenericComponentField_OverwriteOnly()
        {
            var avatarRoot = Spawn("Avatar");
            var body = Spawn("Body", avatarRoot.transform);
            var child = Spawn("Extra", body.transform);
            var extra = child.AddComponent<ExtraComponent>();
            extra.value = 12f;

            var captured = AvatarReferenceCapture.Capture(body.transform, avatarRoot.transform);

            extra.value = 999f; // simulate drift

            AvatarReferenceApplier.Apply(captured, avatarRoot.transform);

            Assert.AreEqual(12f, extra.value, 0.0001f);
        }

        [Test]
        public void Apply_ComponentMissingOnLiveTarget_WarnsAndDoesNotCreate()
        {
            var avatarRoot = Spawn("Avatar");
            var body = Spawn("Body", avatarRoot.transform);

            var state = new AvatarReferenceState { path = "Body" };
            state.components.Add(new ComponentState
            {
                path = string.Empty,
                type = typeof(ExtraComponent).FullName,
            });

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Failed to restore component.*"));
            AvatarReferenceApplier.Apply(state, avatarRoot.transform);

            Assert.IsNull(body.GetComponent<ExtraComponent>(), "createIfMissing is false for this path");
        }

        [Test]
        public void BranchManagerCommit_And_RestoreToCommit_RoundTripsGenericComponentField_AfterDrift()
        {
            var avatarRoot = Spawn("Avatar");
            var body = Spawn("Body", avatarRoot.transform);
            body.AddComponent<AvatarVcsTrackedReference>();
            var child = Spawn("Extra", body.transform);
            var extra = child.AddComponent<ExtraComponent>();
            extra.value = 21f;

            var avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            try
            {
                var commit = BranchManager.Commit(avatarRoot, "with generic component");

                extra.value = 0f; // simulate drift after committing

                var result = BranchManager.RestoreToCommit(avatarRoot, commit.commitId);

                Assert.IsTrue(result.IsSuccess);
                Assert.AreEqual(21f, extra.value, 0.0001f);
            }
            finally
            {
                CommitStore.DeleteAvatarHistory(avatarGuid);
            }
        }

        [Test]
        public void BranchManagerCommit_And_RestoreToCommit_RoundTripsAvatarRootsOwnComponentField_AfterDrift()
        {
            // Issue #45: reported that settings on the avatar root itself
            // (e.g. VRCAvatarDescriptor) don't come back after a checkout
            // when AvatarVcsTrackedReference is placed on the avatar root
            // directly, rather than on a child like Body/Armature. This
            // exercises the exact same path through the real commit/
            // checkout pipeline (not just Capture/Apply in isolation) to
            // confirm the general mechanism -- a component sitting directly
            // on a tracked avatar root -- round-trips correctly.
            var avatarRoot = Spawn("Avatar");
            avatarRoot.AddComponent<AvatarVcsTrackedReference>();
            var extra = avatarRoot.AddComponent<ExtraComponent>();
            extra.value = 42f;

            var avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            try
            {
                var commit = BranchManager.Commit(avatarRoot, "with avatar root's own component");

                extra.value = 0f; // simulate drift after committing

                var result = BranchManager.RestoreToCommit(avatarRoot, commit.commitId);

                Assert.IsTrue(result.IsSuccess);
                Assert.AreEqual(42f, extra.value, 0.0001f);
            }
            finally
            {
                CommitStore.DeleteAvatarHistory(avatarGuid);
            }
        }
    }
}
