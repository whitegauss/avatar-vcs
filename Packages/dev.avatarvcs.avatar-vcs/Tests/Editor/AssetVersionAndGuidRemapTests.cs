using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using AvatarVcs.Core.History;
using AvatarVcs.Core.Model;
using AvatarVcs.Editor.Operations;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

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
        public void CheckForChanges_NullRecorded_ReturnsEmptyWarningsInsteadOfThrowing()
        {
            System.Collections.Generic.List<string> warnings = null;
            Assert.DoesNotThrow(() => warnings = AssetVersionChecker.CheckForChanges(null));
            Assert.IsNotNull(warnings);
            Assert.IsEmpty(warnings);
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
        public void GuidRemapper_ChainedMapping_ResolvesTransitively()
        {
            // A got re-imported to B, then later B itself got re-imported to
            // C. A single-hop lookup would leave A stuck pointing at B.
            GuidRemapper.AddMapping("guid-a", "guid-b");
            GuidRemapper.AddMapping("guid-b", "guid-c");

            Assert.AreEqual("guid-c", GuidRemapper.Resolve("guid-a"));
        }

        [Test]
        public void GuidRemapper_CyclicMapping_TerminatesSafelyWithoutInfiniteLoop()
        {
            // Simulate a circular mapping: A -> B -> A
            GuidRemapper.AddMapping("cycle-a", "cycle-b");
            GuidRemapper.AddMapping("cycle-b", "cycle-a");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("GUID remapping .* hit a cycle"));

            // Must terminate without throwing or hanging, and resolve to one of the cycle nodes
            var resolved = GuidRemapper.Resolve("cycle-a");
            Assert.IsTrue(resolved == "cycle-a" || resolved == "cycle-b");
        }

        [Test]
        public void GuidRemapper_NullOrEmptyGuid_ReturnsInputUnchanged()
        {
            Assert.IsNull(GuidRemapper.Resolve(null));
            Assert.AreEqual("", GuidRemapper.Resolve(""));
        }

        // Regression tests for GuidRemapResolver.BuildIndex: a hand-edited
        // guid-remapping.json can deserialize a mapping entry with a null
        // oldGuid/newGuid (e.g. a bare "{}" element), and
        // Dictionary<string,string>'s indexer/ContainsKey throw
        // ArgumentNullException on a null key. These call GuidRemapResolver
        // directly with a hand-built GuidRemapConfig, bypassing GuidRemapper's
        // file I/O, the same way the resolver's own doc comment says it's
        // meant to be tested.

        [Test]
        public void GuidRemapResolver_BuildIndex_MappingWithNullOldGuid_SkippedInsteadOfThrowing()
        {
            var config = new GuidRemapConfig();
            config.mappings.Add(new GuidRemapEntry { oldGuid = null, newGuid = "new-guid" });

            Dictionary<string, string> index = null;
            Assert.DoesNotThrow(() => index = GuidRemapResolver.BuildIndex(config));
            Assert.IsEmpty(index);
        }

        [Test]
        public void GuidRemapResolver_BuildIndex_MappingWithNullNewGuid_SkippedInsteadOfThrowing()
        {
            var config = new GuidRemapConfig();
            config.mappings.Add(new GuidRemapEntry { oldGuid = "old-guid", newGuid = null });

            Dictionary<string, string> index = null;
            Assert.DoesNotThrow(() => index = GuidRemapResolver.BuildIndex(config));
            Assert.IsEmpty(index);
        }

        [Test]
        public void GuidRemapResolver_BuildIndex_MappingWithEmptyStrings_SkippedInsteadOfThrowing()
        {
            var config = new GuidRemapConfig();
            config.mappings.Add(new GuidRemapEntry { oldGuid = "", newGuid = "" });

            Dictionary<string, string> index = null;
            Assert.DoesNotThrow(() => index = GuidRemapResolver.BuildIndex(config));
            Assert.IsEmpty(index);
        }

        [Test]
        public void GuidRemapResolver_BuildIndex_ValidMappingAlongsideInvalidMapping_StillResolvesValidOne()
        {
            var config = new GuidRemapConfig();
            config.mappings.Add(new GuidRemapEntry { oldGuid = null, newGuid = "unused" });
            config.mappings.Add(new GuidRemapEntry { oldGuid = "old-guid", newGuid = "new-guid" });

            GuidResolution resolution = default;
            Assert.DoesNotThrow(() => resolution = GuidRemapResolver.Resolve(config, "old-guid"));
            Assert.AreEqual("new-guid", resolution.Guid);
            Assert.IsFalse(resolution.CycleDetected);
        }

        private const string RemapConfigPath = "ProjectSettings/AvatarVcs/guid-remapping.json";

        [Test]
        public void GuidRemapper_CorruptConfigFile_ReturnsEmptyConfigInsteadOfThrowing()
        {
            // Simulates a crash mid-write or a bad manual edit leaving
            // truncated/malformed JSON on disk, matching
            // CommitStore_CorruptCommitFile_ReturnsNullInsteadOfThrowing.
            System.IO.File.WriteAllText(RemapConfigPath, "{ not valid json");

            GuidRemapConfig loaded = null;
            Assert.DoesNotThrow(() => loaded = GuidRemapper.Load());
            Assert.IsNotNull(loaded);
            Assert.IsEmpty(loaded.mappings);

            Assert.DoesNotThrow(() => GuidRemapper.Resolve("any-guid"));
        }

        [Test]
        public void GuidRemapper_Save_DoesNotLeaveTempFileBehind()
        {
            GuidRemapper.AddMapping("temp-check-old", "temp-check-new");

            Assert.IsTrue(System.IO.File.Exists(RemapConfigPath));
            Assert.IsFalse(System.IO.File.Exists($"{RemapConfigPath}.tmp"), "the atomic-write temp file must be swapped away, not left behind");
        }

        [Test]
        public void CheckForChanges_ResolvesRecordedGuidThroughRemapping_BeforeWarningMissing()
        {
            // Record a version entry for a prefab, then simulate it having
            // been re-imported to a new GUID (old asset gone, new one takes
            // its place) and remapped. Without resolving through
            // GuidRemapper first, CheckForChanges would look up the old,
            // now-nonexistent GUID directly and always warn "no longer in
            // the project", even though the remapping already made the
            // reference resolve fine again.
            var oldPath = $"{TestAssetDir}/ReimportOld.prefab";
            var oldSource = new GameObject("ReimportOld");
            PrefabUtility.SaveAsPrefabAsset(oldSource, oldPath);
            Object.DestroyImmediate(oldSource);
            var oldGuid = AssetDatabase.AssetPathToGUID(oldPath);

            var entries = AssetVersionChecker.RecordVersions(new[] { oldGuid });
            Assert.AreEqual(1, entries.Count);

            var newPath = $"{TestAssetDir}/ReimportNew.prefab";
            var newSource = new GameObject("ReimportNew");
            PrefabUtility.SaveAsPrefabAsset(newSource, newPath);
            Object.DestroyImmediate(newSource);
            var newGuid = AssetDatabase.AssetPathToGUID(newPath);

            AssetDatabase.DeleteAsset(oldPath);
            GuidRemapper.AddMapping(oldGuid, newGuid);

            var warnings = AssetVersionChecker.CheckForChanges(entries);
            Assert.IsFalse(warnings.Any(w => w.Contains("no longer in the project")),
                "a remapped GUID should resolve to its replacement instead of warning it's missing");

            AssetDatabase.DeleteAsset(newPath);
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
