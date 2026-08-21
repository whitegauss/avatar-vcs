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
        public void AvatarVcsContainer_AssignGuid_IsImmutable()
        {
            var container = Spawn("Container").AddComponent<AvatarVcsContainer>();
            container.AssignGuid("guid-1");

            Assert.Throws<System.InvalidOperationException>(() => container.AssignGuid("guid-2"));
            Assert.AreEqual("guid-1", container.ContainerGuid);
        }

        [Test]
        public void AvatarVcsContainer_OnValidate_DuplicateGuid_LowerSiblingKeepsIt_HigherRegenerates()
        {
            var parent = Spawn("Parent");
            var original = Spawn("outfit_a", parent.transform).AddComponent<AvatarVcsContainer>();
            original.AssignGuid("shared-guid");
            var duplicate = Spawn("outfit_a_dup", parent.transform).AddComponent<AvatarVcsContainer>();
            duplicate.AssignGuid("shared-guid"); // simulates the clone Ctrl+D would have produced

            InvokeOnValidate(duplicate);

            Assert.AreEqual("shared-guid", original.ContainerGuid, "lower sibling index keeps the guid");
            Assert.AreNotEqual("shared-guid", duplicate.ContainerGuid, "higher sibling index must regenerate");
        }

        [Test]
        public void AvatarVcsContainer_OnValidate_NoCollision_LeavesGuidUnchanged()
        {
            var parent = Spawn("Parent");
            var a = Spawn("outfit_a", parent.transform).AddComponent<AvatarVcsContainer>();
            a.AssignGuid("guid-a");
            var b = Spawn("outfit_b", parent.transform).AddComponent<AvatarVcsContainer>();
            b.AssignGuid("guid-b");

            InvokeOnValidate(a);
            InvokeOnValidate(b);

            Assert.AreEqual("guid-a", a.ContainerGuid);
            Assert.AreEqual("guid-b", b.ContainerGuid);
        }

        [Test]
        public void AvatarVcsRoot_OnValidate_DuplicateAvatarSiblings_HigherIndexRegenerates()
        {
            var parent = Spawn("Scene");
            var avatarA = Spawn("Avatar", parent.transform);
            var rootA = Spawn(ContainerManager.RootName, avatarA.transform).AddComponent<AvatarVcsRoot>();
            rootA.AssignGuid("shared-avatar-guid");

            var avatarB = Spawn("Avatar (1)", parent.transform); // simulates duplicating the whole avatar
            var rootB = Spawn(ContainerManager.RootName, avatarB.transform).AddComponent<AvatarVcsRoot>();
            rootB.AssignGuid("shared-avatar-guid");

            InvokeOnValidate(rootB);

            Assert.AreEqual("shared-avatar-guid", rootA.AvatarGuid, "the original avatar keeps its guid");
            Assert.AreNotEqual("shared-avatar-guid", rootB.AvatarGuid, "the duplicated avatar must regenerate");
        }
    }
}
