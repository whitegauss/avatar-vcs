using AvatarVcs.Core.Diagnostics;
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
    ///
    /// KAN-20: Apply no longer returns a warning string; it appends to a
    /// DiagnosticLog the caller owns.
    /// </summary>
    public class GameObjectStateApplierTests
    {
        private GameObject go;
        private DiagnosticLog log;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("Target");
            log = new DiagnosticLog();
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
                GameObjectStateApplier.Apply(null, activeSelf: true, tag: "Player", layer: 0, "context", "Undo", log));
        }

        [Test]
        public void Apply_SetsActiveSelfAndLayer_NoTagRecorded_LogsNothing()
        {
            GameObjectStateApplier.Apply(go, activeSelf: false, tag: null, layer: 3, "context", "Undo", log);

            Assert.IsTrue(log.IsEmpty);
            Assert.IsFalse(go.activeSelf);
            Assert.AreEqual(3, go.layer);
            Assert.AreEqual("Untagged", go.tag);
        }

        [Test]
        public void Apply_DefinedTag_SetsItAndLogsNothing()
        {
            GameObjectStateApplier.Apply(go, activeSelf: true, tag: "Player", layer: 0, "context", "Undo", log);

            Assert.IsTrue(log.IsEmpty);
            Assert.AreEqual("Player", go.tag);
        }

        [Test]
        public void Apply_UndefinedTag_LeavesTagUnchangedAndWarns()
        {
            GameObjectStateApplier.Apply(go, activeSelf: true, tag: "ThisTagDoesNotExist_12345", layer: 0, "the widget", "Undo", log);

            Assert.AreEqual(1, log.Entries.Count);
            Assert.AreEqual(DiagnosticSeverity.Warning, log.Entries[0].Severity);
            var warning = log.Entries[0].Message;
            StringAssert.Contains("ThisTagDoesNotExist_12345", warning);
            StringAssert.Contains("the widget", warning);
            StringAssert.Contains("not defined in this project's Tag Manager", warning);
            Assert.AreEqual("Untagged", go.tag);
        }

        [Test]
        public void Apply_TagAlreadyMatches_DoesNotReapplyOrWarn()
        {
            go.tag = "Player";

            GameObjectStateApplier.Apply(go, activeSelf: true, tag: "Player", layer: 0, "context", "Undo", log);

            Assert.IsTrue(log.IsEmpty);
            Assert.AreEqual("Player", go.tag);
        }
    }
}
