using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Core.History;
using AvatarVcs.Core.Model;
using NUnit.Framework;

namespace AvatarVcs.Tests.Core
{
    [Category("Core")]
    public class CommitIndexOpsTests
    {
        private static CommitIndexEntry Entry(string id, string ts) =>
            new() { commitId = id, timestamp = ts, message = id };

        [Test]
        public void Upsert_ReplacesAnyExistingEntryWithTheSameId()
        {
            var index = new CommitIndex();
            CommitIndexOps.Upsert(index, Entry("c1", "2026-01-01T00:00:00Z"));
            CommitIndexOps.Upsert(index, new CommitIndexEntry { commitId = "c1", message = "updated", timestamp = "2026-01-02T00:00:00Z" });

            Assert.AreEqual(1, index.entries.Count);
            Assert.AreEqual("updated", index.entries[0].message);
        }

        [Test]
        public void Remove_DropsEveryListedIdAndLeavesTheRest()
        {
            var index = new CommitIndex();
            index.entries.Add(Entry("c1", "t1"));
            index.entries.Add(Entry("c2", "t2"));
            index.entries.Add(Entry("c3", "t3"));

            CommitIndexOps.Remove(index, new HashSet<string> { "c1", "c3" });

            CollectionAssert.AreEqual(new[] { "c2" }, index.entries.Select(e => e.commitId));
        }

        [Test]
        public void NewestFirst_OrdersByTimestampDescending()
        {
            var index = new CommitIndex();
            index.entries.Add(Entry("old", "2026-01-01T00:00:00Z"));
            index.entries.Add(Entry("new", "2026-03-01T00:00:00Z"));
            index.entries.Add(Entry("mid", "2026-02-01T00:00:00Z"));

            CollectionAssert.AreEqual(new[] { "new", "mid", "old" },
                CommitIndexOps.NewestFirst(index).Select(e => e.commitId));
        }

        [Test]
        public void EntryFor_ReturnsTheMatchOrNull()
        {
            var index = new CommitIndex();
            index.entries.Add(Entry("c1", "t1"));

            Assert.AreEqual("c1", CommitIndexOps.EntryFor(index, "c1").commitId);
            Assert.IsNull(CommitIndexOps.EntryFor(index, "missing"));
        }
    }
}
