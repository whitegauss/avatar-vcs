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
    }
}
