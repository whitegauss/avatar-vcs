namespace AvatarVcs.Runtime
{
    /// <summary>
    /// Shared shape check for this tool's own generated guids (always
    /// Guid.NewGuid().ToString("N")), used by AvatarVcsRoot.AssignGuid and
    /// AvatarVcsContainer.AssignGuid. Catches a bug in this tool's own guid
    /// generation early; does NOT by itself protect against a malicious
    /// value baked directly into a shared scene/prefab's serialized data --
    /// Unity deserializes [SerializeField] fields directly, bypassing
    /// AssignGuid entirely. CommitStore validates the same shape again at
    /// the point avatarGuid is actually turned into a filesystem path,
    /// which is the real defense boundary for that one.
    /// </summary>
    internal static class GuidShape
    {
        public static bool IsValid(string value)
        {
            if (value == null || value.Length != 32) return false;
            foreach (var c in value)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }
            return true;
        }
    }
}
