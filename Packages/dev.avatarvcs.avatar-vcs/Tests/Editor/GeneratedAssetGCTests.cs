using System.Linq;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using AvatarVcs.Editor.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// Covers the material-duplicate asset lifecycle from design doc section
    /// 4/1.4.3: re-checking out the same commit reuses its generated
    /// duplicate instead of creating a new one every time (previously it
    /// proliferated as "_avatarvcs 1.mat", "_avatarvcs 2.mat", ...), and
    /// deleting a commit removes the duplicates it generated.
    /// </summary>
    public class GeneratedAssetGCTests
    {
        private const string TestAssetDir = "Assets/AvatarVcsTests_GC_Temp";
        private Material sourceMaterial;
        private string sourceMaterialGuid;
        private GameObject avatarRoot;
        private string avatarGuid;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (!AssetDatabase.IsValidFolder(TestAssetDir))
                AssetDatabase.CreateFolder("Assets", "AvatarVcsTests_GC_Temp");

            sourceMaterial = new Material(Shader.Find("Standard"));
            var path = $"{TestAssetDir}/Source.mat";
            AssetDatabase.CreateAsset(sourceMaterial, path);
            sourceMaterialGuid = AssetDatabase.AssetPathToGUID(path);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (AssetDatabase.IsValidFolder(TestAssetDir))
                AssetDatabase.DeleteAsset(TestAssetDir);
        }

        [SetUp]
        public void SetUp()
        {
            avatarRoot = new GameObject("Avatar");
            var body = new GameObject("Body");
            body.transform.SetParent(avatarRoot.transform);
            var renderer = body.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { sourceMaterial };
        }

        [TearDown]
        public void TearDown()
        {
            if (avatarGuid != null)
                CommitStore.DeleteAvatarHistory(avatarGuid);
            if (avatarRoot != null)
                Object.DestroyImmediate(avatarRoot);
        }

        private Commit CommitWithMaterialSetting(string message, string parentCommitId)
        {
            var commit = CommitBuilder.CreateCommit(avatarRoot, message, "main", parentCommitId);
            commit.materialSettings.Add(new MaterialSettingsState
            {
                targetPath = "Body",
                slot = 0,
                sourceMaterialGuid = sourceMaterialGuid,
                shader = "lilToon",
                properties = { new MaterialPropertyValue { name = "_Color", type = "color", value = "0,1,0,1" } },
            });
            CommitStore.SaveCommit(avatarGuid, commit);
            return commit;
        }

        private static bool AssetStillLoads(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            // GUIDToAssetPath can keep resolving a just-deleted asset's path;
            // confirm it actually loads (same fix as ContainerRestore).
            return !string.IsNullOrEmpty(path) && AssetDatabase.LoadAssetAtPath<Material>(path) != null;
        }

        [Test]
        public void CheckoutSameCommitTwice_ReusesGeneratedMaterial_DoesNotProliferate()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var commit = CommitWithMaterialSetting("with material", null);

            var first = CheckoutOperation.Checkout(commit, avatarRoot, "main", null);
            Assert.IsTrue(first.IsSuccess);

            var reloaded = CommitStore.LoadCommit(avatarGuid, commit.commitId);
            Assert.IsFalse(string.IsNullOrEmpty(reloaded.materialSettings[0].generatedGuid),
                "generatedGuid should have been persisted back onto the commit");

            var second = CheckoutOperation.Checkout(reloaded, avatarRoot, "main", first.AutoCommitId);
            Assert.IsTrue(second.IsSuccess);

            var duplicateCount = AssetDatabase.FindAssets("t:Material", new[] { TestAssetDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Count(p => p.Contains("_avatarvcs"));
            Assert.AreEqual(1, duplicateCount, "checking out the same commit twice must not create a second duplicate");
        }

        [Test]
        public void DeleteCommit_RemovesGeneratedMaterialAsset()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var commit = CommitWithMaterialSetting("with material", null);
            CheckoutOperation.Checkout(commit, avatarRoot, "main", null);

            var reloaded = CommitStore.LoadCommit(avatarGuid, commit.commitId);
            var generatedGuid = reloaded.materialSettings[0].generatedGuid;
            Assert.IsTrue(AssetStillLoads(generatedGuid), "sanity check: duplicate should exist before delete");

            CommitStore.DeleteCommit(avatarGuid, commit.commitId, force: true);

            Assert.IsFalse(AssetStillLoads(generatedGuid), "generated duplicate should be deleted along with the commit");
            Assert.IsNull(CommitStore.LoadCommit(avatarGuid, commit.commitId));
            Assert.IsFalse(CommitStore.LoadIndex(avatarGuid).entries.Any(e => e.commitId == commit.commitId));
        }

        [Test]
        public void DeleteCommit_RefusesWhenCommitIsABranchHead_UnlessForced()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var commit = BranchManager.Commit(avatarRoot, "head commit");

            Assert.Throws<System.InvalidOperationException>(() => CommitStore.DeleteCommit(avatarGuid, commit.commitId));
            Assert.DoesNotThrow(() => CommitStore.DeleteCommit(avatarGuid, commit.commitId, force: true));
        }
    }
}
