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
    /// checkout, but BlendShape weights / material slots / active state the
    /// user adjusted inside them are now recorded and re-applied on top.
    /// </summary>
    public class ContainerInnerPropertyTests
    {
        private const string Dir = "Assets/AvatarVcsTests_ContainerInner_Temp";
        private GameObject prefabSource;
        private Mesh mesh;
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
            // Must be a saved asset: a runtime Mesh can't be referenced by
            // the prefab, so the regenerated instance's SMR would have no
            // mesh and nothing to write blend shape weights onto.
            AssetDatabase.CreateAsset(mesh, $"{Dir}/OutfitMesh.asset");

            var src = new GameObject("Outfit");
            src.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
            new GameObject("Toggle").transform.SetParent(src.transform);
            prefabSource = PrefabUtility.SaveAsPrefabAsset(src, $"{Dir}/Outfit.prefab");
            Object.DestroyImmediate(src);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            // mesh is an asset now -- deleting the folder removes it.
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

        [Test]
        public void ContainerInnerBlendShapeAndActiveState_RoundTripThroughCheckout_AfterDrift()
        {
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(root, "outfit_a");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabSource, container.transform);

            instance.GetComponent<SkinnedMeshRenderer>().SetBlendShapeWeight(0, 73f);
            instance.transform.Find("Toggle").gameObject.SetActive(false);

            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var commit = BranchManager.Commit(avatarRoot, "outfit tweaked");

            // Drift both back to prefab defaults.
            instance.GetComponent<SkinnedMeshRenderer>().SetBlendShapeWeight(0, 0f);
            instance.transform.Find("Toggle").gameObject.SetActive(true);

            var result = BranchManager.RestoreToCommit(avatarRoot, commit.commitId);
            Assert.IsTrue(result.IsSuccess);

            // The container (and its prefab instance) was regenerated from
            // scratch -- resolve the fresh objects by path.
            var freshContainer = ContainerManager.GetContainers(root).Single(c => c.name == "outfit_a");
            var freshSmr = freshContainer.Find("Outfit").GetComponent<SkinnedMeshRenderer>();
            var freshToggle = freshContainer.Find("Outfit/Toggle").gameObject;

            Assert.AreEqual(73f, freshSmr.GetBlendShapeWeight(0), 0.0001f,
                "inner BlendShape weight must be re-applied after the container regenerates");
            Assert.IsFalse(freshToggle.activeSelf,
                "inner active state must be re-applied after the container regenerates");
        }

        [Test]
        public void PreKan70Commit_NoInnerProperties_RegeneratesCleanWithoutError()
        {
            // A snapshot with empty blendShapes/materials/objectStates (an
            // older commit) must still restore, and simply not touch inner
            // state -- pre-KAN-70 behaviour.
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(root, "outfit_a");
            PrefabUtility.InstantiatePrefab(prefabSource, container.transform);

            var snapshot = ContainerCapture.CaptureContainer(container.transform, avatarRoot.transform);
            snapshot.blendShapes.Clear();
            snapshot.materials.Clear();
            snapshot.objectStates.Clear();

            Assert.DoesNotThrow(() => ContainerRestore.InstantiateContainer(snapshot, root));
            Assert.IsNotNull(ContainerManager.GetContainers(root).Single(c => c.name == "outfit_a").Find("Outfit"));
        }
    }
}
