using System;
using System.Collections.Generic;

namespace AvatarVcs.Core.Model
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
        /// <summary>
        /// Highest index schemaVersion this build understands. Same rule as
        /// Commit.CurrentSchemaVersion, and it exists for the same reason:
        /// JsonUtility drops keys it has no field for, so a build that reads
        /// a newer file and writes it back silently deletes whatever the
        /// newer build put there. CommitStore refuses to overwrite a file
        /// that says a higher number rather than quietly truncating it.
        ///
        /// A file with no schemaVersion key keeps this initializer, so every
        /// index written before the field existed reads as current -- which
        /// is right: those files hold exactly the fields this build knows.
        /// </summary>
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public List<CommitIndexEntry> entries = new();
    }
}
