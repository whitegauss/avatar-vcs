using System.Linq;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using AvatarVcs.Editor.MaterialSettings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// The supported-shader path end to end, which nothing else covers: every
    /// other materialSettings test uses a Standard-shader material, so
    /// ShaderPropertyMap.IsSupported returns false and the whole
    /// capture -> commit -> checkout -> re-apply sequence is skipped. That gap
    /// is why two releases shipped with this path unverified.
    ///
    /// Needs a shader literally named "lilToon"; TestProject supplies a
    /// stand-in (Assets/AvatarVcsTestShaders/lilToon.shader). In a project
    /// without one -- including a user project with the real lilToon absent --
    /// these Ignore rather than fail.
    /// </summary>
    public class MaterialSettingsRoundTripTests
    {
        private const string Dir = "Assets/AvatarVcsTests_MatSettings_Temp";

        private Shader lilToon;
        private Shader lilToonOutline;
        private Mesh mesh;
        private Material coat;
        private Material outlinedCoat;
        private GameObject outfitPrefab;
        private GameObject nestedOutfitPrefab;
        private GameObject avatarRoot;
        private string avatarGuid;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            lilToon = Shader.Find("lilToon");
            lilToonOutline = Shader.Find("Hidden/lilToonOutline");
            if (lilToon == null) return;

            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "AvatarVcsTests_MatSettings_Temp");

            mesh = new Mesh
            {
                name = "CoatMesh",
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
            };
            AssetDatabase.CreateAsset(mesh, $"{Dir}/CoatMesh.asset");

            coat = new Material(lilToon);
            AssetDatabase.CreateAsset(coat, $"{Dir}/Coat.mat");

            if (lilToonOutline != null)
            {
                outlinedCoat = new Material(lilToonOutline);
                AssetDatabase.CreateAsset(outlinedCoat, $"{Dir}/OutlinedCoat.mat");
            }

            var src = new GameObject("Outfit");
            var filter = src.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = src.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { coat };
            outfitPrefab = PrefabUtility.SaveAsPrefabAsset(src, $"{Dir}/Outfit.prefab");
            Object.DestroyImmediate(src);

            // The usual shape of a real outfit: the renderer lives on a child,
            // not on the prefab root.
            var nested = new GameObject("NestedOutfit");
            var body = new GameObject("Body");
            body.transform.SetParent(nested.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            body.AddComponent<MeshRenderer>().sharedMaterials = new[] { coat };
            nestedOutfitPrefab = PrefabUtility.SaveAsPrefabAsset(nested, $"{Dir}/NestedOutfit.prefab");
            Object.DestroyImmediate(nested);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (AssetDatabase.IsValidFolder(Dir)) AssetDatabase.DeleteAsset(Dir);
        }

        [SetUp]
        public void SetUp()
        {
            if (lilToon == null) Assert.Ignore("No shader named 'lilToon' in this project.");
            // The colour every test starts from, reset here because tests
            // below deliberately write to the shared asset.
            coat.SetColor("_Color", Color.white);
            EditorUtility.SetDirty(coat);
            if (outlinedCoat != null)
            {
                outlinedCoat.SetColor("_Color", Color.white);
                EditorUtility.SetDirty(outlinedCoat);
            }
            AssetDatabase.SaveAssets();

            avatarRoot = new GameObject("Avatar");
        }

        [TearDown]
        public void TearDown()
        {
            if (avatarGuid != null) { CommitStore.DeleteAvatarHistory(avatarGuid); avatarGuid = null; }
            if (avatarRoot != null) Object.DestroyImmediate(avatarRoot);

            // Duplicates the checkouts above generated next to Coat.mat.
            foreach (var path in AssetDatabase.FindAssets("t:Material", new[] { Dir })
                         .Select(AssetDatabase.GUIDToAssetPath)
                         .Where(p => p.Contains("_avatarvcs")))
                AssetDatabase.DeleteAsset(path);
        }

        private Renderer LiveCoatRenderer()
        {
            var vcsRoot = ContainerManager.FindRoot(avatarRoot);
            return vcsRoot.GetComponentInChildren<Renderer>(includeInactive: true);
        }

        // The user's own repro (2026-09-03): commit, change the lilToon colour
        // in the Inspector, commit again, then check out the FIRST commit --
        // the colour stayed changed instead of going back.
        [Test]
        public void CheckingOutAnEarlierCommit_RestoresTheRecordedShaderColour()
        {
            var root = ContainerManager.EnsureRootWithDefaults(avatarRoot);
            PrefabUtility.InstantiatePrefab(outfitPrefab, root.transform);

            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);

            var first = BranchManager.Commit(avatarRoot, "initial, coat is white");

            // Capture has to have worked for the rest of this to mean anything,
            // so assert it separately -- it splits a capture bug from an apply
            // bug instead of leaving one red assert to interpret.
            var recorded = first.containers
                .SelectMany(c => c.materialSettings)
                .SelectMany(ms => ms.properties)
                .FirstOrDefault(p => p.name == "_Color");
            Assert.IsNotNull(recorded, "the first commit must record the lilToon _Color of the adopted container");
            Assert.AreEqual("1,1,1,1", recorded.value, "recorded as white");

            // Editing a material in the Inspector writes to the shared asset.
            coat.SetColor("_Color", Color.red);
            EditorUtility.SetDirty(coat);
            AssetDatabase.SaveAssets();

            BranchManager.Commit(avatarRoot, "coat turned red");

            var result = BranchManager.RestoreToCommit(avatarRoot, first.commitId);
            Assert.IsTrue(result.IsSuccess, $"checkout failed: {result.Kind}");

            var live = LiveCoatRenderer().sharedMaterials[0];
            Assert.AreEqual(Color.white, live.GetColor("_Color"),
                "checking out the first commit must put the coat back to white");
        }

        // The source .mat must never be written to, however the values are
        // restored -- design doc 1.4.3. Pinned separately so a fix for the
        // test above can't "work" by mutating the user's own asset.
        [Test]
        public void Checkout_NeverMutatesTheSourceMaterialAsset()
        {
            var root = ContainerManager.EnsureRootWithDefaults(avatarRoot);
            PrefabUtility.InstantiatePrefab(outfitPrefab, root.transform);

            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var first = BranchManager.Commit(avatarRoot, "initial, coat is white");

            coat.SetColor("_Color", Color.red);
            EditorUtility.SetDirty(coat);
            AssetDatabase.SaveAssets();

            BranchManager.RestoreToCommit(avatarRoot, first.commitId);

            var onDisk = AssetDatabase.LoadAssetAtPath<Material>($"{Dir}/Coat.mat");
            Assert.AreEqual(Color.red, onDisk.GetColor("_Color"),
                "the source asset keeps whatever the user last set; only the generated duplicate carries recorded values");
        }

        // Track Properties, not containers: a renderer sitting outside
        // [AvatarVCS] under the tracked avatar root. Reported separately as
        // "ルートでアバターの髪型でも起こってた", and it runs through
        // AvatarReferenceCollector + the commit's top-level materialSettings,
        // which is different code from the container path above.
        [Test]
        public void TrackedRenderer_CheckingOutAnEarlierCommit_RestoresTheRecordedShaderColour()
        {
            var hair = new GameObject("Hair");
            hair.transform.SetParent(avatarRoot.transform, false);
            var filter = hair.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            hair.AddComponent<MeshRenderer>().sharedMaterials = new[] { coat };

            ContainerManager.EnsureRootWithDefaults(avatarRoot);
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);

            var first = BranchManager.Commit(avatarRoot, "initial, hair is white");

            var recorded = first.materialSettings
                .SelectMany(ms => ms.properties)
                .FirstOrDefault(p => p.name == "_Color");
            Assert.IsNotNull(recorded, "the first commit must record the tracked renderer's lilToon _Color");
            Assert.AreEqual("1,1,1,1", recorded.value, "recorded as white");

            coat.SetColor("_Color", Color.red);
            EditorUtility.SetDirty(coat);
            AssetDatabase.SaveAssets();

            BranchManager.Commit(avatarRoot, "hair turned red");

            var result = BranchManager.RestoreToCommit(avatarRoot, first.commitId);
            Assert.IsTrue(result.IsSuccess, $"checkout failed: {result.Kind}");

            var live = avatarRoot.transform.Find("Hair").GetComponent<Renderer>().sharedMaterials[0];
            Assert.AreEqual(Color.white, live.GetColor("_Color"),
                "checking out the first commit must put the hair back to white");
        }

        // The second and later checkouts, which take MaterialSettingsApplier's
        // "reuse the existing duplicate" branch instead of creating one. By
        // then the renderer slot holds the generated duplicate, so the colour
        // the user edits in the Inspector is the duplicate's.
        [Test]
        public void SecondCheckoutOfTheSameCommit_StillRestoresTheRecordedShaderColour()
        {
            var root = ContainerManager.EnsureRootWithDefaults(avatarRoot);
            PrefabUtility.InstantiatePrefab(outfitPrefab, root.transform);

            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var first = BranchManager.Commit(avatarRoot, "initial, coat is white");

            // First checkout: generates the duplicate and puts it in the slot.
            Assert.IsTrue(BranchManager.RestoreToCommit(avatarRoot, first.commitId).IsSuccess);

            // Now the user edits what the renderer actually points at.
            var inSlot = LiveCoatRenderer().sharedMaterials[0];
            inSlot.SetColor("_Color", Color.red);
            EditorUtility.SetDirty(inSlot);
            AssetDatabase.SaveAssets();

            BranchManager.Commit(avatarRoot, "coat turned red");

            Assert.IsTrue(BranchManager.RestoreToCommit(avatarRoot, first.commitId).IsSuccess);

            Assert.AreEqual(Color.white, LiveCoatRenderer().sharedMaterials[0].GetColor("_Color"),
                "re-checking out the first commit must put the coat back to white");
        }

        // A renderer one level below the prefab root -- the usual shape of a
        // real outfit (Outfit/Body), unlike the flat prefab the tests above
        // use. Exercises the container-relative targetPath rebasing in
        // ContainerRestore.ApplyInnerProperties.
        [Test]
        public void NestedRenderer_CheckingOutAnEarlierCommit_RestoresTheRecordedShaderColour()
        {
            var root = ContainerManager.EnsureRootWithDefaults(avatarRoot);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(nestedOutfitPrefab, root.transform);

            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var first = BranchManager.Commit(avatarRoot, "initial, coat is white");

            var recorded = first.containers
                .SelectMany(c => c.materialSettings)
                .SelectMany(ms => ms.properties)
                .FirstOrDefault(p => p.name == "_Color");
            Assert.IsNotNull(recorded, "the first commit must record the nested renderer's lilToon _Color");

            coat.SetColor("_Color", Color.red);
            EditorUtility.SetDirty(coat);
            AssetDatabase.SaveAssets();

            BranchManager.Commit(avatarRoot, "coat turned red");

            var result = BranchManager.RestoreToCommit(avatarRoot, first.commitId);
            Assert.IsTrue(result.IsSuccess, $"checkout failed: {result.Kind}");

            Assert.AreEqual(Color.white, LiveCoatRenderer().sharedMaterials[0].GetColor("_Color"),
                "checking out the first commit must put the nested renderer back to white");
        }

        // KAN-89 skips writing a property the duplicate already holds, so a
        // repeat checkout doesn't dirty the asset and make the (now batched)
        // flush real work. The recorded values must still win over anything
        // that edited the duplicate in the meantime -- a checkout is a
        // regenerate, not a one-time stamp.
        [Test]
        public void ReCheckout_StillOverwritesAHandEditedDuplicate()
        {
            var root = ContainerManager.EnsureRootWithDefaults(avatarRoot);
            PrefabUtility.InstantiatePrefab(outfitPrefab, root.transform);

            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var first = BranchManager.Commit(avatarRoot, "initial, coat is white");
            Assert.IsTrue(BranchManager.RestoreToCommit(avatarRoot, first.commitId).IsSuccess);

            // Someone edits the generated duplicate directly.
            var duplicate = LiveCoatRenderer().sharedMaterials[0];
            Assert.IsTrue(duplicate.name.Contains("_avatarvcs"), "sanity check: the slot holds the generated duplicate");
            duplicate.SetColor("_Color", Color.magenta);
            EditorUtility.SetDirty(duplicate);
            AssetDatabase.SaveAssets();

            Assert.IsTrue(BranchManager.RestoreToCommit(avatarRoot, first.commitId).IsSuccess);

            Assert.AreEqual(Color.white, LiveCoatRenderer().sharedMaterials[0].GetColor("_Color"),
                "the recorded value must win over a hand-edited duplicate");
        }

        [Test]
        public void SaveBatchScopes_Nest()
        {
            Assert.DoesNotThrow(() =>
            {
                using var outer = MaterialSettingsApplier.BeginSaveBatch();
                using var inner = MaterialSettingsApplier.BeginSaveBatch();
            });
        }

        // The bug the user actually hit: their materials were on
        // "Hidden/lilToonOutline" and "Hidden/lilToonTransparent", not the
        // bare "lilToon" the allowlist matched. Every slot was skipped
        // silently, so materialSettings was empty in every commit and
        // checkout had nothing to restore -- the colour simply stayed as-is.
        [Test]
        public void LilToonVariantShader_IsCapturedAndRestoredLikeThePlainOne()
        {
            if (lilToonOutline == null) Assert.Ignore("No shader named 'Hidden/lilToonOutline' in this project.");

            var hair = new GameObject("Hair");
            hair.transform.SetParent(avatarRoot.transform, false);
            hair.AddComponent<MeshFilter>().sharedMesh = mesh;
            hair.AddComponent<MeshRenderer>().sharedMaterials = new[] { outlinedCoat };

            ContainerManager.EnsureRootWithDefaults(avatarRoot);
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);

            var first = BranchManager.Commit(avatarRoot, "initial, hair is white");

            var recorded = first.materialSettings.FirstOrDefault();
            Assert.IsNotNull(recorded,
                "a lilToon variant must be recorded; recording nothing is what made the whole feature look broken");
            Assert.AreEqual("Hidden/lilToonOutline", recorded.shader);
            Assert.AreEqual("1,1,1,1", recorded.properties.Single(p => p.name == "_Color").value);

            outlinedCoat.SetColor("_Color", Color.red);
            EditorUtility.SetDirty(outlinedCoat);
            AssetDatabase.SaveAssets();

            BranchManager.Commit(avatarRoot, "hair turned red");

            var result = BranchManager.RestoreToCommit(avatarRoot, first.commitId);
            Assert.IsTrue(result.IsSuccess, $"checkout failed: {result.Kind}");

            var live = avatarRoot.transform.Find("Hair").GetComponent<Renderer>().sharedMaterials[0];
            Assert.AreEqual(Color.white, live.GetColor("_Color"),
                "checking out the first commit must put the hair back to white");
        }
    }
}
