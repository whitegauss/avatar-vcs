using System.Linq;
using AvatarVcs.Core.Model;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using AvatarVcs.Editor.Operations;
using AvatarVcs.Editor.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// KAN-81, two gaps that share a shape: something is verified up to the
    /// point where it would actually matter, and not past it.
    ///
    /// 1. The GUID remap is asserted through HasMissingPrefabs -- the
    ///    pre-flight check -- but nothing asserted the replacement prefab is
    ///    then really instantiated. A remap that satisfies the pre-flight and
    ///    fails the restore leaves an EMPTY container, which is the user's
    ///    accessories gone, and every existing test would still pass.
    ///
    /// 2. EditorHistoryStore / EditorAvatarGateway / EditorUserPrompt are the
    ///    real implementations behind the presenter's ports, and had zero test
    ///    references between them. The presenter suite runs entirely on fakes,
    ///    so a swapped argument or a method wired to the wrong call site is
    ///    invisible to it.
    /// </summary>
    public class EditorPortsAndRemapTests
    {
        private const string Dir = "Assets/AvatarVcsTests_Ports_Temp";

        private GameObject oldPrefab;
        private GameObject newPrefab;
        private string oldPrefabGuid;
        private string newPrefabGuid;
        private GameObject avatarRoot;
        private string avatarGuid;
        private GuidRemapConfig originalRemapConfig;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "AvatarVcsTests_Ports_Temp");

            var oldSource = new GameObject("OldOutfit");
            oldPrefab = PrefabUtility.SaveAsPrefabAsset(oldSource, $"{Dir}/OldOutfit.prefab");
            Object.DestroyImmediate(oldSource);
            oldPrefabGuid = AssetDatabase.AssetPathToGUID($"{Dir}/OldOutfit.prefab");

            var newSource = new GameObject("NewOutfit");
            newPrefab = PrefabUtility.SaveAsPrefabAsset(newSource, $"{Dir}/NewOutfit.prefab");
            Object.DestroyImmediate(newSource);
            newPrefabGuid = AssetDatabase.AssetPathToGUID($"{Dir}/NewOutfit.prefab");
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (AssetDatabase.IsValidFolder(Dir)) AssetDatabase.DeleteAsset(Dir);
        }

        [SetUp]
        public void SetUp()
        {
            // The remap config is real project state, not per-test state.
            // Same save/restore the existing remap tests use, so a mapping
            // added here can't leak into another test.
            originalRemapConfig = GuidRemapper.Load();
            avatarRoot = new GameObject("Avatar");
        }

        [TearDown]
        public void TearDown()
        {
            GuidRemapper.Save(originalRemapConfig);
            if (avatarGuid != null) { CommitStore.DeleteAvatarHistory(avatarGuid); avatarGuid = null; }
            if (avatarRoot != null) Object.DestroyImmediate(avatarRoot);
        }

        // ---- 1. remap, past the pre-flight ----

        [Test]
        public void AfterARemap_TheReplacementPrefabIsActuallyInstantiated()
        {
            var root = ContainerManager.EnsureRoot(avatarRoot);
            var snapshot = new ContainerSnapshot
            {
                containerId = "outfit",
                // A real container guid shape: AvatarVcsContainer.AssignGuid
                // rejects anything that is not 32 lowercase hex chars.
                containerGuid = System.Guid.NewGuid().ToString("N"),
                prefabGuids = { oldPrefabGuid },
                localScale = Vector3.one,
            };

            // Stand in for "the prefab was reimported and got a new guid":
            // point the recorded guid at a different asset entirely, so a
            // restore that ignored the remap would resolve nothing.
            GuidRemapper.AddMapping(oldPrefabGuid, newPrefabGuid);

            var container = ContainerRestore.InstantiateContainer(snapshot, root);

            Assert.AreEqual(1, container.transform.childCount,
                "the pre-flight passing is not the point -- the container has to come back with the prefab in it");

            var instance = container.transform.GetChild(0).gameObject;
            Assert.AreEqual(newPrefabGuid, ContainerManager.GetPrefabGuid(instance),
                "and it must be the replacement prefab, not the recorded one");
        }

        // ---- 2. the real port implementations ----

        [Test]
        public void EditorHistoryStore_RoundTripsAConfigIndexAndCommit_ThroughRealStorage()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var store = new EditorHistoryStore();

            var commit = CommitBuilder.CreateCommit(avatarRoot, "first", "main", null);
            CommitStore.SaveCommit(avatarGuid, commit);

            Assert.AreEqual("first", store.LoadCommit(avatarGuid, commit.commitId)?.message);
            Assert.IsTrue(store.LoadIndex(avatarGuid).entries.Any(e => e.commitId == commit.commitId));
            Assert.IsNotNull(store.LoadConfig(avatarGuid));

            store.DeleteCommit(avatarGuid, commit.commitId);
            Assert.IsNull(store.LoadCommit(avatarGuid, commit.commitId));
        }

        // The note feature (KAN-94) writes through this method, which was
        // added to the adapter with nothing exercising it.
        [Test]
        public void EditorHistoryStore_SaveCommit_WritesThroughToStorage()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var store = new EditorHistoryStore();

            var commit = CommitBuilder.CreateCommit(avatarRoot, "first", "main", null);
            CommitStore.SaveCommit(avatarGuid, commit);

            commit.note = "outfit A + hair B";
            store.SaveCommit(avatarGuid, commit);

            Assert.AreEqual("outfit A + hair B", CommitStore.LoadCommit(avatarGuid, commit.commitId).note);
        }

        [Test]
        public void EditorHistoryStore_DeleteCommits_RefusesABranchHead_AndReportsIt()
        {
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);
            var store = new EditorHistoryStore();

            var head = BranchManager.Commit(avatarRoot, "head");

            var blocked = store.DeleteCommits(avatarGuid, new[] { head.commitId });

            CollectionAssert.Contains(blocked, head.commitId,
                "a branch head is refused, and the caller is told which ids were refused");
            Assert.IsNotNull(store.LoadCommit(avatarGuid, head.commitId));
        }

        [Test]
        public void EditorAvatarGateway_FindsTheAvatarGuid_AndCapturesLiveState()
        {
            var gateway = new EditorAvatarGateway { AvatarRoot = avatarRoot };
            ContainerManager.EnsureRootWithDefaults(avatarRoot);
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);

            Assert.AreEqual(avatarGuid, gateway.FindAvatarGuid(),
                "the gateway must report the same avatar the rest of the tool works with");

            var live = gateway.CaptureLiveState();
            Assert.IsNotNull(live);
            Assert.IsNotNull(live.containers);
            Assert.IsNotNull(live.avatarReferences);
        }

        [Test]
        public void EditorAvatarGateway_CommitCurrentState_ProducesACommitThatIsActuallyStored()
        {
            var gateway = new EditorAvatarGateway { AvatarRoot = avatarRoot };
            ContainerManager.EnsureRootWithDefaults(avatarRoot);
            avatarGuid = ContainerManager.GetAvatarGuid(avatarRoot);

            var commit = gateway.CommitCurrentState("from the gateway");

            Assert.AreEqual("from the gateway", commit.message);
            Assert.AreEqual("from the gateway", CommitStore.LoadCommit(avatarGuid, commit.commitId)?.message,
                "returning a Commit is not enough; it has to have reached storage");
        }

        [Test]
        public void EditorAvatarGateway_RegisterGuidRemap_IsVisibleToTheResolver()
        {
            var gateway = new EditorAvatarGateway { AvatarRoot = avatarRoot };

            gateway.RegisterGuidRemap(oldPrefabGuid, newPrefabGuid);

            Assert.AreEqual(newPrefabGuid, GuidRemapper.Resolve(oldPrefabGuid),
                "the remap the UI registers has to be the one restore reads");
        }
    }
}
