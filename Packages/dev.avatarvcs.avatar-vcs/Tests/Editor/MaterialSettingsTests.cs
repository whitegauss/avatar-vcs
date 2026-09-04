using System.Linq;
using AvatarVcs.Editor.MaterialSettings;
using AvatarVcs.Core.MaterialSettings;
using AvatarVcs.Core.Model;
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
    ///
    /// Asset creation/deletion is done once per fixture (OneTimeSetUp/
    /// OneTimeTearDown) rather than per test: repeatedly creating and deleting
    /// an asset at the same path across many tests in quick succession trips
    /// Unity's "infinite import loop" detector.
    /// </summary>
    public class MaterialSettingsTests
    {
        private const string TestAssetDir = "Assets/AvatarVcsTests_MatSettings_Temp";
        private Material sourceMaterial;
        private string sourceMaterialPath;
        private string sourceMaterialGuid;
        private Color originalColor;
        private GameObject avatarRoot;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (!AssetDatabase.IsValidFolder(TestAssetDir))
                AssetDatabase.CreateFolder("Assets", "AvatarVcsTests_MatSettings_Temp");

            sourceMaterial = new Material(Shader.Find("Standard"));
            originalColor = new Color(1f, 0f, 0f, 1f);
            sourceMaterial.SetColor("_Color", originalColor);
            sourceMaterialPath = $"{TestAssetDir}/Source.mat";
            AssetDatabase.CreateAsset(sourceMaterial, sourceMaterialPath);
            sourceMaterialGuid = AssetDatabase.AssetPathToGUID(sourceMaterialPath);
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
            if (avatarRoot != null) Object.DestroyImmediate(avatarRoot);
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
            // Compare by asset identity, not reference equality: Renderer.
            // sharedMaterials allocates a fresh array (and sometimes a
            // fresh wrapper) on every read, which isn't always AreSame to a
            // previously-held reference even when nothing was reassigned.
            Assert.AreEqual(AssetDatabase.GetAssetPath(duplicate), AssetDatabase.GetAssetPath(renderer.sharedMaterials[0]));

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

            // Standard doesn't declare _OutlineWidth (a lilToon property), so
            // ShaderPropertyMap.GetProperties -- which reads properties off
            // the material's actual shader -- never produces it here.
            Assert.IsFalse(state.properties.Any(p => p.name == "_OutlineWidth"));
        }

        [Test]
        public void Apply_CalledTwiceWithSameState_ReusesGeneratedDuplicate()
        {
            var state = new MaterialSettingsState
            {
                targetPath = "Body",
                slot = 0,
                sourceMaterialGuid = sourceMaterialGuid,
                shader = "lilToon",
            };
            state.properties.Add(new MaterialPropertyValue { name = "_Color", type = "color", value = "0,1,0,1" });

            var first = MaterialSettingsApplier.Apply(state, avatarRoot);
            Assert.IsFalse(string.IsNullOrEmpty(state.generatedGuid));
            var firstPath = AssetDatabase.GetAssetPath(first);

            var second = MaterialSettingsApplier.Apply(state, avatarRoot);

            Assert.AreSame(first, second, "second Apply should reuse the same duplicate, not create a new one");
            Assert.AreEqual(firstPath, AssetDatabase.GetAssetPath(second));
        }

        [Test]
        public void Apply_OnReuse_ReappliesPropertiesInsteadOfTrustingWhateverIsThere()
        {
            // A checkout is a regenerate, not a one-time stamp: if the
            // duplicate drifted (hand-edited, or a stale value from before
            // the commit was amended) between two Applies of the same
            // state, the recorded properties must win again, the same way
            // containers always destroy and rebuild rather than trusting
            // whatever object happens to already be there.
            var state = new MaterialSettingsState
            {
                targetPath = "Body",
                slot = 0,
                sourceMaterialGuid = sourceMaterialGuid,
                shader = "lilToon",
            };
            state.properties.Add(new MaterialPropertyValue { name = "_Color", type = "color", value = "0,1,0,1" });

            var first = MaterialSettingsApplier.Apply(state, avatarRoot);
            first.SetColor("_Color", new Color(1f, 1f, 1f, 1f)); // simulate drift on the duplicate itself

            var second = MaterialSettingsApplier.Apply(state, avatarRoot);

            Assert.AreSame(first, second);
            Assert.Less(Vector4.Distance(new Color(0f, 1f, 0f, 1f), second.GetColor("_Color")), 0.001f,
                "the recorded property must be reasserted, not left at the drifted value");
        }

        [Test]
        public void Apply_MalformedPropertyValue_SkipsThatPropertyInsteadOfThrowing()
        {
            // property.value ultimately comes from commit JSON on disk, which
            // can be malformed independent of tampering (crash mid-write,
            // bad merge). A parse failure on one property must not abort
            // the whole Apply -- especially since CheckoutOperation only
            // catches InvalidOperationException/NotSupportedException around
            // this call, after containers have already been destroyed.
            var state = new MaterialSettingsState
            {
                targetPath = "Body",
                slot = 0,
                sourceMaterialGuid = sourceMaterialGuid,
                shader = "lilToon",
            };
            state.properties.Add(new MaterialPropertyValue { name = "_Color", type = "color", value = "not,a,valid,color" });

            Material duplicate = null;
            Assert.DoesNotThrow(() => duplicate = MaterialSettingsApplier.Apply(state, avatarRoot));
            Assert.IsNotNull(duplicate, "the duplicate should still be created even though one property failed to parse");
        }

        [Test]
        public void Apply_SourceMaterialInPackagesFolder_FallsBackToAssetsGenerated()
        {
            // A source material inside a UPM package (Packages/...) is
            // immutable/read-only; writing a duplicate as a sibling asset
            // there (the normal "next to the source" behavior) would fail.
            const string packageMaterialPath = "Packages/dev.avatarvcs.avatar-vcs/Tests/Editor/_AvatarVcsTests_PackageSourceMaterial.mat";
            var packageMaterial = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(packageMaterial, packageMaterialPath);
            var packageMaterialGuid = AssetDatabase.AssetPathToGUID(packageMaterialPath);

            try
            {
                var state = new MaterialSettingsState
                {
                    targetPath = "Body",
                    slot = 0,
                    sourceMaterialGuid = packageMaterialGuid,
                    shader = "lilToon",
                };
                state.properties.Add(new MaterialPropertyValue { name = "_Color", type = "color", value = "0,1,0,1" });

                var duplicate = MaterialSettingsApplier.Apply(state, avatarRoot);

                Assert.IsNotNull(duplicate);
                var duplicatePath = AssetDatabase.GetAssetPath(duplicate);
                Assert.IsTrue(duplicatePath.StartsWith("Assets/AvatarVCS_Generated/"),
                    $"duplicate of a Packages/-sourced material must fall back under Assets/, got '{duplicatePath}'");
            }
            finally
            {
                AssetDatabase.DeleteAsset(packageMaterialPath);
                if (AssetDatabase.IsValidFolder("Assets/AvatarVCS_Generated"))
                    AssetDatabase.DeleteAsset("Assets/AvatarVCS_Generated");
            }
        }

        [Test]
        public void Apply_UnsupportedShader_Throws()
        {
            var state = new MaterialSettingsState
            {
                targetPath = "Body",
                slot = 0,
                sourceMaterialGuid = sourceMaterialGuid,
                shader = "Standard", // not in ShaderPropertyMap
            };

            Assert.Throws<System.NotSupportedException>(() => MaterialSettingsApplier.Apply(state, avatarRoot));
        }

        [TestCase("lilToon")]
        [TestCase(".poiyomi/Poiyomi")]
        [TestCase("VRM/MToon")]
        [TestCase("VRM10/MToon10")]
        public void ShaderPropertyMap_SupportsCommonAvatarShaders(string shaderName)
        {
            // None of these are available in a bare Unity project (see the
            // fixture-level comment above), so this only exercises the
            // name-based allowlist; GetProperties itself (dynamic shader
            // introspection) is exercised against a real shader below.
            Assert.IsTrue(ShaderPropertyMap.IsSupported(shaderName));
        }

        [Test]
        public void ShaderPropertyMap_UnknownShader_IsNotSupported()
        {
            Assert.IsFalse(ShaderPropertyMap.IsSupported("Standard"));
        }

        [Test]
        public void ShaderPropertyMap_GetProperties_NullShader_ReturnsEmpty()
        {
            Assert.IsEmpty(ShaderPropertyMap.GetProperties(null));
        }

        [Test]
        public void ShaderPropertyMap_GetProperties_EnumeratesColorFloatAndTextureFromTheShaderItself()
        {
            // Standard stands in for a real supported shader here (same
            // constraint as lilToon not being available), since GetProperties
            // needs to introspect an actual Shader object -- it declares
            // _Color (Color) and _Glossiness/_Metallic (Range, i.e. "float"),
            // which is enough to prove properties come from the shader's own
            // declared surface rather than a hand-curated list that could
            // miss one (see issue #44).
            var shader = Shader.Find("Standard");
            var properties = ShaderPropertyMap.GetProperties(shader);

            Assert.IsTrue(properties.Any(p => p.name == "_Color" && p.type == "color"));
            Assert.IsTrue(properties.Any(p => p.name == "_Glossiness" && p.type == "float"));

            // Textures used to be excluded here as "asset references, not
            // values". That left the generated duplicate carrying whatever
            // texture the source material held at checkout time, so a swapped
            // lilToon second-layer texture could never be restored (KAN-91).
            // They are recoverable by GUID, same as a material slot.
            Assert.IsTrue(properties.Any(p => p.name == "_MainTex" && p.type == "texture"));

            // Still excluded: types MaterialPropertyValue has no encoding for.
            // Standard declares _EmissionColor as Color and no bare Vector or
            // Int property, so assert the shape instead -- every enumerated
            // type must be one apply() can actually write back.
            CollectionAssert.IsSubsetOf(
                properties.Select(p => p.type).Distinct().ToList(),
                new[] { "color", "float", "texture" });
        }
    }
}
