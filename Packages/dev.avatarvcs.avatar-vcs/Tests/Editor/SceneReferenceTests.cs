using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Editor.Capture;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using AvatarVcs.Editor.Model;
using AvatarVcs.Editor.Operations;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// Covers the scene-reference fix: ObjectReference fields pointing at
    /// live scene objects (e.g. VRCPhysBone.rootTransform,
    /// ModularAvatarMergeArmature pointing at a bone on the avatar's own
    /// Armature) must round-trip by path instead of being silently nulled
    /// out, which is what happened when every ObjectReference was treated
    /// as an asset reference. Uses the built-in
    /// SkinnedMeshRenderer.rootBone (a Transform field) as a stand-in, since
    /// it needs no MA/VRChat dependency and is exactly this "Component
    /// pointing at another live Transform" shape.
    ///
    /// (HingeJoint.connectedBody was tried first but turned out to be a poor
    /// stand-in: it never appears via SerializedObject's NextVisible walk at
    /// all -- a pre-existing, unrelated Unity quirk with Joint types'
    /// custom property drawers, confirmed via a raw-property dump in CI.
    /// rootBone is a plain field with no such special-casing.)
    /// </summary>
    public class SceneReferenceTests
    {
        private readonly List<GameObject> spawned = new();

        private GameObject Spawn(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent);
            spawned.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        [Test]
        public void Capture_ClassifiesSceneObjectReference_AsSceneRefByPath_NotAssetRef()
        {
            var avatarRoot = Spawn("Avatar");
            var armature = Spawn("Armature", avatarRoot.transform);
            var bone = Spawn("Hip", armature.transform);

            var configRoot = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(configRoot, "outfit_a");
            var renderer = container.AddComponent<SkinnedMeshRenderer>();
            renderer.rootBone = bone.transform;

            var state = ComponentCapturer.Capture(renderer, container.transform, avatarRoot.transform);

            var sceneRef = state.sceneRefs.Single(s => s.key == "m_RootBone");
            Assert.AreEqual("Armature/Hip", sceneRef.path);
            Assert.AreEqual(typeof(Transform).FullName, sceneRef.type);
            Assert.IsFalse(state.assetRefs.Any(a => a.key == sceneRef.key), "rootBone must not also appear as an asset ref");
        }

        [Test]
        public void CaptureThenRestore_SceneReferenceOutsideContainer_PointsAtSameLiveObject()
        {
            var avatarRoot = Spawn("Avatar");
            var armature = Spawn("Armature", avatarRoot.transform);
            var bone = Spawn("Hip", armature.transform);

            var configRoot = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(configRoot, "outfit_a");
            var renderer = container.AddComponent<SkinnedMeshRenderer>();
            renderer.rootBone = bone.transform;

            var snapshot = ContainerCapture.CaptureContainer(container.transform, avatarRoot.transform);

            // Destroy and regenerate the container -- the bone (outside the
            // container, in avatar-owned territory) is left untouched.
            var restored = ContainerRestore.InstantiateContainer(snapshot, configRoot);

            var restoredRenderer = restored.GetComponent<SkinnedMeshRenderer>();
            Assert.IsNotNull(restoredRenderer);
            Assert.AreSame(bone.transform, restoredRenderer.rootBone);
        }

        [Test]
        public void Capture_SceneReferenceOutsideAvatarHierarchy_IsSkippedWithWarning()
        {
            var avatarRoot = Spawn("Avatar");
            var unrelatedRoot = Spawn("Unrelated");

            var configRoot = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(configRoot, "outfit_a");
            var renderer = container.AddComponent<SkinnedMeshRenderer>();
            renderer.rootBone = unrelatedRoot.transform;

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Scene reference .* points outside the avatar hierarchy"));
            var state = ComponentCapturer.Capture(renderer, container.transform, avatarRoot.transform);

            Assert.IsFalse(state.sceneRefs.Any(s => s.key == "m_RootBone"));
        }

        [Test]
        public void Checkout_ComponentReferencingAnotherContainer_ResolvesRegardlessOfContainerOrder()
        {
            // A component on one container's root can reference an object
            // inside a *different* container (design doc allows arbitrary
            // component references on a container root). Restoring
            // container-by-container (instantiate structure, then
            // immediately apply that container's components, then move to
            // the next container) would fail to resolve this if the
            // referencing container happens to come first in commit.containers
            // -- the referenced container wouldn't exist yet. Deliberately
            // ordering the referencer first below reproduces exactly that.
            var avatarRoot = Spawn("Avatar");
            var configRoot = ContainerManager.EnsureRoot(avatarRoot);
            var containerA = ContainerManager.CreateContainer(configRoot, "container_a");
            var containerB = ContainerManager.CreateContainer(configRoot, "container_b");

            var renderer = containerA.AddComponent<SkinnedMeshRenderer>();
            renderer.rootBone = containerB.transform; // cross-container scene reference

            var snapshotA = ContainerCapture.CaptureContainer(containerA.transform, avatarRoot.transform);
            var snapshotB = ContainerCapture.CaptureContainer(containerB.transform, avatarRoot.transform);
            var commit = new Commit { containers = { snapshotA, snapshotB } }; // referencer (A) listed before referenced (B)

            var result = CheckoutOperation.CheckoutWithoutAutoCommit(commit, avatarRoot);

            Assert.IsTrue(result.IsSuccess);
            var restoredA = configRoot.transform.Find("container_a");
            var restoredB = configRoot.transform.Find("container_b");
            var restoredRenderer = restoredA.GetComponent<SkinnedMeshRenderer>();
            Assert.AreSame(restoredB, restoredRenderer.rootBone);
        }
    }
}
