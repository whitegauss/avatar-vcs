using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Core.Model;

namespace AvatarVcs.Core.History
{
    /// <summary>
    /// Pure operations over CommitIndex: the upsert CommitStore.SaveCommit
    /// does on every save, the batch removal DeleteCommit(s) does, and the
    /// newest-first ordering the history UI lists commits in.
    /// </summary>
    public static class CommitIndexOps
    {
        public static void Upsert(CommitIndex index, CommitIndexEntry entry)
        {
            index.entries.RemoveAll(e => e.commitId == entry.commitId);
            index.entries.Add(entry);
        }

        public static void Remove(CommitIndex index, ISet<string> commitIds)
        {
            index.entries.RemoveAll(e => commitIds.Contains(e.commitId));
        }

        public static List<CommitIndexEntry> NewestFirst(CommitIndex index) =>
            index.entries.OrderByDescending(e => e.timestamp).ToList();

        public static CommitIndexEntry EntryFor(CommitIndex index, string commitId) =>
            index.entries.FirstOrDefault(e => e.commitId == commitId);

        /// <summary>
        /// Builds an index straight from the commits themselves.
        ///
        /// index.json is a cache: every field in it is copied from a commit
        /// file, and the commit files are the history. That makes losing it
        /// recoverable, which matters because losing it is the one failure
        /// that makes an avatar look like it has no history at all -- and
        /// because the alternative, writing a fresh one-entry index over the
        /// unreadable file, would turn a recoverable problem into a permanent
        /// one (CommitStore.LoadIndex).
        /// </summary>
        public static CommitIndex RebuildFrom(IEnumerable<Commit> commits)
        {
            var index = new CommitIndex();
            if (commits == null) return index;

            foreach (var commit in commits)
            {
                if (commit == null || string.IsNullOrEmpty(commit.commitId)) continue;

                Upsert(index, new CommitIndexEntry
                {
                    commitId = commit.commitId,
                    parentCommitId = commit.parentCommitId,
                    branch = commit.branch,
                    message = commit.message,
                    timestamp = commit.timestamp,
                });
            }

            return index;
        }
    }
}
