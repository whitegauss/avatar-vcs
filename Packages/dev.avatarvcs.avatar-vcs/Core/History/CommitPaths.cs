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
