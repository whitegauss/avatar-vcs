namespace AvatarVcs.Core.History
{
    /// <summary>
    /// File-based storage layout for commits, the per-avatar index, and
    /// branch config (design doc section 4): ProjectSettings/AvatarVcs/
    /// avatars/{avatarGuid}/{config,index}.json and commits/{commitId}.json.
    /// Every avatarGuid-taking method here routes through AvatarDir, which is
    /// where the CommitIdentifier shape check actually happens -- an invalid
    /// avatarGuid never reaches string interpolation into a path.
    /// </summary>
    public static class CommitPaths
    {
        public const string AvatarsRoot = "ProjectSettings/AvatarVcs/avatars";
        public const string GuidRemapFile = "ProjectSettings/AvatarVcs/guid-remapping.json";

        /// <summary>
        /// Where the orphan sweep moves a history instead of deleting it (see
        /// CommitStore.TrashAvatarHistory). Deliberately a sibling of
        /// AvatarsRoot rather than a folder inside it: everything that decides
        /// what exists -- AvatarHistoryInventory's directory listing above all
        /// -- enumerates AvatarsRoot, and a trashed history must not look like
        /// a stored one just because it is still on disk.
        /// </summary>
        public const string TrashRoot = "ProjectSettings/AvatarVcs/trash";

        /// <summary>
        /// stamp is what keeps repeated trashings of the same avatar apart;
        /// the guid stays the leading path segment so a user can find the
        /// history they want back by the id the log named.
        /// </summary>
        public static string TrashDir(string avatarGuid, string stamp)
        {
            CommitIdentifier.EnsureValid(avatarGuid, nameof(avatarGuid));
            return $"{TrashRoot}/{avatarGuid}-{stamp}";
        }

        public static string AvatarDir(string avatarGuid)
        {
            CommitIdentifier.EnsureValid(avatarGuid, nameof(avatarGuid));
            return $"{AvatarsRoot}/{avatarGuid}";
        }

        public static string CommitFile(string avatarGuid, string commitId)
        {
            CommitIdentifier.EnsureValid(commitId, nameof(commitId));
            return $"{AvatarDir(avatarGuid)}/commits/{commitId}.json";
        }

        public static string IndexFile(string avatarGuid) =>
            $"{AvatarDir(avatarGuid)}/index.json";

        public static string ConfigFile(string avatarGuid) =>
            $"{AvatarDir(avatarGuid)}/config.json";
    }
}
