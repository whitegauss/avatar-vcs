using AvatarVcs.Editor.Apply;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// The shared activeSelf/tag/layer apply logic behind both
    /// ContainerRestore.InstantiateContainerStructure and
    /// AvatarReferenceApplier.ApplyObjectStates -- extracted after the same
    /// "GameObject.tag doesn't throw for an undefined tag" bug was
    /// independently found and fixed in both call sites.
    /// </summary>
    public class GameObjectStateApplierTests
    {
        private GameObject go;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("Target");
        }

        [TearDown]
        public void TearDown()
        {
            if (go != null) Object.DestroyImmediate(go);
        }

        [Test]
        public void Apply_NullGameObject_ThrowsArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                GameObjectStateApplier.Apply(null, activeSelf: true, tag: "Player", layer: 0, "context", "Undo"));
        }

        [Test]
        public void Apply_SetsActiveSelfAndLayer_NoTagRecorded_ReturnsNull()
        {
            var warning = GameObjectStateApplier.Apply(go, activeSelf: false, tag: null, layer: 3, "context", "Undo");

            Assert.IsNull(warning);
            Assert.IsFalse(go.activeSelf);
            Assert.AreEqual(3, go.layer);
            Assert.AreEqual("Untagged", go.tag);
        }

        [Test]
        public void Apply_DefinedTag_SetsItAndReturnsNull()
        {
            var warning = GameObjectStateApplier.Apply(go, activeSelf: true, tag: "Player", layer: 0, "context", "Undo");

            Assert.IsNull(warning);
            Assert.AreEqual("Player", go.tag);
        }

        [Test]
        public void Apply_UndefinedTag_LeavesTagUnchangedAndReturnsWarning()
        {
            var warning = GameObjectStateApplier.Apply(go, activeSelf: true, tag: "ThisTagDoesNotExist_12345", layer: 0, "the widget", "Undo");

            Assert.IsNotNull(warning);
            StringAssert.Contains("ThisTagDoesNotExist_12345", warning);
            StringAssert.Contains("the widget", warning);
            StringAssert.Contains("not defined in this project's Tag Manager", warning);
            Assert.AreEqual("Untagged", go.tag);
        }

        [Test]
        public void Apply_TagAlreadyMatches_DoesNotReapplyOrWarn()
        {
            go.tag = "Player";

            var warning = GameObjectStateApplier.Apply(go, activeSelf: true, tag: "Player", layer: 0, "context", "Undo");

            Assert.IsNull(warning);
            Assert.AreEqual("Player", go.tag);
        }
    }
}
