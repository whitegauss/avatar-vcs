using System.Linq;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
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
        private Mesh mesh;
        private Material coat;
        private GameObject outfitPrefab;
        private GameObject avatarRoot;
        private string avatarGuid;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            lilToon = Shader.Find("lilToon");
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

            var src = new GameObject("Outfit");
            var filter = src.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = src.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { coat };
            outfitPrefab = PrefabUtility.SaveAsPrefabAsset(src, $"{Dir}/Outfit.prefab");
            Object.DestroyImmediate(src);
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
    }
}
