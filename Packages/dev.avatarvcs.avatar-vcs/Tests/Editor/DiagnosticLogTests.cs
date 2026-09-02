using AvatarVcs.Core.Diagnostics;
using NUnit.Framework;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// KAN-20: the deferred-diagnostics collector that capture/apply helpers
    /// now append to instead of calling Debug.LogWarning directly.
    /// </summary>
    [Category("Core")]
    public class DiagnosticLogTests
    {
        [Test]
        public void NewLog_IsEmpty()
        {
            var log = new DiagnosticLog();
            Assert.IsTrue(log.IsEmpty);
            Assert.AreEqual(0, log.Entries.Count);
        }

        [Test]
        public void Warn_And_Error_RecordSeverityAndMessageInOrder()
        {
            var log = new DiagnosticLog();
            log.Warn("first");
            log.Error("second");
            log.Warn("third");

            Assert.IsFalse(log.IsEmpty);
            Assert.AreEqual(3, log.Entries.Count);
            Assert.AreEqual(DiagnosticSeverity.Warning, log.Entries[0].Severity);
            Assert.AreEqual("first", log.Entries[0].Message);
            Assert.AreEqual(DiagnosticSeverity.Error, log.Entries[1].Severity);
            Assert.AreEqual("second", log.Entries[1].Message);
            Assert.AreEqual(DiagnosticSeverity.Warning, log.Entries[2].Severity);
        }

        [Test]
        public void AddRange_AppendsEntriesFromAnotherLog()
        {
            var inner = new DiagnosticLog();
            inner.Warn("a");
            inner.Warn("b");

            var outer = new DiagnosticLog();
            outer.Warn("x");
            outer.AddRange(inner);

            Assert.AreEqual(3, outer.Entries.Count);
            Assert.AreEqual("x", outer.Entries[0].Message);
            Assert.AreEqual("a", outer.Entries[1].Message);
            Assert.AreEqual("b", outer.Entries[2].Message);
        }

        [Test]
        public void AddRange_Null_IsNoOp()
        {
            var log = new DiagnosticLog();
            log.Warn("only");
            Assert.DoesNotThrow(() => log.AddRange(null));
            Assert.AreEqual(1, log.Entries.Count);
        }
    }
}
