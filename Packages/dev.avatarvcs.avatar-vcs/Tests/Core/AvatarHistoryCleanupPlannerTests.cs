using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Core.History;
using NUnit.Framework;

namespace AvatarVcs.Tests.Core
{
    /// <summary>
    /// This policy decides deletions of version-control history, so every rule
    /// it applies is pinned here rather than left to the Editor glue.
    /// </summary>
    [Category("Core")]
    public class AvatarHistoryCleanupPlannerTests
    {
        private static AvatarHistoryInfo History(string guid, bool referenced, string timestamp, int commits = 1) =>
            new()
            {
                avatarGuid = guid,
                isReferenced = referenced,
                newestCommitTimestamp = timestamp,
                commitCount = commits,
            };

        private static List<string> Deleted(IEnumerable<AvatarHistoryInfo> histories, int keep = 1) =>
            AvatarHistoryCleanupPlanner.Plan(histories, keep)
                .Where(d => d.delete)
                .Select(d => d.history.avatarGuid)
                .ToList();

        [Test]
        public void AReferencedHistoryIsNeverDeleted()
        {
            var histories = new[]
            {
                History("live", referenced: true, "2020-01-01T00:00:00Z"), // oldest, but in use
                History("orphan_a", referenced: false, "2026-01-01T00:00:00Z"),
                History("orphan_b", referenced: false, "2025-01-01T00:00:00Z"),
            };

            CollectionAssert.AreEquivalent(new[] { "orphan_b" }, Deleted(histories));
        }

        [Test]
        public void TheMostRecentOrphanIsKeptAsASafetyNet()
        {
            // The user's reason for keeping one: removing the [AvatarVCS] root
            // by accident mints a new id, and the history that just went
            // orphaned is the one they would want back.
            var histories = new[]
            {
                History("old", referenced: false, "2024-01-01T00:00:00Z"),
                History("newest", referenced: false, "2026-09-04T00:00:00Z"),
                History("middle", referenced: false, "2025-06-01T00:00:00Z"),
            };

            CollectionAssert.AreEquivalent(new[] { "old", "middle" }, Deleted(histories));
        }

        [Test]
        public void AnOrphanWithNoCommitsNeverOccupiesTheRetainedSlot()
        {
            // An empty history is the cheapest thing to lose, so it must not
            // push a real one out of the single kept slot.
            var histories = new[]
            {
                History("empty", referenced: false, null, commits: 0),
                History("hasCommits", referenced: false, "2025-01-01T00:00:00Z", commits: 5),
            };

            CollectionAssert.AreEquivalent(new[] { "empty" }, Deleted(histories));
        }

        [Test]
        public void TimestampsAreComparedAsInstants_NotAsText()
        {
            // "2026-09-04T09:00:00+09:00" is 00:00Z -- earlier than 01:00Z,
            // although it sorts later as a plain string.
            var histories = new[]
            {
                History("offsetMorning", referenced: false, "2026-09-04T09:00:00+09:00"),
                History("utcLater", referenced: false, "2026-09-04T01:00:00Z"),
            };

            CollectionAssert.AreEquivalent(new[] { "offsetMorning" }, Deleted(histories));
        }

        [Test]
        public void KeepingZeroOrphansDeletesEveryUnreferencedHistory()
        {
            var histories = new[]
            {
                History("live", referenced: true, "2026-01-01T00:00:00Z"),
                History("a", referenced: false, "2026-01-01T00:00:00Z"),
                History("b", referenced: false, "2025-01-01T00:00:00Z"),
            };

            CollectionAssert.AreEquivalent(new[] { "a", "b" }, Deleted(histories, keep: 0));
        }

        [Test]
        public void EverythingReferenced_DeletesNothing()
        {
            var histories = new[]
            {
                History("a", referenced: true, "2026-01-01T00:00:00Z"),
                History("b", referenced: true, "2025-01-01T00:00:00Z"),
            };

            CollectionAssert.IsEmpty(Deleted(histories));
        }

