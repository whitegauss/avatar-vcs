using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Core.Model;

namespace AvatarVcs.Core.History
{
    /// <summary>
    /// One requested-but-undeletable commit, and the branch that blocks it
    /// (still its head). Not a readonly struct: its fields are set via
    /// object initializer, and C# requires every field of a readonly struct
    /// to be individually marked readonly for that to compile.
    /// </summary>
    public struct BlockedCommit
    {
        public string CommitId;
        public string BranchName;
    }

    /// <summary>
    /// The result of planning a commit deletion (single or batch): which
    /// requested commits are blocked (still a branch head, force not set),
    /// which will actually be deleted, and which generated-asset guids have
    /// no surviving referrer and should be deleted along with them.
    /// </summary>
    public sealed class CommitDeletionPlan
    {
        public IReadOnlyList<BlockedCommit> Blocked { get; }
        public IReadOnlyList<string> CommitsToDelete { get; }
        public IReadOnlyList<string> AssetGuidsToDelete { get; }

        public CommitDeletionPlan(
            IReadOnlyList<BlockedCommit> blocked,
            IReadOnlyList<string> commitsToDelete,
            IReadOnlyList<string> assetGuidsToDelete)
        {
            Blocked = blocked;
            CommitsToDelete = commitsToDelete;
            AssetGuidsToDelete = assetGuidsToDelete;
        }
    }

    /// <summary>
    /// Plans deletion of one or more commits: which are blocked (still a
    /// branch head, unless force), and which generated assets (design doc
    /// section 4/1.4.3 -- duplicate materials created while checking a
    /// commit out) have no surviving referrer and should be deleted with
    /// them. Pure: the caller (CommitStore) loads every commit referenced by
    /// index.entries up front and hands them in as loadedCommits, and
    /// carries out the actual file/AssetDatabase deletion the plan
    /// describes.
    /// </summary>
    public static class CommitDeletionPlanner
    {
        public static CommitDeletionPlan Plan(
            BranchConfig config,
            IReadOnlyDictionary<string, Commit> loadedCommits,
            IEnumerable<string> requestedIds,
            bool force)
        {
            var validRequested = requestedIds.Where(CommitIdentifier.IsValidShape).Distinct().ToList();

            var blocked = new List<BlockedCommit>();
            var toDelete = new List<string>();
            foreach (var commitId in validRequested)
            {
                var branchName = force ? null : BranchConfigOps.BranchHeadedBy(config, commitId);
                if (branchName != null)
                {
                    blocked.Add(new BlockedCommit { CommitId = commitId, BranchName = branchName });
                    continue;
                }
                toDelete.Add(commitId);
            }

            var toDeleteSet = new HashSet<string>(toDelete);

            // Every generated-asset guid still referenced by a commit that
            // will SURVIVE this batch (not just "any other commit right
            // now", since two commits sharing a guid could both be in the
            // same batch -- neither survives, so the asset has no more
            // referrers and should go too).
            var stillReferenced = loadedCommits
                .Where(kv => kv.Value != null && !toDeleteSet.Contains(kv.Key))
                .SelectMany(kv => kv.Value.generatedAssets)
                .ToHashSet();

            var assetGuidsToDelete = new List<string>();
            var seenGuids = new HashSet<string>();
            foreach (var commitId in toDelete)
            {
                if (!loadedCommits.TryGetValue(commitId, out var commit) || commit == null) continue;

                foreach (var guid in commit.generatedAssets)
                {
                    if (stillReferenced.Contains(guid)) continue;
                    if (seenGuids.Add(guid))
                        assetGuidsToDelete.Add(guid);
                }
            }

            return new CommitDeletionPlan(blocked, toDelete, assetGuidsToDelete);
        }
    }
}
