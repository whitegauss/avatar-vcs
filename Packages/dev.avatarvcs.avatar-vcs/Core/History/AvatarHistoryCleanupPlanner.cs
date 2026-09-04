using System;
using System.Collections.Generic;
using System.Linq;

namespace AvatarVcs.Core.History
{
    /// <summary>
    /// One avatar's stored history, as the cleanup planner sees it.
    /// </summary>
    public class AvatarHistoryInfo
    {
        public string avatarGuid;

        /// <summary>
        /// Whether an AvatarVcsRoot carrying this guid still exists anywhere
        /// in the project. The Editor side answers this by looking at loaded
        /// objects AND by searching every scene/prefab file on disk -- an
        /// avatar sitting in a scene nobody has open is still very much in
        /// use, and treating it as orphaned would delete real history.
        /// </summary>
        public bool isReferenced;

        /// <summary>ISO-8601, from the newest index entry. Null when the history has no commits.</summary>
        public string newestCommitTimestamp;

        public int commitCount;
        public long byteSize;
    }

    /// <summary>
    /// Decides which stored avatar histories may be deleted.
    ///
    /// An avatar's identity is the guid minted onto its AvatarVcsRoot marker,
    /// so deleting the "[AvatarVCS]" root and setting it up again mints a new
    /// one and strands the old history under
    /// ProjectSettings/AvatarVcs/avatars/. Nothing ever removed those, so they
    /// accumulate for the lifetime of the project.
    ///
    /// Deliberately pure: the policy is the part worth pinning in tests, and
    /// it decides deletions of version-control history, so it must be
    /// inspectable without an Editor.
    /// </summary>
    public static class AvatarHistoryCleanupPlanner
    {
        public const int DefaultKeepOrphans = 1;

        public class Decision
        {
            public AvatarHistoryInfo history;
            public bool delete;
            /// <summary>Why, in words, for the confirmation dialog.</summary>
            public string reason;
        }

        /// <summary>
        /// A decision per history, in the order given. Referenced histories are
        /// never deleted. Among the unreferenced ones the newest keepOrphans
        /// are kept as a safety net for the case the user actually meant to
        /// keep working with that avatar and removed its root by accident;
        /// the rest are deleted.
        /// </summary>
        public static List<Decision> Plan(IEnumerable<AvatarHistoryInfo> histories, int keepOrphans = DefaultKeepOrphans)
        {
            if (histories == null) throw new ArgumentNullException(nameof(histories));
            if (keepOrphans < 0) throw new ArgumentOutOfRangeException(nameof(keepOrphans), "keepOrphans must not be negative.");

            var all = histories.Where(h => h != null).ToList();

            // Newest first. A history with no commits (or an unparseable
            // timestamp) sorts last: it is the least costly thing to lose, so
            // it should not be what occupies the one retained slot.
            var keptOrphans = all
                .Where(h => !h.isReferenced)
                .OrderByDescending(h => SortKey(h.newestCommitTimestamp))
                .Take(keepOrphans)
                .ToHashSet();

            return all.Select(h => new Decision
            {
                history = h,
                delete = !h.isReferenced && !keptOrphans.Contains(h),
                reason = h.isReferenced
                    ? "still used by an avatar in this project"
                    : keptOrphans.Contains(h)
                        ? "kept: most recent history with no avatar left, in case its root was removed by mistake"
                        : "no avatar in this project carries this id any more",
            }).ToList();
        }

        // DateTimeOffset, not string ordering: timestamps are written with
        // DateTime.UtcNow.ToString("o") today, but a hand-edited or older
        // commit can carry an offset ("+09:00") that sorts wrong as text.
        private static DateTimeOffset SortKey(string timestamp) =>
            DateTimeOffset.TryParse(timestamp, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : DateTimeOffset.MinValue;
    }
}