        [Test]
        public void EveryHistoryGetsADecisionWithAReason()
        {
            var plan = AvatarHistoryCleanupPlanner.Plan(new[]
            {
                History("live", referenced: true, "2026-01-01T00:00:00Z"),
                History("keptOrphan", referenced: false, "2026-01-01T00:00:00Z"),
                History("goner", referenced: false, "2020-01-01T00:00:00Z"),
            });

            Assert.AreEqual(3, plan.Count);
            foreach (var decision in plan)
                Assert.IsFalse(string.IsNullOrEmpty(decision.reason), $"{decision.history.avatarGuid} needs a reason");
        }

        // An incomplete project scan must never widen the deletion set. A scan
        // comes back short for reasons that say nothing about the avatar: the
        // project serialises assets as binary, a file is locked, the user
        // cancelled. Treating "not found" as "orphaned" there deletes the
        // history of avatars that are very much alive.
        [Test]
        public void AnIncompleteScan_ReportsEveryUnresolvedHistoryAsStillReferenced()
        {
            var all = new[] { "a", "b", "c" };

            var referenced = AvatarHistoryCleanupPlanner.ReferencedAfterScan(
                all, positivelyFound: new[] { "a" }, scanCompleted: false);

            CollectionAssert.AreEquivalent(all, referenced);
            CollectionAssert.IsEmpty(Deleted(all.Select(g => History(g, referenced.Contains(g), "2026-01-01T00:00:00Z"))));
        }

        [Test]
        public void ACompleteScan_ReportsOnlyWhatItFound()
        {
            var all = new[] { "a", "b", "c" };

            var referenced = AvatarHistoryCleanupPlanner.ReferencedAfterScan(
                all, positivelyFound: new[] { "a" }, scanCompleted: true);

            CollectionAssert.AreEquivalent(new[] { "a" }, referenced);
        }

        [Test]
        public void ACompleteScanThatFoundNothing_StillLeavesTheRetainedOrphan()
        {
            var referenced = AvatarHistoryCleanupPlanner.ReferencedAfterScan(
                new[] { "a", "b" }, positivelyFound: null, scanCompleted: true);

            CollectionAssert.IsEmpty(referenced);
            CollectionAssert.AreEquivalent(new[] { "b" }, Deleted(new[]
            {
                History("a", referenced: false, "2026-01-01T00:00:00Z"),
                History("b", referenced: false, "2025-01-01T00:00:00Z"),
            }));
        }

        [Test]
        public void TimestampOrder_IsWhatTheInventoryMustUseToPickAHistorysNewest()
        {
            Assert.Greater(
                AvatarHistoryCleanupPlanner.TimestampOrder("2026-09-04T01:00:00Z"),
                AvatarHistoryCleanupPlanner.TimestampOrder("2026-09-04T09:00:00+09:00"),
                "09:00+09:00 is 00:00Z, so it is the earlier instant even though it sorts later as text");

            Assert.AreEqual(
                System.DateTimeOffset.MinValue,
                AvatarHistoryCleanupPlanner.TimestampOrder("not a timestamp"));
            Assert.AreEqual(
                System.DateTimeOffset.MinValue,
                AvatarHistoryCleanupPlanner.TimestampOrder(null));
        }

        [Test]
        public void NullEntriesAreIgnored_AndANullListIsRejected()
        {
            var plan = AvatarHistoryCleanupPlanner.Plan(new[]
            {
                null,
                History("orphan", referenced: false, "2026-01-01T00:00:00Z"),
            });

            Assert.AreEqual(1, plan.Count);
            Assert.Throws<System.ArgumentNullException>(() => AvatarHistoryCleanupPlanner.Plan(null));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => AvatarHistoryCleanupPlanner.Plan(new List<AvatarHistoryInfo>(), keepOrphans: -1));
        }
    }
}
