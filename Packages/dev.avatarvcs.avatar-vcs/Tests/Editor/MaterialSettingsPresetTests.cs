using System.Linq;
using AvatarVcs.Core.Model;
using AvatarVcs.Core.Presets;
using AvatarVcs.Editor.MaterialSettings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// Standalone material settings presets: the "tell a friend your
    /// settings" path. What matters here is not that a value round-trips
    /// through one material, but that it lands on a DIFFERENT material --
    /// that is the whole reason the format drops targetPath, slot and
    /// sourceMaterialGuid.
    ///
    /// Needs a shader in the supported set; TestProject supplies stand-ins.
    /// </summary>
    public class MaterialSettingsPresetTests
    {
        private const string Dir = "Assets/AvatarVcsTests_Preset_Temp";

        private Shader lilToon;
        private Material source;
        private Material destination;
        private Texture2D texture;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            lilToon = Shader.Find("lilToon");
            if (lilToon == null) return;

            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "AvatarVcsTests_Preset_Temp");

            texture = new Texture2D(2, 2) { name = "PresetTex" };
            AssetDatabase.CreateAsset(texture, $"{Dir}/PresetTex.asset");
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

            source = new Material(lilToon);
            destination = new Material(lilToon);
            AssetDatabase.CreateAsset(source, $"{Dir}/Source.mat");
            AssetDatabase.CreateAsset(destination, $"{Dir}/Destination.mat");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset($"{Dir}/Source.mat");
            AssetDatabase.DeleteAsset($"{Dir}/Destination.mat");
        }

        // The point of the format. A commit records where a value lives; a
        // preset deliberately doesn't, so it can be applied somewhere else.
        [Test]
        public void APresetAppliesToADifferentMaterial()
        {
            source.SetColor("_Color", Color.red);
            source.SetFloat("_Cutoff", 0.25f);

            var preset = MaterialSettingsPresetIO.Capture(source);
            var skipped = MaterialSettingsPresetIO.Apply(preset, destination);

            CollectionAssert.IsEmpty(skipped);
            Assert.AreEqual(Color.red, destination.GetColor("_Color"));
            Assert.AreEqual(0.25f, destination.GetFloat("_Cutoff"), 0.0001f);
        }

        // 491 properties of mostly-defaults would hide the two that were
        // actually set, and this file is meant to be read by whoever receives
        // it. Anything omitted is already what their material would do.
        [Test]
        public void OnlyPropertiesThatDifferFromTheShaderDefaultsAreExported()
        {
            source.SetColor("_Color", Color.red);

            var preset = MaterialSettingsPresetIO.Capture(source);

            Assert.IsTrue(preset.properties.Any(p => p.name == "_Color"));
            Assert.Less(preset.properties.Count, 5,
                "an untouched lilToon material must not export its entire declared property set");
            Assert.IsFalse(preset.properties.Any(p => p.name == "_Metallic"),
                "_Metallic was never set, so it is at the shader default and carries no information");
        }

        [Test]
        public void AnUntouchedMaterialExportsNothing()
        {
            var preset = MaterialSettingsPresetIO.Capture(source);

            CollectionAssert.IsEmpty(preset.properties,
                "nothing was changed, so there is nothing to tell anyone about");
        }

        [Test]
        public void TheSourceMaterialIsNotIdentifiedInTheFile()
        {
            source.SetColor("_Color", Color.red);

            var json = MaterialSettingsPresetJson.Serialize(MaterialSettingsPresetIO.Capture(source));

            var sourceGuid = AssetDatabase.AssetPathToGUID($"{Dir}/Source.mat");
            StringAssert.DoesNotContain(sourceGuid, json,
                "a preset records values, not which asset they were taken from");
            StringAssert.DoesNotContain("targetPath", json,
                "and not where in some avatar's hierarchy it lived");
        }

        [Test]
        public void TexturesTravelAsGuids_AndResolveOnImport()
        {
            source.SetTexture("_Main2ndTex", texture);

            var preset = MaterialSettingsPresetIO.Capture(source);
            var entry = preset.properties.Single(p => p.name == "_Main2ndTex");
            Assert.AreEqual("texture", entry.type);
            Assert.AreEqual(AssetDatabase.AssetPathToGUID($"{Dir}/PresetTex.asset"), entry.value);

            MaterialSettingsPresetIO.Apply(preset, destination);

            Assert.AreEqual($"{Dir}/PresetTex.asset",
                AssetDatabase.GetAssetPath(destination.GetTexture("_Main2ndTex")));
        }

        // The normal case for a shared preset: the recipient doesn't own the
        // texture. That has to be reported, not silently blank the slot --
        // and it is why a preset can't stand in for an asset you don't have.
        [Test]
        public void ATextureTheRecipientDoesNotHave_IsReportedAndLeavesTheSlotAlone()
        {
            destination.SetTexture("_Main2ndTex", texture);

            var preset = new MaterialSettingsPreset
            {
                shader = lilToon.name,
                properties =
                {
                    new MaterialPropertyValue
                    {
                        name = "_Main2ndTex", type = "texture", value = "ffffffffffffffffffffffffffffffff",
                    },
                },
            };

            var skipped = MaterialSettingsPresetIO.Apply(preset, destination);

            CollectionAssert.Contains(skipped, "_Main2ndTex");
            Assert.AreEqual($"{Dir}/PresetTex.asset",
                AssetDatabase.GetAssetPath(destination.GetTexture("_Main2ndTex")),
                "an unresolvable texture must not clear what is already there");
        }

        [Test]
        public void APropertyTheTargetShaderDoesNotHave_IsSkippedNotFatal()
        {
            var preset = new MaterialSettingsPreset
            {
                shader = "SomeOtherShader",
                properties =
                {
                    new MaterialPropertyValue { name = "_Color", type = "color", value = "1,0,0,1" },
                    new MaterialPropertyValue { name = "_NotOnThisShader", type = "float", value = "1" },
                },
            };

            var skipped = MaterialSettingsPresetIO.Apply(preset, destination);

            CollectionAssert.AreEquivalent(new[] { "_NotOnThisShader" }, skipped);
            Assert.AreEqual(Color.red, destination.GetColor("_Color"),
                "the properties that do match are still applied");
        }

        [Test]
        public void AShaderMismatch_IsCalledOutInTheImportMessage()
        {
            var message = MaterialSettingsPresetJson.DescribeImport(
                3, "/tmp/x.json", "Hidden/lilToonOutline", "Standard", new[] { "_Foo" });

            StringAssert.Contains("Hidden/lilToonOutline", message);
            StringAssert.Contains("Standard", message);
            StringAssert.Contains("_Foo", message);
        }
    }
}
