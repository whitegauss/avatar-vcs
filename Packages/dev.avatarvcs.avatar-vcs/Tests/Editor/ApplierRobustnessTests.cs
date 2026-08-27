using System;
using System.Collections.Generic;
using AvatarVcs.Editor.Apply;
using AvatarVcs.Editor.AvatarReferences;
using AvatarVcs.Editor.MaterialSettings;
using AvatarVcs.Core.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// Robustness tests for ComponentApplier, AvatarReferenceApplier, and
    /// MaterialSettingsApplier covering missing components, unresolved paths,
    /// out-of-range slots, and unresolvable GUIDs.
    /// </summary>
    public class ApplierRobustnessTests
    {
        private const string TestAssetDir = "Assets/AvatarVcsTests_ApplierRobustness_Temp";
        private readonly List<GameObject> spawned = new();
        private Material testMat;
        private string testMatGuid;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (!AssetDatabase.IsValidFolder(TestAssetDir))
                AssetDatabase.CreateFolder("Assets", "AvatarVcsTests_ApplierRobustness_Temp");

            testMat = new Material(Shader.Find("Standard"));
            var matPath = $"{TestAssetDir}/ValidMat.mat";
            AssetDatabase.CreateAsset(testMat, matPath);
            testMatGuid = AssetDatabase.AssetPathToGUID(matPath);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (AssetDatabase.IsValidFolder(TestAssetDir))
                AssetDatabase.DeleteAsset(TestAssetDir);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();
        }

        private GameObject Spawn(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent);
            spawned.Add(go);
            return go;
        }

        #region ComponentApplier Robustness

        [Test]
        public void ComponentApplier_UnresolvableType_ReturnsComponentTypeUnresolved()
        {
            var root = Spawn("Root");
            var state = new ComponentState
            {
                path = "",
                type = "NonExistent.FakeComponentType",
            };

            var result = ComponentApplier.Apply(state, root);
            Assert.AreEqual(ApplyResultKind.ComponentTypeUnresolved, result.Kind);
            Assert.IsFalse(result.IsSuccess);
        }

        [Test]
        public void ComponentApplier_UnresolvablePath_ReturnsPathUnresolved()
        {
            var root = Spawn("Root");
            var state = new ComponentState
            {
                path = "NonExistent/Child/Path",
                type = typeof(Light).FullName,
            };

            var result = ComponentApplier.Apply(state, root);
            Assert.AreEqual(ApplyResultKind.PathUnresolved, result.Kind);
            Assert.IsFalse(result.IsSuccess);
        }

        [Test]
        public void ComponentApplier_UnknownFieldsAndAssetRefs_AreSkippedWithoutCrashing()
        {
            var root = Spawn("Root");
            root.AddComponent<Light>();

            var state = new ComponentState
            {
                path = "",
                type = typeof(Light).FullName,
                fields =
                {
                    new FieldValue { key = "m_NonExistentFieldKey", type = "int", value = "999" },
                    new FieldValue { key = "m_Intensity", type = "float", value = "2.5" },
                },
                assetRefs =
                {
                    new AssetRef { key = "m_NonExistentAssetRefKey", guid = "invalid_guid" },
                },
            };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Unknown field .*m_NonExistentFieldKey"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Unknown asset reference .*m_NonExistentAssetRefKey"));

            var result = ComponentApplier.Apply(state, root);

            Assert.IsTrue(result.IsSuccess);
            var light = root.GetComponent<Light>();
            Assert.AreEqual(2.5f, light.intensity, 0.0001f);
        }

        [Test]
        public void ComponentApplier_UnresolvableSceneRefPath_IsSkippedWithWarning()
        {
            var root = Spawn("Root");
            var renderer = root.AddComponent<SkinnedMeshRenderer>();

            var state = new ComponentState
            {
                path = "",
                type = typeof(SkinnedMeshRenderer).FullName,
                sceneRefs =
                {
                    new SceneRef
                    {
                        key = "m_RootBone",
                        path = "NonExistent/Bone/Path",
                        type = typeof(Transform).FullName,
                    },
                },
            };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Scene reference path .* could not be resolved"));

            var result = ComponentApplier.Apply(state, root, root);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(renderer.rootBone);
        }

        #endregion

        #region AvatarReferenceApplier Robustness

        [Test]
        public void AvatarReferenceApplier_UnresolvablePath_LogsWarningAndDoesNotCrash()
        {
            var root = Spawn("Avatar");
            var state = new AvatarReferenceState
            {
                path = "Missing/Body",
                blendShapes = { new BlendShapeRef { name = "Shape", weight = 100f } },
            };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("avatarReferences path 'Missing/Body' could not be resolved"));
            Assert.DoesNotThrow(() => AvatarReferenceApplier.Apply(state, root.transform));
        }

        [Test]
        public void AvatarReferenceApplier_MissingBlendShapeName_LogsWarningAndSkips()
        {
            var root = Spawn("Avatar");
            var body = Spawn("Body", root.transform);
            var smr = body.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh { name = "EmptyMesh" };
            smr.sharedMesh = mesh;

            var state = new AvatarReferenceState
            {
                path = "Body",
                blendShapes = { new BlendShapeRef { name = "NonExistentShape", weight = 50f } },
            };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Blend shape 'NonExistentShape' not found on 'Body'"));
            Assert.DoesNotThrow(() => AvatarReferenceApplier.Apply(state, root.transform));

            UnityEngine.Object.DestroyImmediate(mesh);
        }

        [Test]
        public void AvatarReferenceApplier_OutOfRangeMaterialSlot_LogsWarningAndSkips()
        {
            var root = Spawn("Avatar");
            var body = Spawn("Body", root.transform);
            var mr = body.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { testMat }; // Only slot 0 exists

            var state = new AvatarReferenceState
            {
                path = "Body",
                materials =
                {
                    new MaterialRef { slot = -1, guid = testMatGuid },
                    new MaterialRef { slot = 99, guid = testMatGuid },
                },
            };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Material slot -1 out of range"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Material slot 99 out of range"));

            Assert.DoesNotThrow(() => AvatarReferenceApplier.Apply(state, root.transform));
        }

        [Test]
        public void AvatarReferenceApplier_UnresolvableMaterialGuid_LogsWarningAndSkips()
        {
            var root = Spawn("Avatar");
            var body = Spawn("Body", root.transform);
            var mr = body.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { testMat };

            var state = new AvatarReferenceState
            {
                path = "Body",
                materials = { new MaterialRef { slot = 0, guid = "invalid_guid_0000000000000000" } },
            };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Material GUID .* could not be resolved for slot 0"));

            Assert.DoesNotThrow(() => AvatarReferenceApplier.Apply(state, root.transform));
            // Renderer.sharedMaterials returns a freshly allocated array each
            // call, whose element wrappers aren't always reference-equal to
            // a previously-held one even when nothing was reassigned; compare
            // by asset identity instead (confirmed via an earlier, similar
            // AreSame flake with a real Unity object re-read on this project).
            Assert.AreEqual(AssetDatabase.GetAssetPath(testMat), AssetDatabase.GetAssetPath(mr.sharedMaterials[0]),
                "Original material should remain untouched");
        }

        #endregion

        #region MaterialSettingsApplier Robustness

        [Test]
        public void MaterialSettingsApplier_UnresolvableTargetPath_ThrowsInvalidOperationException()
        {
            var root = Spawn("Avatar");
            var state = new MaterialSettingsState
            {
                targetPath = "NonExistent/Body",
                slot = 0,
                sourceMaterialGuid = testMatGuid,
                shader = "lilToon",
            };

            Assert.Throws<InvalidOperationException>(() => MaterialSettingsApplier.Apply(state, root));
        }

        [Test]
        public void MaterialSettingsApplier_TargetHasNoRenderer_ThrowsInvalidOperationException()
        {
            var root = Spawn("Avatar");
            var body = Spawn("Body", root.transform); // No Renderer

            var state = new MaterialSettingsState
            {
                targetPath = "Body",
                slot = 0,
                sourceMaterialGuid = testMatGuid,
                shader = "lilToon",
            };

            Assert.Throws<InvalidOperationException>(() => MaterialSettingsApplier.Apply(state, root));
        }

        [Test]
        public void MaterialSettingsApplier_UnresolvableSourceGuid_ThrowsInvalidOperationException()
        {
            var root = Spawn("Avatar");
            var body = Spawn("Body", root.transform);
            body.AddComponent<MeshRenderer>();

            var state = new MaterialSettingsState
            {
                targetPath = "Body",
                slot = 0,
                sourceMaterialGuid = "invalid_guid_0000000000000000",
                shader = "lilToon",
            };

            Assert.Throws<InvalidOperationException>(() => MaterialSettingsApplier.Apply(state, root));
        }

        [Test]
        public void MaterialSettingsApplier_OutOfRangeSlot_ThrowsInvalidOperationException()
        {
            var root = Spawn("Avatar");
            var body = Spawn("Body", root.transform);
            var mr = body.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { testMat }; // Only slot 0 exists

            var state = new MaterialSettingsState
            {
                targetPath = "Body",
                slot = 5, // Out of range
                sourceMaterialGuid = testMatGuid,
                shader = "lilToon",
            };

            Assert.Throws<InvalidOperationException>(() => MaterialSettingsApplier.Apply(state, root));
        }

        [Test]
        public void ComponentApplier_MalformedFieldValue_LogsWarningAndDoesNotCrash()
        {
            var root = Spawn("Root");
            root.AddComponent<Light>();

            var state = new ComponentState
            {
                path = "",
                type = typeof(Light).FullName,
                fields =
                {
                    new FieldValue { key = "m_Intensity", type = "float", value = "not-a-valid-float" },
                },
            };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Could not decode field 'm_Intensity'"));
            var result = ComponentApplier.Apply(state, root);
            Assert.IsTrue(result.IsSuccess);
        }

        #endregion

        #region AvatarReferenceApplier Robustness Continued

        [Test]
        public void AvatarReferenceApplier_TargetHasNoRenderer_LogsWarningAndDoesNotCrash()
        {
            var root = Spawn("Avatar");
            Spawn("Body", root.transform); // No Renderer attached

            var state = new AvatarReferenceState
            {
                path = "Body",
                materials = { new MaterialRef { slot = 0, guid = testMatGuid } },
            };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("'Body' has no Renderer; material references skipped"));
            Assert.DoesNotThrow(() => AvatarReferenceApplier.Apply(state, root.transform));
        }

        [Test]
        public void AvatarReferenceApplier_TargetHasNoSkinnedMeshRenderer_LogsWarningAndDoesNotCrash()
        {
            var root = Spawn("Avatar");
            Spawn("Body", root.transform); // No SkinnedMeshRenderer

            var state = new AvatarReferenceState
            {
                path = "Body",
                blendShapes = { new BlendShapeRef { name = "Shape", weight = 100f } },
            };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("'Body' has no SkinnedMeshRenderer with a mesh; blend shapes skipped"));
            Assert.DoesNotThrow(() => AvatarReferenceApplier.Apply(state, root.transform));
        }

        [Test]
        public void AvatarReferenceApplier_ActiveState_UnresolvableDescendant_LogsWarningAndDoesNotCrash()
        {
            var root = Spawn("Avatar");
            Spawn("Body", root.transform);

            var state = new AvatarReferenceState
            {
                path = "Body",
                objectStates = { new ObjectStateRef { path = "NonExistentChild", activeSelf = false } },
            };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("avatarReferences objectState path 'NonExistentChild' under 'Body' could not be resolved"));
            Assert.DoesNotThrow(() => AvatarReferenceApplier.Apply(state, root.transform));
        }

        #endregion

        #region MaterialSettingsApplier Robustness Continued

        [Test]
        public void MaterialSettingsApplier_SourceAssetNotAMaterial_ThrowsInvalidOperationException()
        {
            // Point sourceMaterialGuid to a non-Material asset (e.g. this test file or a prefab)
            var scriptPath = "Packages/dev.avatarvcs.avatar-vcs/Editor/AvatarVcs.Editor.asmdef";
            var nonMaterialGuid = AssetDatabase.AssetPathToGUID(scriptPath);

            var root = Spawn("Avatar");
            var body = Spawn("Body", root.transform);
            var mr = body.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { testMat };

            var state = new MaterialSettingsState
            {
                targetPath = "Body",
                slot = 0,
                sourceMaterialGuid = nonMaterialGuid,
                shader = "lilToon",
            };

            Assert.Throws<InvalidOperationException>(() => MaterialSettingsApplier.Apply(state, root));
        }

        [Test]
        public void MaterialSettingsApplier_DuplicateHasNoProperty_LogsWarningAndSkips()
        {
            var root = Spawn("Avatar");
            var body = Spawn("Body", root.transform);
            var mr = body.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { testMat };

            var state = new MaterialSettingsState
            {
                targetPath = "Body",
                slot = 0,
                sourceMaterialGuid = testMatGuid,
                shader = "lilToon",
                properties =
                {
                    new MaterialPropertyValue { name = "_NonExistentProperty12345", type = "float", value = "1.0" },
                },
            };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Duplicate material has no property '_NonExistentProperty12345'"));
            var duplicate = MaterialSettingsApplier.Apply(state, root);
            Assert.IsNotNull(duplicate);
        }

        [Test]
        public void MaterialSettingsApplier_UnsupportedPropertyType_LogsWarningAndSkips()
        {
            var root = Spawn("Avatar");
            var body = Spawn("Body", root.transform);
            var mr = body.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { testMat };

            var state = new MaterialSettingsState
            {
                targetPath = "Body",
                slot = 0,
                sourceMaterialGuid = testMatGuid,
                shader = "lilToon",
                properties =
                {
                    new MaterialPropertyValue { name = "_Color", type = "matrix4x4", value = "1,0,0,0" },
                },
            };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Unsupported material property type 'matrix4x4'"));
            var duplicate = MaterialSettingsApplier.Apply(state, root);
            Assert.IsNotNull(duplicate);
        }

        #endregion
    }
}
