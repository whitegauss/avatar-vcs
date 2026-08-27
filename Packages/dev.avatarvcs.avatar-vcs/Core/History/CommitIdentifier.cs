using System;

namespace AvatarVcs.Core.History
{
    /// <summary>
    /// Shape validation for avatarGuid/commitId values before they're
    /// interpolated into filesystem paths (see CommitPaths). Split out of
    /// CommitStore so the actual path-traversal defense boundary can be
    /// exercised directly, without a scene or real files.
    /// </summary>
    public static class CommitIdentifier
    {
        /// <summary>
        /// Both avatarGuid and commitId are always Guid.NewGuid().ToString("N")
        /// in normal operation, but they're interpolated directly into
        /// filesystem paths below -- avatarGuid comes off a SerializeField
        /// that Unity deserializes directly (a hand-edited or shared scene/
        /// prefab could contain anything), and commitId is re-read from
        /// commit JSON on disk during checkout. This is the actual defense
        /// boundary against a value like "../../../outside" escaping
        /// ProjectSettings/AvatarVcs/ -- not AvatarVcsRoot.AssignGuid, which
        /// only guards this tool's own generation path.
        /// </summary>
        public static bool IsValidShape(string value)
        {
            if (value == null || value.Length != 32) return false;
            foreach (var c in value)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }
            return true;
        }

        public static void EnsureValid(string value, string paramName)
        {
            if (!IsValidShape(value))
                throw new ArgumentException(
                    $"{paramName} must be a 32-character lowercase hex string (as produced by Guid.NewGuid().ToString(\"N\")); got '{value}'.",
                    paramName);
        }
    }
}
