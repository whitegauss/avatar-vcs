using System.Linq;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using AvatarVcs.Editor.Model;
using AvatarVcs.Editor.Operations;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// Covers phase 4 from DesignDoc_avatar-vcs.md section 7.4:
    /// - task 12 (section 6.3): commits record referenced assets' content
    ///   hashes; checkout warns (without blocking) when one has changed.
    /// - task 13 (section 6.4): a project-wide GUID remapping lets a
    ///   re-imported asset's new GUID resolve transparently, both for the
    ///   missing-prefab pre-flight check and for real checkouts.
    /// </summary>
    public class AssetVersionAndGuidRemapTests
    {
        private const string TestAssetDir = "Assets/AvatarVcsTests_VersionRemap_Temp";
        private string prefabPath;
        private string prefabGuid;

        private GameObject avatarRoot;
        private string avatarGuid;
        private GuidRemapConfig originalRemapConfig;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (!AssetDatabase.IsValidFolder(TestAssetDir))
                AssetDatabase.CreateFolder("Assets", "AvatarVcsTests_VersionRemap_Temp");

            prefabPath = $"{TestAssetDir}/Outfit.prefab";
            var source = new GameObject("Outfit");
            PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
            Object.DestroyImmediate(source);
            prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
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
            // GuidRemapper is project-scoped, not per-avatar; snapshot/restore
            // it so these tests don't leak mappings into the real project.
            originalRemapConfig = GuidRemapper.Load();
        }

        [TearDown]
        public void TearDown()
        {
            GuidRemapper.Save(originalRemapConfig);
            if (avatarGuid != null)
                CommitStore.DeleteAvatarHistory(avatarGuid);
            if (avatarRoot != null)
                Object.DestroyImmediate(avatarRoot);
        }

        [Test]
        public void CreateCommit_RecordsAssetVersionForReferencedPrefab()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var root = ContainerManager.FindRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(root, "outfit_a");
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            PrefabUtility.InstantiatePrefab(prefabAsset, container.transform);

            var commit = CommitBuilder.CreateCommit(avatarRoot, "with prefab", "main", null);

            var entry = commit.assetVersions.SingleOrDefault(v => v.guid == prefabGuid);
            Assert.IsNotNull(entry, "commit should record an assetVersions entry for the referenced prefab");
            Assert.AreEqual("Outfit.prefab", entry.assetName);
            Assert.IsFalse(string.IsNullOrEmpty(entry.contentHash));
        }

        [Test]
        public void Checkout_WarnsWhenReferencedPrefabContentChanged_ButStillSucceeds()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var root = ContainerManager.FindRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(root, "outfit_a");
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            PrefabUtility.InstantiatePrefab(prefabAsset, container.transform);

            var commit = BranchManager.Commit(avatarRoot, "with prefab");
            Assert.IsTrue(commit.assetVersions.Any(v => v.guid == prefabGuid));

            // Change the prefab's content while keeping its GUID, simulating
            // "the outfit was updated in place" (design doc 6.2's first row).
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            contents.transform.position = new Vector3(1f, 2f, 3f);
            PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            PrefabUtility.UnloadPrefabContents(contents);

            var reloaded = CommitStore.LoadCommit(avatarGuid, commit.commitId);
            var result = CheckoutOperation.Checkout(reloaded, avatarRoot, "main", commit.commitId);

            Assert.IsTrue(result.IsSuccess, "a changed asset should warn, not block checkout");
            Assert.IsTrue(result.VersionWarnings.Count > 0);
        }

        [Test]
        public void GuidRemapper_AddMapping_ThenResolve_ReturnsNewGuid()
        {
            GuidRemapper.AddMapping("old-guid-123", "new-guid-456");

            Assert.AreEqual("new-guid-456", GuidRemapper.Resolve("old-guid-123"));
            Assert.AreEqual("unrelated-guid", GuidRemapper.Resolve("unrelated-guid"));
        }

        [Test]
        public void GuidRemapper_AddMapping_Twice_OverwritesPreviousTarget()
        {
            GuidRemapper.AddMapping("old-guid-123", "first-new-guid");
            GuidRemapper.AddMapping("old-guid-123", "second-new-guid");

            Assert.AreEqual("second-new-guid", GuidRemapper.Resolve("old-guid-123"));
        }

        [Test]
        public void HasMissingPrefabs_WithRemapping_ResolvesViaReplacementGuid()
        {
            var replacementPath = $"{TestAssetDir}/Replacement.prefab";
            var replacementSource = new GameObject("Replacement");
            PrefabUtility.SaveAsPrefabAsset(replacementSource, replacementPath);
            Object.DestroyImmediate(replacementSource);
            var replacementGuid = AssetDatabase.AssetPathToGUID(replacementPath);

            var movedPath = $"{TestAssetDir}/MovedAway.prefab";
            var movedSource = new GameObject("MovedAway");
            PrefabUtility.SaveAsPrefabAsset(movedSource, movedPath);
            Object.DestroyImmediate(movedSource);
            var movedGuid = AssetDatabase.AssetPathToGUID(movedPath);

            var snapshot = new ContainerSnapshot
            {
                containerId = "outfit_a",
                containerGuid = "g1",
                prefabGuids = { movedGuid },
            };

            // Simulate re-importing into a new location: the old GUID no
            // longer resolves to anything.
            AssetDatabase.DeleteAsset(movedPath);

            Assert.IsTrue(ContainerRestore.HasMissingPrefabs(snapshot, out var missing));
            CollectionAssert.Contains(missing, movedGuid);

            GuidRemapper.AddMapping(movedGuid, replacementGuid);

            Assert.IsFalse(ContainerRestore.HasMissingPrefabs(snapshot, out _),
                "remapped guid should resolve via its replacement");

            AssetDatabase.DeleteAsset(replacementPath);
        }
    }
}
