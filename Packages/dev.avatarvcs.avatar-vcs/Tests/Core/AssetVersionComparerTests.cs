using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Core.History;
using AvatarVcs.Core.Model;
using NUnit.Framework;

namespace AvatarVcs.Tests.Core
{
    [Category("Core")]
    public class AssetVersionComparerTests
    {
        private static AssetVersionEntry Entry(string guid, string name, string hash) =>
            new() { guid = guid, assetName = name, contentHash = hash, recordedAt = "2026-01-01T00:00:00Z" };

        [Test]
        public void BuildWarnings_MissingAsset_ReportsItGone()
        {
            var recorded = new[] { Entry("g1", "Body.mat", "h1") };
            var warnings = AssetVersionComparer.BuildWarnings(recorded,
                _ => new AssetVersionProbe { Exists = false });

            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("no longer in the project", warnings[0]);
            StringAssert.Contains("Body.mat", warnings[0]);
        }

        [Test]
        public void BuildWarnings_ChangedHash_ReportsItMayLookDifferent()
        {
            var recorded = new[] { Entry("g1", "Body.mat", "h1") };
            var warnings = AssetVersionComparer.BuildWarnings(recorded,
                _ => new AssetVersionProbe { Exists = true, ContentHash = "h2" });

            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("has changed since this commit was recorded", warnings[0]);
        }

        [Test]
        public void BuildWarnings_SameHash_IsQuiet()
        {
            var recorded = new[] { Entry("g1", "Body.mat", "h1") };
            var warnings = AssetVersionComparer.BuildWarnings(recorded,
                _ => new AssetVersionProbe { Exists = true, ContentHash = "h1" });

            Assert.IsEmpty(warnings);
        }

        [Test]
        public void BuildWarnings_NullRecorded_IsEmptyNotAThrow()
        {
            Assert.IsEmpty(AssetVersionComparer.BuildWarnings(null, _ => default));
        }

        [Test]
        public void BuildEntries_CopiesEachTupleWithTheSharedRecordedAt()
        {
            var entries = AssetVersionComparer.BuildEntries(
                new[] { ("g1", "A.mat", "h1"), ("g2", "B.prefab", "h2") }, "2026-05-05T00:00:00Z");

            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual("g2", entries[1].guid);
            Assert.IsTrue(entries.All(e => e.recordedAt == "2026-05-05T00:00:00Z"));
        }
    }
}
