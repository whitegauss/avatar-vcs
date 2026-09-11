using System.IO;
using System.Linq;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using AvatarVcs.Editor.Repair;
using AvatarVcs.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// Repair against a genuinely broken scene, rather than against a parsed
    /// string: the markers sit on a prefab instance, which is what an avatar
    /// is, and the broken component is made the way the real one was -- by the
    /// script's guid changing under a saved scene.
    ///
    /// This is the gap the first version shipped through. The parsing and the
    /// classification had tests; putting the component back and taking the
    /// broken one away did not, and it turned out
    /// GameObjectUtility.RemoveMonoBehavioursWithMissingScript quietly does
    /// nothing on a prefab instance. The user's markers came back with the
    /// broken components still sitting next to them.
    /// </summary>
    public class MarkerRepairPrefabInstanceTests
    {
        private const string Dir = "Assets/AvatarVcsTests_Repair_Temp";
        private const string TrackedScriptPath =
            "Packages/dev.avatarvcs.avatar-vcs/Runtime/Components/AvatarVcsTrackedReference.cs";
        private const string BogusGuid = "ffffffffffffffffffffffffffffffff";

        // Distinct from the "Avatar" every other fixture spawns: the scene
        // copy this writes contains whatever else is in the runner's scene.
        private const string AvatarName = "RepairFixtureAvatar";

        private string scenePath;
        private string avatarGuid;
        private Scene scene;
        private GameObject live;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "AvatarVcsTests_Repair_Temp");

            scenePath = $"{Dir}/RepairScene.unity";
        }

        [TearDown]
        public void TearDown()
        {
            if (live != null) { Object.DestroyImmediate(live); live = null; }
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (avatarGuid != null) { CommitStore.DeleteAvatarHistory(avatarGuid); avatarGuid = null; }
            if (AssetDatabase.IsValidFolder(Dir)) AssetDatabase.DeleteAsset(Dir);
        }

        [Test]
        public void Repair_MarkerBrokenOnAPrefabInstance_ComesBackAndTheBrokenComponentGoes()
        {
            var trackedGuid = AssetDatabase.AssetPathToGUID(TrackedScriptPath);
            if (string.IsNullOrEmpty(trackedGuid))
                Assert.Ignore("AvatarVcsTrackedReference.cs has no guid in this project.");

            BuildAvatarSceneAndCommit();
            BreakTheScriptGuid(trackedGuid);

            var avatar = ReopenedAvatar();
            Assert.Greater(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(avatar), 0,
                "precondition: the marker is broken");
            Assert.IsNull(avatar.GetComponent<AvatarVcsTrackedReference>(), "precondition: and gone");

            var plan = Planned();
            MarkerRepair.Apply(plan);

            Assert.IsNotNull(avatar.GetComponent<AvatarVcsTrackedReference>(), "the marker must be back");
            Assert.AreEqual(0, GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(avatar),
                "and the broken component must be gone -- it is what every validator in the project complains about");
        }

        // The user ran Repair, the markers came back, and the broken ones
        // stayed. Running it again has to finish the job rather than report
        // that there is nothing to do.
        [Test]
        public void Repair_RunAgainAfterTheMarkerIsBack_StillClearsTheBrokenComponent()
        {
            var trackedGuid = AssetDatabase.AssetPathToGUID(TrackedScriptPath);
            if (string.IsNullOrEmpty(trackedGuid))
                Assert.Ignore("AvatarVcsTrackedReference.cs has no guid in this project.");

            BuildAvatarSceneAndCommit();
            BreakTheScriptGuid(trackedGuid);

            var avatar = ReopenedAvatar();
            // Put the marker back by hand, leaving the broken one behind:
            // exactly the state the first Repair left the reporter's scene in.
            Undo.AddComponent<AvatarVcsTrackedReference>(avatar);
            EditorSceneManager.SaveScene(scene);
            Assert.Greater(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(avatar), 0);

            var plan = Planned();
            Assert.IsEmpty(plan.actions, "nothing to add: the marker is already there");
            Assert.Greater(plan.BrokenComponents, 0, "but there is still something to clear");

            MarkerRepair.Apply(plan);

            Assert.AreEqual(0, GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(avatar));
        }

        /// <summary>
        /// Builds the avatar in whatever scene is already active and writes a
        /// *copy* of that scene to disk, rather than making a new one.
        /// EditorSceneManager.NewScene refuses to add a scene while an
        /// untitled one is unsaved, which is exactly how the test runner
        /// starts, and saving the runner's scene out from under itself is not
        /// this fixture's business.
        /// </summary>
        private void BuildAvatarSceneAndCommit()
        {
            // An avatar is a prefab instance, which is the whole point here.
            var source = new GameObject(AvatarName);
            var body = new GameObject("Body");
            body.transform.SetParent(source.transform, false);
            // Named after the instance, not the other way round: InstantiatePrefab
            // names the instance after the prefab asset, so a prefab called
            // "Avatar" would put an "Avatar" in the scene copy -- and there are
            // several of those from other fixtures.
            var prefab = PrefabUtility.SaveAsPrefabAsset(source, $"{Dir}/{AvatarName}.prefab");
            Object.DestroyImmediate(source);

            live = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            ContainerManager.EnsureRootWithDefaults(live);
            avatarGuid = ContainerManager.GetAvatarGuid(live);

            // A commit is what tells Repair that the field-less marker on this
            // object was a Track Properties Here (avatarReferences[].path).
            BranchManager.Commit(live, "init");

            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), scenePath, saveAsCopy: true);

            // The copy on disk is the fixture from here on; the live one would
            // only be a second avatar carrying the same avatarGuid.
            Object.DestroyImmediate(live);
            live = null;
        }

        /// <summary>
        /// Swaps the marker's script guid in the saved file for one nothing
        /// resolves, then reopens the scene -- the same thing a package update
        /// used to do by re-generating the .meta files.
        /// </summary>
        private void BreakTheScriptGuid(string trackedGuid)
        {
            var text = File.ReadAllText(scenePath);
            Assert.IsTrue(text.Contains(trackedGuid), "the saved scene should reference the marker script");
            File.WriteAllText(scenePath, text.Replace(trackedGuid, BogusGuid));
            AssetDatabase.Refresh();

            LogAssert.ignoreFailingMessages = true;
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            LogAssert.ignoreFailingMessages = false;

            // Repair reads the guids out of the file on disk, so it declines a
            // scene with unsaved changes. Opening one with broken scripts can
            // leave it dirty, so make the two agree before planning.
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// Plan, with the reasons it might have found nothing folded into the
        /// failure message -- a plan that skipped this scene and a plan that
        /// could not identify the marker look identical from the assertions.
        /// </summary>
        private MarkerRepairPlan Planned()
        {
            var plan = MarkerRepair.Plan();
            Assert.IsFalse(plan.blockers.Any(b => b.Contains(scene.name)),
                $"the fixture scene was skipped: {string.Join(" | ", plan.blockers)}");
            Assert.IsEmpty(plan.unresolved, "the marker should have been identified from the commit");
            return plan;
        }

        private GameObject ReopenedAvatar()
        {
            var roots = scene.GetRootGameObjects();
            var avatar = roots.FirstOrDefault(go => go.name == AvatarName);
            Assert.IsNotNull(avatar,
                $"'{AvatarName}' is not in the reopened scene. Roots: {string.Join(", ", roots.Select(r => r.name))}");
            return avatar;
        }
    }
}
