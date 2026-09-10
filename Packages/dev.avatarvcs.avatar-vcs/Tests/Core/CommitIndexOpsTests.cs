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

        // The recovery path for a lost or unreadable index.json: everything in
        // an entry is copied from the commit, so the commits can produce it
        // again. Ids are real 32-hex guids because that is what
        // CommitIdentifier lets through on the way back in.
        [Test]
        public void RebuildFrom_CopiesEveryEntryFieldOffTheCommit()
        {
            var commit = new Commit
            {
                commitId = "d098f2b073af4f40a6e5291f71d4f55b",
                parentCommitId = "a465f09c1d6d4edab7d4f8bbb31ad466",
                branch = "long-hair",
                message = "sa",
                timestamp = "2026-09-03T05:27:46.5108946Z",
            };

            var entry = CommitIndexOps.RebuildFrom(new[] { commit }).entries.Single();

            Assert.AreEqual(commit.commitId, entry.commitId);
            Assert.AreEqual(commit.parentCommitId, entry.parentCommitId);
            Assert.AreEqual(commit.branch, entry.branch);
            Assert.AreEqual(commit.message, entry.message);
            Assert.AreEqual(commit.timestamp, entry.timestamp);
        }

        [Test]
        public void RebuildFrom_SkipsWhatItCannotIdentify()
        {
            var index = CommitIndexOps.RebuildFrom(new[]
            {
                null,
                new Commit { commitId = null, branch = "main" },
                new Commit { commitId = "", branch = "main" },
                new Commit { commitId = "d098f2b073af4f40a6e5291f71d4f55b", branch = "main" },
            });

            Assert.AreEqual("d098f2b073af4f40a6e5291f71d4f55b", index.entries.Single().commitId);
        }

        [Test]
        public void RebuildFrom_NothingToRebuildFromIsAnEmptyIndex()
        {
            Assert.IsEmpty(CommitIndexOps.RebuildFrom(null).entries);
            Assert.IsEmpty(CommitIndexOps.RebuildFrom(new Commit[0]).entries);
        }
    }
}
