using System.Reflection;
using AvatarVcs.Editor.Core;
using AvatarVcs.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// Covers CODE_REVIEW.md's Ctrl+D duplication finding: duplicating a
    /// GameObject clones its serialized fields verbatim, so a duplicated
    /// AvatarVcsContainer/AvatarVcsRoot starts out sharing its source's
    /// (supposedly immutable) guid. OnValidate self-heals this, but Unity's
    /// own guarantees about exactly when OnValidate fires after a duplicate
    /// aren't something a test should depend on -- these invoke it directly
    /// via reflection to test the conflict-resolution logic itself.
    /// </summary>
    public class MarkerComponentTests
    {
        private readonly System.Collections.Generic.List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        private GameObject Spawn(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            spawned.Add(go);
            return go;
        }

        private static void InvokeOnValidate(Component c) =>
            c.GetType().GetMethod("OnValidate", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(c, null);

        [Test]
        public void AvatarVcsRoot_AssignGuid_RejectsNonGuidShapedValue()
        {
            // avatarGuid keys CommitStore's on-disk paths; catching a
            // malformed value here (this tool's own generation path) is
            // defense-in-depth on top of CommitStore's own validation at the
            // point avatarGuid actually becomes a path.
            var root = Spawn("Root").AddComponent<AvatarVcsRoot>();

            Assert.Throws<System.ArgumentException>(() => root.AssignGuid("../../../outside"));
            Assert.Throws<System.ArgumentException>(() => root.AssignGuid("too-short"));
        }

        [Test]
        public void AvatarVcsContainer_AssignGuid_IsImmutable()
        {
            var container = Spawn("Container").AddComponent<AvatarVcsContainer>();
            const string guid1 = "11111111111111111111111111111111";
            container.AssignGuid(guid1);

            Assert.Throws<System.InvalidOperationException>(() => container.AssignGuid("22222222222222222222222222222222"));
            Assert.AreEqual(guid1, container.ContainerGuid);
        }

        [Test]
        public void AvatarVcsContainer_AssignGuid_RejectsNonGuidShapedValue()
        {
            var container = Spawn("Container").AddComponent<AvatarVcsContainer>();

            Assert.Throws<System.ArgumentException>(() => container.AssignGuid("../../../outside"));
            Assert.Throws<System.ArgumentException>(() => container.AssignGuid("too-short"));
        }

        [Test]
        public void AvatarVcsContainer_OnValidate_DuplicateGuid_LowerSiblingKeepsIt_HigherRegenerates()
        {
            var parent = Spawn("Parent");
            const string sharedGuid = "0123456789abcdef0123456789abcdea";
            var original = Spawn("outfit_a", parent.transform).AddComponent<AvatarVcsContainer>();
            original.AssignGuid(sharedGuid);
            var duplicate = Spawn("outfit_a_dup", parent.transform).AddComponent<AvatarVcsContainer>();
            duplicate.AssignGuid(sharedGuid); // simulates the clone Ctrl+D would have produced

            InvokeOnValidate(duplicate);

            Assert.AreEqual(sharedGuid, original.ContainerGuid, "lower sibling index keeps the guid");
            Assert.AreNotEqual(sharedGuid, duplicate.ContainerGuid, "higher sibling index must regenerate");
        }

        [Test]
        public void AvatarVcsContainer_OnValidate_NoCollision_LeavesGuidUnchanged()
        {
            var parent = Spawn("Parent");
            const string guidA = "0123456789abcdef0123456789abcdea";
            const string guidB = "0123456789abcdef0123456789abcdeb";
            var a = Spawn("outfit_a", parent.transform).AddComponent<AvatarVcsContainer>();
            a.AssignGuid(guidA);
            var b = Spawn("outfit_b", parent.transform).AddComponent<AvatarVcsContainer>();
            b.AssignGuid(guidB);

            InvokeOnValidate(a);
            InvokeOnValidate(b);

            Assert.AreEqual(guidA, a.ContainerGuid);
            Assert.AreEqual(guidB, b.ContainerGuid);
        }

        [Test]
        public void AvatarVcsRoot_OnValidate_DuplicateAvatarSiblings_HigherIndexRegenerates()
        {
            var parent = Spawn("Scene");
            var avatarA = Spawn("Avatar", parent.transform);
            const string sharedGuid = "0123456789abcdef0123456789abcdef"; // valid shape: AssignGuid now enforces 32-char lowercase hex
            var rootA = Spawn(ContainerManager.RootName, avatarA.transform).AddComponent<AvatarVcsRoot>();
            rootA.AssignGuid(sharedGuid);

            var avatarB = Spawn("Avatar (1)", parent.transform); // simulates duplicating the whole avatar
            var rootB = Spawn(ContainerManager.RootName, avatarB.transform).AddComponent<AvatarVcsRoot>();
            rootB.AssignGuid(sharedGuid);

            InvokeOnValidate(rootB);

            Assert.AreEqual(sharedGuid, rootA.AvatarGuid, "the original avatar keeps its guid");
            Assert.AreNotEqual(sharedGuid, rootB.AvatarGuid, "the duplicated avatar must regenerate");
        }
    }
}
