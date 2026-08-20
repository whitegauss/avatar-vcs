using System.Linq;
using AvatarVcs.Editor.MaterialSettings;
using AvatarVcs.Editor.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// Covers phase 2 task 8 from DesignDoc_avatar-vcs.md section 7.2/1.4.3:
    /// duplicate-then-apply leaves the source material byte-for-byte
    /// untouched, and the renderer ends up pointed at the duplicate.
    ///
    /// lilToon itself isn't available in a bare Unity project, so these tests
    /// use the built-in Standard shader as a stand-in: the state's "shader"
    /// field is set to "lilToon" (decoupled from the material's real shader,
    /// exactly as MaterialSettingsApplier reads it), and only "_Color" is
    /// exercised since it's a property Standard actually declares.
    /// </summary>
    public class MaterialSettingsTests
    {
        private const string TestAssetDir = "Assets/AvatarVcsTests_MatSettings_Temp";
        private GameObject avatarRoot;
        private Material sourceMaterial;
        private string sourceMaterialPath;
        private string sourceMaterialGuid;
        private Color originalColor;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TestAssetDir))
                AssetDatabase.CreateFolder("Assets", "AvatarVcsTests_MatSettings_Temp");

            sourceMaterial = new Material(Shader.Find("Standard"));
            originalColor = new Color(1f, 0f, 0f, 1f);
            sourceMaterial.SetColor("_Color", originalColor);
            sourceMaterialPath = $"{TestAssetDir}/Source.mat";
            AssetDatabase.CreateAsset(sourceMaterial, sourceMaterialPath);
            sourceMaterialGuid = AssetDatabase.AssetPathToGUID(sourceMaterialPath);

            avatarRoot = new GameObject("Avatar");
            var body = new GameObject("Body");
            body.transform.SetParent(avatarRoot.transform);
            var renderer = body.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { sourceMaterial };
        }

        [TearDown]
        public void TearDown()
        {
            if (avatarRoot != null) Object.DestroyImmediate(avatarRoot);
            if (AssetDatabase.IsValidFolder(TestAssetDir))
                AssetDatabase.DeleteAsset(TestAssetDir);
        }

        [Test]
        public void Apply_DuplicatesMaterial_LeavesSourceUnchanged_AndPointsRendererAtDuplicate()
        {
            var state = new MaterialSettingsState
            {
                targetPath = "Body",
                slot = 0,
                sourceMaterialGuid = sourceMaterialGuid,
                shader = "lilToon",
            };
            var newColor = new Color(0f, 1f, 0f, 1f);
            state.properties.Add(new MaterialPropertyValue
            {
                name = "_Color",
                type = "color",
                value = $"{newColor.r},{newColor.g},{newColor.b},{newColor.a}",
            });

            var duplicate = MaterialSettingsApplier.Apply(state, avatarRoot);

            Assert.IsNotNull(duplicate);
            Assert.AreNotSame(sourceMaterial, duplicate);
            Assert.Less(Vector4.Distance(originalColor, sourceMaterial.GetColor("_Color")), 0.001f);
            Assert.Less(Vector4.Distance(newColor, duplicate.GetColor("_Color")), 0.001f);

            var renderer = avatarRoot.transform.Find("Body").GetComponent<MeshRenderer>();
            Assert.AreSame(duplicate, renderer.sharedMaterials[0]);

            var duplicatePath = AssetDatabase.GetAssetPath(duplicate);
            Assert.IsFalse(string.IsNullOrEmpty(duplicatePath), "duplicate must be saved as an asset");
            var duplicateDir = System.IO.Path.GetDirectoryName(duplicatePath)?.Replace('\\', '/');
            var sourceDir = System.IO.Path.GetDirectoryName(sourceMaterialPath)?.Replace('\\', '/');
            Assert.AreEqual(sourceDir, duplicateDir);
        }

        [Test]
        public void Capture_ReadsCurrentPropertyValues_ForSupportedShader_AndSkipsUndeclaredProperties()
        {
            var state = MaterialSettingsCapture.Capture(sourceMaterial, "lilToon", "Body", 0);

            Assert.AreEqual("lilToon", state.shader);
            Assert.AreEqual(sourceMaterialGuid, state.sourceMaterialGuid);

            var colorEntry = state.properties.First(p => p.name == "_Color");
            var parsed = colorEntry.value.Split(',').Select(float.Parse).ToArray();
            Assert.Less(Vector4.Distance(originalColor, new Vector4(parsed[0], parsed[1], parsed[2], parsed[3])), 0.001f);

            // Standard doesn't declare _OutlineWidth; HasProperty guard must skip it.
            Assert.IsFalse(state.properties.Any(p => p.name == "_OutlineWidth"));
        }

        [Test]
        public void Apply_UnsupportedShader_Throws()
        {
            var state = new MaterialSettingsState
            {
                targetPath = "Body",
                slot = 0,
                sourceMaterialGuid = sourceMaterialGuid,
                shader = "Standard", // not in ShaderPropertyMap (MVP: lilToon only)
            };

            Assert.Throws<System.NotSupportedException>(() => MaterialSettingsApplier.Apply(state, avatarRoot));
        }
    }
}
