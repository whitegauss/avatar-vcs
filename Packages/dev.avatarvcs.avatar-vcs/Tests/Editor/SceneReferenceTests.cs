using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Editor.Capture;
using AvatarVcs.Editor.Core;
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
    /// as an asset reference. Uses the built-in HingeJoint.connectedBody
    /// (a Rigidbody field) as a stand-in, since it needs no MA/VRChat
    /// dependency and is exactly this "Component pointing at another live
    /// Component" shape.
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
        public void Diagnostic_DumpHingeJointCapture()
        {
            var avatarRoot = Spawn("Avatar");
            var armature = Spawn("Armature", avatarRoot.transform);
            var bone = Spawn("Hip", armature.transform);
            var boneRigidbody = bone.AddComponent<Rigidbody>();

            var configRoot = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(configRoot, "outfit_a");
            var joint = container.AddComponent<HingeJoint>();
            joint.connectedBody = boneRigidbody;

            TestContext.WriteLine($"connectedBody after assignment: {(joint.connectedBody == null ? "null" : joint.connectedBody.name)}");

            var rawSo = new UnityEditor.SerializedObject(joint);
            var rawProp = rawSo.GetIterator();
            var rawEnter = true;
            while (rawProp.NextVisible(rawEnter))
            {
                rawEnter = rawProp.propertyType == UnityEditor.SerializedPropertyType.Generic;
                TestContext.WriteLine($"RAW path={rawProp.propertyPath} name={rawProp.name} type={rawProp.propertyType}");
            }

            var state = ComponentCapturer.Capture(joint, container.transform, avatarRoot.transform);

            TestContext.WriteLine($"fields.Count={state.fields.Count}");
            foreach (var f in state.fields) TestContext.WriteLine($"  field key={f.key} type={f.type} value={f.value}");
            TestContext.WriteLine($"assetRefs.Count={state.assetRefs.Count}");
            foreach (var a in state.assetRefs) TestContext.WriteLine($"  assetRef key={a.key} guid={a.guid}");
            TestContext.WriteLine($"sceneRefs.Count={state.sceneRefs.Count}");
            foreach (var s in state.sceneRefs) TestContext.WriteLine($"  sceneRef key={s.key} path={s.path} type={s.type}");

            Assert.Pass("diagnostic dump above");
        }

        [Test]
        public void Capture_ClassifiesSceneObjectReference_AsSceneRefByPath_NotAssetRef()
        {
            var avatarRoot = Spawn("Avatar");
            var armature = Spawn("Armature", avatarRoot.transform);
            var bone = Spawn("Hip", armature.transform);
            var boneRigidbody = bone.AddComponent<Rigidbody>();

            var configRoot = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(configRoot, "outfit_a");
            var joint = container.AddComponent<HingeJoint>();
            joint.connectedBody = boneRigidbody;

            var state = ComponentCapturer.Capture(joint, container.transform, avatarRoot.transform);

            var sceneRef = state.sceneRefs.Single();
            Assert.AreEqual("Armature/Hip", sceneRef.path);
            Assert.AreEqual(typeof(Rigidbody).FullName, sceneRef.type);
            Assert.IsFalse(state.assetRefs.Any(a => a.key == sceneRef.key), "the connectedBody field must not also appear as an asset ref");
        }

        [Test]
        public void CaptureThenRestore_SceneReferenceOutsideContainer_PointsAtSameLiveObject()
        {
            var avatarRoot = Spawn("Avatar");
            var armature = Spawn("Armature", avatarRoot.transform);
            var bone = Spawn("Hip", armature.transform);
            var boneRigidbody = bone.AddComponent<Rigidbody>();

            var configRoot = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(configRoot, "outfit_a");
            var joint = container.AddComponent<HingeJoint>();
            joint.connectedBody = boneRigidbody;

            var snapshot = ContainerCapture.CaptureContainer(container.transform, avatarRoot.transform);

            // Destroy and regenerate the container -- the bone (outside the
            // container, in avatar-owned territory) is left untouched.
            var restored = ContainerRestore.InstantiateContainer(snapshot, configRoot);

            var restoredJoint = restored.GetComponent<HingeJoint>();
            Assert.IsNotNull(restoredJoint);
            Assert.AreSame(boneRigidbody, restoredJoint.connectedBody);
        }

        [Test]
        public void Capture_SceneReferenceOutsideAvatarHierarchy_IsSkippedWithWarning()
        {
            var avatarRoot = Spawn("Avatar");
            var unrelatedRoot = Spawn("Unrelated");
            var unrelatedRigidbody = unrelatedRoot.AddComponent<Rigidbody>();

            var configRoot = ContainerManager.EnsureRoot(avatarRoot);
            var container = ContainerManager.CreateContainer(configRoot, "outfit_a");
            var joint = container.AddComponent<HingeJoint>();
            joint.connectedBody = unrelatedRigidbody;

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Scene reference .* points outside the avatar hierarchy"));
            var state = ComponentCapturer.Capture(joint, container.transform, avatarRoot.transform);

            Assert.IsEmpty(state.sceneRefs);
        }
    }
}
