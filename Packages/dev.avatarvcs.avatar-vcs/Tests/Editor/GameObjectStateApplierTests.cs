using System.Linq;
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

        // KAN-83: layer comes from commit JSON, which this repo treats as
        // hand-editable and merge-corruptible. Unity has 32 layers and
        // misbehaves silently outside 0..31. Warn and leave it alone rather
        // than clamp -- 40 clamped to 31 is a different, equally wrong layer,
        // and silently moving an object onto one is worse than not moving it.
        [TestCase(32)]
        [TestCase(40)]
        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        public void Apply_LayerOutsideUnitysRange_LeavesItUnchangedAndWarns(int layer)
        {
            go.layer = 5;

            GameObjectStateApplier.Apply(go, activeSelf: true, tag: null, layer: layer, "the widget", "Undo", log);

            Assert.AreEqual(5, go.layer, "an out-of-range layer must not move the object");
            Assert.IsTrue(
                log.Entries.Any(e => e.Severity == DiagnosticSeverity.Warning
                    && e.Message.Contains("0..31") && e.Message.Contains("the widget")),
                "and the user has to be told, naming what it was recorded for");
        }

        [TestCase(0)]
        [TestCase(31)]
        public void Apply_LayerAtTheEdgesOfUnitysRange_IsStillApplied(int layer)
        {
            go.layer = 5;

            GameObjectStateApplier.Apply(go, activeSelf: true, tag: null, layer: layer, "context", "Undo", log);

            Assert.AreEqual(layer, go.layer, "0 and 31 are valid layers; the guard must not exclude them");
            Assert.IsEmpty(log.Entries);
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
