using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Core.Model;
using AvatarVcs.Core.Naming;
using AvatarVcs.Editor.MaterialSettings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// The duplicate-material explosion found on a real avatar: 552 generated
    /// materials, 82% of every material in that project, names six levels of
    /// "_avatarvcs" deep.
    ///
    /// Three faults produced it. Volume: a copy was made for every material
    /// slot whose shader was supported, changed or not, on every checkout.
    /// Multiplicity: each commit got a copy of its own, so they accumulated as
    /// "_avatarvcs 1" ... "_avatarvcs 17". Chaining: capture recorded whichever
    /// material the renderer was pointing at, which after a checkout is the
    /// copy, so the next checkout copied the copy.
    ///
    /// Settings are now written onto the material itself. A copy is made only
    /// where this checkout has no business writing -- a material something
    /// outside the avatar is also wearing -- and then there is exactly one of
    /// it, for good.
    /// </summary>
    public class GeneratedMaterialSprawlTests
    {
        private const string Dir = "Assets/AvatarVcsTests_MatSprawl_Temp";

        private Material source;
        private string sourcePath;
        private string sourceGuid;
        private GameObject avatarRoot;
        private MeshRenderer bodyRenderer;
        private GameObject outsider;

        private static readonly Color SourceColor = new Color(1f, 0f, 0f, 1f);

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "AvatarVcsTests_MatSprawl_Temp");

            source = new Material(Shader.Find("Standard"));
            sourcePath = $"{Dir}/Source.mat";
            AssetDatabase.CreateAsset(source, sourcePath);
            sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (AssetDatabase.IsValidFolder(Dir)) AssetDatabase.DeleteAsset(Dir);
        }

        [SetUp]
        public void SetUp()
        {
            source.SetColor("_Color", SourceColor);
            avatarRoot = new GameObject("Avatar");
            var body = new GameObject("Body");
            body.transform.SetParent(avatarRoot.transform);
            bodyRenderer = body.AddComponent<MeshRenderer>();
            bodyRenderer.sharedMaterials = new[] { source };
        }

        [TearDown]
        public void TearDown()
        {
            if (avatarRoot != null) Object.DestroyImmediate(avatarRoot);
            if (outsider != null) Object.DestroyImmediate(outsider);
            outsider = null;
            foreach (var path in GeneratedIn(Dir)) AssetDatabase.DeleteAsset(path);
        }

        // The headline case: on the avatar this came from, the user had edited
        // three materials and every checkout wrote a copy of all 46.
        [Test]
        public void Apply_RecordedValuesAreWhatTheMaterialAlreadyHolds_TouchesNothing()
        {
            var state = State(SourceColor);

            var applied = MaterialSettingsApplier.Apply(state, avatarRoot);

            Assert.AreSame(source, applied);
            Assert.IsTrue(string.IsNullOrEmpty(state.generatedGuid));
            CollectionAssert.IsEmpty(GeneratedIn(Dir));
        }

        [Test]
        public void Apply_RecordedValuesDiffer_WritesThemOntoTheMaterialWithoutCopying()
        {
            var state = State(Color.green);

            var applied = MaterialSettingsApplier.Apply(state, avatarRoot);

            Assert.AreSame(source, applied);
            Assert.AreEqual(Color.green, source.GetColor("_Color"));
            CollectionAssert.IsEmpty(GeneratedIn(Dir));
        }

        // Where a copy is still right, there is one of it -- not one per
        // commit. Two commits recording different values for the same shared
        // material reuse the same file, because only one commit's state is
        // ever live in the scene.
        [Test]
        public void Apply_SharedMaterial_KeepsExactlyOneCopyAcrossCommits()
        {
            GiveTheMaterialToSomeoneElse();
            var green = State(Color.green);
            var blue = State(Color.blue);

            LogAssert.ignoreFailingMessages = true;
            var first = MaterialSettingsApplier.Apply(green, avatarRoot);
            var second = MaterialSettingsApplier.Apply(blue, avatarRoot);
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(1, GeneratedIn(Dir).Count, "one copy per material, for good");
            Assert.AreEqual(green.generatedGuid, blue.generatedGuid);
            Assert.AreSame(first, second);
            Assert.AreEqual(Color.blue, second.GetColor("_Color"), "the copy holds whatever is checked out now");
            Assert.AreEqual(SourceColor, source.GetColor("_Color"), "and the shared material is never written to");
        }

        // The chaining fault: after a checkout the renderer wears the copy, so
        // this is what the next commit sees.
        [Test]
        public void Capture_WhenTheSlotWearsACopy_RecordsTheOriginalAsTheSource()
        {
            GiveTheMaterialToSomeoneElse();
            LogAssert.ignoreFailingMessages = true;
            var copy = MaterialSettingsApplier.Apply(State(Color.green), avatarRoot);
            LogAssert.ignoreFailingMessages = false;
            Assert.AreSame(copy, bodyRenderer.sharedMaterials[0], "precondition: the slot wears the copy");

            var recaptured = MaterialSettingsCapture.Capture(
                copy, "lilToon", "Body", 0, GeneratedMaterialSource.Resolve(copy));

            Assert.AreEqual(sourceGuid, recaptured.sourceMaterialGuid);
            Assert.AreEqual(Color.green.ToString(), ParseColor(recaptured).ToString(),
                "the values still come off the material the renderer is wearing");
        }

        [Test]
        public void Resolve_TracesACopyBackToItsSource()
        {
            GiveTheMaterialToSomeoneElse();
            LogAssert.ignoreFailingMessages = true;
            var copy = MaterialSettingsApplier.Apply(State(Color.green), avatarRoot);
            LogAssert.ignoreFailingMessages = false;

            Assert.AreSame(source, GeneratedMaterialSource.Resolve(copy));
            Assert.AreSame(source, GeneratedMaterialSource.Resolve(source), "a material of the user's own is its own source");
        }

        [Test]
        public void Sweeper_CollectsACopyNothingPointsAt()
        {
            var orphan = new Material(Shader.Find("Standard"));
            var orphanPath = $"{Dir}/Orphan_avatarvcs.mat";
            AssetDatabase.CreateAsset(orphan, orphanPath);

            var plan = GeneratedMaterialSweeper.Plan();
            if (plan.Blocked) Assert.Ignore("Another fixture left an unreadable commit; the sweeper refuses to guess.");

            CollectionAssert.Contains(plan.deletable, orphanPath);
        }

        [Test]
        public void Sweeper_KeepsACopyAnOpenSceneIsWearing()
        {
            GiveTheMaterialToSomeoneElse();
            LogAssert.ignoreFailingMessages = true;
            var copy = MaterialSettingsApplier.Apply(State(Color.green), avatarRoot);
            LogAssert.ignoreFailingMessages = false;
            var copyPath = AssetDatabase.GetAssetPath(copy);

            var plan = GeneratedMaterialSweeper.Plan();
            if (plan.Blocked) Assert.Ignore("Another fixture left an unreadable commit; the sweeper refuses to guess.");

            CollectionAssert.DoesNotContain(plan.deletable, copyPath);
            Assert.GreaterOrEqual(plan.keptInUse, 1);
        }

        // A user's own file that merely resembles our output is not a
        // candidate at all -- same rule the commit-deletion guard uses.
        [Test]
        public void Sweeper_IgnoresAUserFileThatOnlyResemblesOurOutput()
        {
            var lookalike = new Material(Shader.Find("Standard"));
            var lookalikePath = $"{Dir}/Coat_avatarvcs_backup.mat";
            AssetDatabase.CreateAsset(lookalike, lookalikePath);

            try
            {
                var plan = GeneratedMaterialSweeper.Plan();
                if (plan.Blocked) Assert.Ignore("Another fixture left an unreadable commit; the sweeper refuses to guess.");

                CollectionAssert.DoesNotContain(plan.deletable, lookalikePath);
            }
            finally
            {
                AssetDatabase.DeleteAsset(lookalikePath);
            }
        }

        /// <summary>
        /// Puts the material on a renderer outside the avatar, which is the
        /// one situation a checkout still copies rather than writes.
        /// </summary>
        private void GiveTheMaterialToSomeoneElse()
        {
            outsider = new GameObject("SomeoneElse");
            outsider.AddComponent<MeshRenderer>().sharedMaterials = new[] { source };
        }

        private static Color ParseColor(MaterialSettingsState state)
        {
            var parts = state.properties.First(p => p.name == "_Color").value
                .Split(',')
                .Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            return new Color(parts[0], parts[1], parts[2], parts[3]);
        }

        private MaterialSettingsState State(Color color)
        {
            var state = new MaterialSettingsState
            {
                targetPath = "Body",
                slot = 0,
                sourceMaterialGuid = sourceGuid,
                shader = "lilToon",
            };
            state.properties.Add(new MaterialPropertyValue
            {
                name = "_Color",
                type = "color",
                value = $"{color.r},{color.g},{color.b},{color.a}",
            });
            return state;
        }

        private static List<string> GeneratedIn(string directory) =>
            AssetDatabase.FindAssets("t:Material", new[] { directory })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(GeneratedAssetNaming.LooksGenerated)
                .ToList();
    }
}
