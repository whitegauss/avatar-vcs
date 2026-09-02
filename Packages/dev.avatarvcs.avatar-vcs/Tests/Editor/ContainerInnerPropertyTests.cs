using System.Linq;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using AvatarVcs.Editor.Operations;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// KAN-70: a container destroys and regenerates its prefab instances on
    /// checkout, but the BlendShape weights / material slots / active-tag-
    /// layer state the user adjusted inside them are now recorded and
    /// re-applied on top.
    /// </summary>
    public class ContainerInnerPropertyTests
    {
        private const string Dir = "Assets/AvatarVcsTests_ContainerInner_Temp";
        private GameObject prefabSource;
        private Mesh mesh;
        private Material matA;
        private Material matB;
        private GameObject avatarRoot;
        private string avatarGuid;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "AvatarVcsTests_ContainerInner_Temp");

            mesh = new Mesh
            {
                name = "OutfitMesh",
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
            };
            mesh.AddBlendShapeFrame("Puff", 100f, new[] { Vector3.zero, Vector3.zero, Vector3.zero }, null, null);
            // Must be a saved asset: a runtime Mesh / Material can't be
            // referenced by the prefab, so the regenerated instance would
            // have nothing to write blend shape weights / material slots onto.
            AssetDatabase.CreateAsset(mesh, $"{Dir}/OutfitMesh.asset");

            matA = new Material(Shader.Find("Standard"));
            matB = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(matA, $"{Dir}/MatA.mat");
            AssetDatabase.CreateAsset(matB, $"{Dir}/MatB.mat");

            var src = new GameObject("Outfit");
            var smr = src.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.sharedMaterials = new[] { matA }; // slot 0 exists on the regenerated prefab
            new GameObject("Toggle").transform.SetParent(src.transform);
            prefabSource = PrefabUtility.SaveAsPrefabAsset(src, $"{Dir}/Outfit.prefab");
            Object.DestroyImmediate(src);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            // mesh/materials are assets now -- deleting the folder removes them.
            if (AssetDatabase.IsValidFolder(Dir)) AssetDatabase.DeleteAsset(Dir);
        }

        [SetUp]
        public void SetUp() => avatarRoot = new GameObject("Avatar");

        [TearDown]
        public void TearDown()
        {
            if (avatarGuid != null) { CommitStore.DeleteAvatarHistory(avatarGuid); avatarGuid = null; }
            if (avatarRoot != null) Object.DestroyImmediate(avatarRoot);
        }

        private static string MatPath(Object m) => AssetDatabase.GetAssetPath(m);

        [Test]
        public void ContainerInnerProperties_RoundTripThroughCheckout_AfterDrift()
        {
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(root, "outfit_a");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabSource, container.transform);
            var smr = instance.GetComponent<SkinnedMeshRenderer>();
            var toggle = instance.transform.Find("Toggle").gameObject;

            // The user's adjustments inside the container.
            smr.SetBlendShapeWeight(0, 73f);
            smr.sharedMaterials = new[] { matB };
            instance.tag = "EditorOnly";
            instance.layer = 3;
            toggle.SetActive(false);

            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var commit = BranchManager.Commit(avatarRoot, "outfit tweaked");

            // Drift everything back toward prefab defaults.
            smr.SetBlendShapeWeight(0, 0f);
            smr.sharedMaterials = new[] { matA };
            instance.tag = "Untagged";
            instance.layer = 0;
            toggle.SetActive(true);

            var result = BranchManager.RestoreToCommit(avatarRoot, commit.commitId);
            Assert.IsTrue(result.IsSuccess);

            // The container (and its prefab instance) was regenerated from
            // scratch -- resolve the fresh objects by path.
            var freshContainer = ContainerManager.GetContainers(root).Single(c => c.name == "outfit_a");
            var freshOutfit = freshContainer.Find("Outfit").gameObject;
            var freshSmr = freshOutfit.GetComponent<SkinnedMeshRenderer>();
            var freshToggle = freshContainer.Find("Outfit/Toggle").gameObject;

            Assert.AreEqual(73f, freshSmr.GetBlendShapeWeight(0), 0.0001f, "BlendShape weight re-applied");
            Assert.AreEqual(MatPath(matB), MatPath(freshSmr.sharedMaterials[0]), "material slot re-applied");
            Assert.AreEqual("EditorOnly", freshOutfit.tag, "tag re-applied");
            Assert.AreEqual(3, freshOutfit.layer, "layer re-applied");
            Assert.IsFalse(freshToggle.activeSelf, "active state re-applied");
        }

        // A container with no recorded inner properties: an older commit
        // (missing keys -> empty lists) or a hand-corrupted one (explicit
        // null). Both must regenerate cleanly and simply not touch inner
        // state (pre-KAN-70 behaviour / the ApplyInnerProperties null guard).
        [TestCase(true)]
        [TestCase(false)]
        public void ContainerWithoutInnerProperties_RegeneratesCleanWithoutError(bool nullLists)
        {
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(root, "outfit_a");
            PrefabUtility.InstantiatePrefab(prefabSource, container.transform);

            var snapshot = ContainerCapture.CaptureContainer(container.transform, avatarRoot.transform);
            if (nullLists)
            {
                snapshot.blendShapes = null;
                snapshot.materials = null;
                snapshot.objectStates = null;
            }
            else
            {
                snapshot.blendShapes.Clear();
                snapshot.materials.Clear();
                snapshot.objectStates.Clear();
            }

            Assert.DoesNotThrow(() => ContainerRestore.InstantiateContainer(snapshot, root));
            Assert.IsNotNull(ContainerManager.GetContainers(root).Single(c => c.name == "outfit_a").Find("Outfit"));
        }
    }
}
