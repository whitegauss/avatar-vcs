using System.Linq;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using AvatarVcs.Core.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

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

        // Same check, but type-agnostic: the false-positive tests below name
        // non-Material assets, which LoadAssetAtPath<Material> would report as
        // gone even when the guard correctly left them alone.
        private static bool AnyAssetStillLoads(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            return !string.IsNullOrEmpty(path) && AssetDatabase.LoadMainAssetAtPath(path) != null;
        }

        /// <summary>
        /// Points the test avatar's renderer back at the source material, so
        /// no generated duplicate is in use. Collection is deliberately
        /// refused while the scene still holds one (KAN-92), so a test about
        /// collection has to let go of it first.
        /// </summary>
        private void ReleaseGeneratedMaterialFromScene() =>
            avatarRoot.transform.Find("Body").GetComponent<Renderer>().sharedMaterials = new[] { sourceMaterial };

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

            // Checkout leaves the duplicate in the renderer's slot, and a
            // material the scene is wearing is never collected (KAN-92).
            // This test is about the collection itself, so hand the slot back
            // to the source first.
            ReleaseGeneratedMaterialFromScene();

            CommitStore.DeleteCommit(avatarGuid, commit.commitId, force: true);

            Assert.IsFalse(AssetStillLoads(generatedGuid), "generated duplicate should be deleted along with the commit");
            Assert.IsNull(CommitStore.LoadCommit(avatarGuid, commit.commitId));
            Assert.IsFalse(CommitStore.LoadIndex(avatarGuid).entries.Any(e => e.commitId == commit.commitId));
        }

        // The reported data loss: checkout points the renderers at the
        // generated duplicates, so deleting the commit that produced them
        // deleted the materials the avatar was wearing. The planner only
        // asks whether another *commit* still references the asset; nothing
        // asked the scene. Harmless while generatedAssets was always empty,
        // real once lilToon variants started matching.
        [Test]
        public void DeleteCommit_DoesNotDeleteAGeneratedMaterialTheSceneIsStillUsing()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var commit = CommitWithMaterialSetting("with material", null);
            CheckoutOperation.Checkout(commit, avatarRoot, "main", null);

            var reloaded = CommitStore.LoadCommit(avatarGuid, commit.commitId);
            var generatedGuid = reloaded.materialSettings[0].generatedGuid;

            var renderer = avatarRoot.transform.Find("Body").GetComponent<Renderer>();
            Assert.AreEqual(generatedGuid,
                AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(renderer.sharedMaterials[0])),
                "sanity check: checkout put the generated duplicate in the renderer's slot");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Not deleting .*still using it"));
            CommitStore.DeleteCommit(avatarGuid, commit.commitId, force: true);

            Assert.IsTrue(AssetStillLoads(generatedGuid),
                "deleting the commit must not take the material the avatar is wearing with it");
            Assert.IsNotNull(renderer.sharedMaterials[0], "the renderer's slot must not be left empty");
        }

        [Test]
        public void DeleteCommit_StillDeletesAGeneratedMaterialNothingIsUsing()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var commit = CommitWithMaterialSetting("with material", null);
            CheckoutOperation.Checkout(commit, avatarRoot, "main", null);

            var reloaded = CommitStore.LoadCommit(avatarGuid, commit.commitId);
            var generatedGuid = reloaded.materialSettings[0].generatedGuid;

            // Point the renderer back at the source, so nothing in the scene
            // holds the duplicate any more -- the guard must not turn into
            // "never collect anything".
            ReleaseGeneratedMaterialFromScene();

            CommitStore.DeleteCommit(avatarGuid, commit.commitId, force: true);

            Assert.IsFalse(AssetStillLoads(generatedGuid),
                "an unreferenced duplicate is still garbage and must still be collected");
        }

        [Test]
        public void DeleteCommit_DoesNotDeleteAssetStillReferencedByAnotherCommit()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var first = CommitWithMaterialSetting("first", null);
            CheckoutOperation.Checkout(first, avatarRoot, "main", null);

            var reloadedFirst = CommitStore.LoadCommit(avatarGuid, first.commitId);
            var sharedGuid = reloadedFirst.materialSettings[0].generatedGuid;
            Assert.IsTrue(AssetStillLoads(sharedGuid));

            // Simulate a second commit (e.g. a branch point) that carries
            // forward the same already-generated duplicate rather than
            // regenerating its own.
            var second = CommitBuilder.CreateCommit(avatarRoot, "second", "main", first.commitId);
            second.materialSettings.Add(new MaterialSettingsState
            {
                targetPath = "Body",
                slot = 0,
                sourceMaterialGuid = sourceMaterialGuid,
                shader = "lilToon",
                generatedGuid = sharedGuid,
            });
            second.generatedAssets.Add(sharedGuid);
            CommitStore.SaveCommit(avatarGuid, second);

            CommitStore.DeleteCommit(avatarGuid, reloadedFirst.commitId, force: true);

            Assert.IsTrue(AssetStillLoads(sharedGuid), "asset still referenced by another commit must survive deletion");

            ReleaseGeneratedMaterialFromScene();
            CommitStore.DeleteCommit(avatarGuid, second.commitId, force: true);

            Assert.IsFalse(AssetStillLoads(sharedGuid), "once no commit references it, deleting the last one should clean it up");
        }

        // A user's own asset whose GUID a corrupt / hand-edited commit names
        // in generatedAssets. None of these match the "<name>_avatarvcs" (+
        // optional " N") suffix MaterialSettingsApplier produces, so
        // deletion must be refused even as the commit around it is deleted.
        // "Coat_avatarvcs_backup" specifically guards against a substring
        // match; "clip_avatarvcs v2" against " " + non-digits.
        [TestCase("UserOwned")]
        [TestCase("Coat_avatarvcs_backup")]
        [TestCase("clip_avatarvcs v2")]
        public void DeleteCommit_DoesNotDeleteNonAvatarVcsAssetListedInGeneratedAssets(string assetName)
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);

            var innocentPath = $"{TestAssetDir}/{assetName}.mat";
            AssetDatabase.CreateAsset(new Material(Shader.Find("Standard")), innocentPath);
            var innocentGuid = AssetDatabase.AssetPathToGUID(innocentPath);

            var commit = CommitBuilder.CreateCommit(avatarRoot, "corrupt generatedAssets", "main", null);
            commit.generatedAssets.Add(innocentGuid);
            CommitStore.SaveCommit(avatarGuid, commit);

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex($"Not deleting .*{System.Text.RegularExpressions.Regex.Escape(assetName)}"));
            CommitStore.DeleteCommit(avatarGuid, commit.commitId, force: true);

            Assert.IsTrue(AssetStillLoads(innocentGuid), "a non-AvatarVCS asset named in generatedAssets must survive");
            Assert.IsNull(CommitStore.LoadCommit(avatarGuid, commit.commitId), "the commit itself is still deleted");

            AssetDatabase.DeleteAsset(innocentPath);
        }

        // KAN-76: the producer only ever writes a Material, so a user asset
        // carrying the suffix on any other extension is not ours. The old
        // guard ignored the extension entirely and would have deleted this.
        [Test]
        public void DeleteCommit_DoesNotDeleteASuffixedNonMaterialAsset()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);

            var prefabPath = $"{TestAssetDir}/Hair_avatarvcs.prefab";
            var source = new GameObject("Hair_avatarvcs");
            PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
            Object.DestroyImmediate(source);
            var prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);

            var commit = CommitBuilder.CreateCommit(avatarRoot, "corrupt generatedAssets", "main", null);
            commit.generatedAssets.Add(prefabGuid);
            CommitStore.SaveCommit(avatarGuid, commit);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Not deleting .*Hair_avatarvcs"));
            CommitStore.DeleteCommit(avatarGuid, commit.commitId, force: true);

            Assert.IsTrue(AnyAssetStillLoads(prefabGuid), "a .prefab is never something this package generated");

            AssetDatabase.DeleteAsset(prefabPath);
        }

        // KAN-76: AssetDatabase.GUIDToAssetPath resolves folder GUIDs, and
        // DeleteAsset removes a folder recursively -- the worst outcome a
        // corrupt generatedAssets entry could reach.
        [Test]
        public void DeleteCommit_DoesNotDeleteAFolder()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);

            var folderPath = $"{TestAssetDir}/Stuff_avatarvcs";
            AssetDatabase.CreateFolder(TestAssetDir, "Stuff_avatarvcs");
            var folderGuid = AssetDatabase.AssetPathToGUID(folderPath);

            var commit = CommitBuilder.CreateCommit(avatarRoot, "corrupt generatedAssets", "main", null);
            commit.generatedAssets.Add(folderGuid);
            CommitStore.SaveCommit(avatarGuid, commit);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Not deleting .*Stuff_avatarvcs"));
            CommitStore.DeleteCommit(avatarGuid, commit.commitId, force: true);

            Assert.IsTrue(AssetDatabase.IsValidFolder(folderPath), "a folder must never be recursively deleted by the GC");

            AssetDatabase.DeleteAsset(folderPath);
        }

        [Test]
        public void DeleteCommit_RefusesWhenCommitIsABranchHead_UnlessForced()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var commit = BranchManager.Commit(avatarRoot, "head commit");

            Assert.Throws<System.InvalidOperationException>(() => CommitStore.DeleteCommit(avatarGuid, commit.commitId));
            Assert.DoesNotThrow(() => CommitStore.DeleteCommit(avatarGuid, commit.commitId, force: true));
        }

        [Test]
        public void DeleteCommits_RemovesEachCommitAndItsOwnGeneratedAsset()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var first = CommitWithMaterialSetting("first", null);
            CheckoutOperation.Checkout(first, avatarRoot, "main", null);
            var firstGuid = CommitStore.LoadCommit(avatarGuid, first.commitId).materialSettings[0].generatedGuid;

            var second = CommitBuilder.CreateCommit(avatarRoot, "second", "main", first.commitId);
            second.materialSettings.Add(new MaterialSettingsState
            {
                targetPath = "Body",
                slot = 0,
                sourceMaterialGuid = sourceMaterialGuid,
                shader = "lilToon",
                generatedGuid = "unused_guid_for_second",
            });
            second.generatedAssets.Add("unused_guid_for_second");
            CommitStore.SaveCommit(avatarGuid, second);

            var blocked = CommitStore.DeleteCommits(avatarGuid, new[] { first.commitId, second.commitId });

            Assert.IsEmpty(blocked);
            Assert.IsFalse(AssetStillLoads(firstGuid));
            Assert.IsNull(CommitStore.LoadCommit(avatarGuid, first.commitId));
            Assert.IsNull(CommitStore.LoadCommit(avatarGuid, second.commitId));
            // Not asserting the index is fully empty: CheckoutOperation.Checkout
            // above took its own "[auto] before checkout" safety-net commit,
            // which isn't in the delete request and correctly survives.
            var remainingIds = CommitStore.LoadIndex(avatarGuid).entries.Select(e => e.commitId).ToList();
            CollectionAssert.DoesNotContain(remainingIds, first.commitId);
            CollectionAssert.DoesNotContain(remainingIds, second.commitId);
        }

        [Test]
        public void DeleteCommits_SharedAssetBetweenTwoCommitsInTheSameBatch_IsDeletedOnceBothGone()
        {
            // The scenario the batch API exists for: DeleteCommit's
            // per-call "still referenced elsewhere" scan would see the
            // OTHER commit in this pair as a survivor (since it hasn't been
            // deleted yet at the time of that individual call), leaving the
            // asset orphaned. Deleting both together in one DeleteCommits
            // call must correctly recognize neither survives.
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var first = CommitWithMaterialSetting("first", null);
            CheckoutOperation.Checkout(first, avatarRoot, "main", null);
            var sharedGuid = CommitStore.LoadCommit(avatarGuid, first.commitId).materialSettings[0].generatedGuid;

            var second = CommitBuilder.CreateCommit(avatarRoot, "second", "main", first.commitId);
            second.materialSettings.Add(new MaterialSettingsState
            {
                targetPath = "Body",
                slot = 0,
                sourceMaterialGuid = sourceMaterialGuid,
                shader = "lilToon",
                generatedGuid = sharedGuid,
            });
            second.generatedAssets.Add(sharedGuid);
            CommitStore.SaveCommit(avatarGuid, second);

            var blocked = CommitStore.DeleteCommits(avatarGuid, new[] { first.commitId, second.commitId });

            Assert.IsEmpty(blocked);
            Assert.IsFalse(AssetStillLoads(sharedGuid), "neither commit referencing this guid survives the batch, so it must be cleaned up");
        }

        [Test]
        public void DeleteCommits_SkipsHeadBlockedCommit_ButDeletesTheRest()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var first = BranchManager.Commit(avatarRoot, "first");
            var head = BranchManager.Commit(avatarRoot, "head"); // current branch head

            var blocked = CommitStore.DeleteCommits(avatarGuid, new[] { first.commitId, head.commitId });

            CollectionAssert.AreEqual(new[] { head.commitId }, blocked);
            Assert.IsNull(CommitStore.LoadCommit(avatarGuid, first.commitId));
            Assert.IsNotNull(CommitStore.LoadCommit(avatarGuid, head.commitId), "head-blocked commit must survive");
        }
    }
}
