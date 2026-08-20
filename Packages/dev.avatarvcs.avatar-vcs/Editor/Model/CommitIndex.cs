using System;
using System.Collections.Generic;

namespace AvatarVcs.Editor.Model
{
    /// <summary>
    /// Lightweight metadata for one commit, for listing history without
    /// loading every commit's full body. Design doc section 4.
    /// </summary>
    [Serializable]
    public class CommitIndexEntry
    {
        public string commitId;
        public string parentCommitId;
        public string branch;
        public string message;
        public string timestamp;
    }

    [Serializable]
    public class CommitIndex
    {
        public List<CommitIndexEntry> entries = new();
    }
}
