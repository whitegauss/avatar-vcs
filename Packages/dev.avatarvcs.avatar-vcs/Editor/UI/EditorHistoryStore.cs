using System.Collections.Generic;
using AvatarVcs.Core.History;
using AvatarVcs.Core.Model;
using AvatarVcs.Core.Presentation;
using AvatarVcs.Editor.History;

namespace AvatarVcs.Editor.UI
{
    /// <summary>
    /// IHistoryStore backed by CommitStore. KAN-21 phase 4-4.
    /// </summary>
    public sealed class EditorHistoryStore : IHistoryStore
    {
        public BranchConfig LoadConfig(string avatarGuid) => CommitStore.LoadConfig(avatarGuid);

        public CommitIndex LoadIndex(string avatarGuid) => CommitStore.LoadIndex(avatarGuid);

        public Commit LoadCommit(string avatarGuid, string commitId) => CommitStore.LoadCommit(avatarGuid, commitId);

        public void DeleteCommit(string avatarGuid, string commitId) => CommitStore.DeleteCommit(avatarGuid, commitId);

        public List<string> DeleteCommits(string avatarGuid, IEnumerable<string> commitIds) =>
            CommitStore.DeleteCommits(avatarGuid, commitIds);

        public void SaveCommit(string avatarGuid, Commit commit) => CommitStore.SaveCommit(avatarGuid, commit);
    }
}
