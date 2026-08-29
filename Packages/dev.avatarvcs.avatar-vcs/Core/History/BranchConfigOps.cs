using System.Linq;
using AvatarVcs.Core.Model;

namespace AvatarVcs.Core.History
{
    /// <summary>
    /// Pure operations over BranchConfig: lookup, head bookkeeping, and name
    /// validation shared by BranchManager (I/O side) and the UI. Design doc
    /// section 2.2: branches are just named pointers to commit ids.
    /// </summary>
    public static class BranchConfigOps
    {
        public static BranchEntry Find(BranchConfig config, string name) =>
            config.branches.FirstOrDefault(b => b.name == name);

        public static string HeadOf(BranchConfig config, string name) =>
            Find(config, name)?.commitId;

        public static string CurrentHead(BranchConfig config) =>
            HeadOf(config, config.currentBranch);

        public static void SetHead(BranchConfig config, string branch, string commitId)
        {
            var entry = Find(config, branch);
            if (entry != null)
                entry.commitId = commitId;
            else
                config.branches.Add(new BranchEntry { name = branch, commitId = commitId });
        }

        /// <summary>
        /// Name of the branch currently headed at commitId, or null if none
        /// -- used to decide whether deleting a commit must be blocked (it's
        /// still a branch head) and, if so, to name that branch in the error
        /// message.
        /// </summary>
        public static string BranchHeadedBy(BranchConfig config, string commitId) =>
            config.branches.FirstOrDefault(b => b.commitId == commitId)?.name;

        // Branch names aren't currently used as filesystem paths anywhere
        // (storage is keyed by avatarGuid/commitId), but restricting them
        // now avoids painting into a corner if that ever changes, and rules
        // out control characters and stray whitespace regardless.
        private static readonly char[] ForbiddenChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

        public static bool IsValidBranchName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name != name.Trim()) return false;
            if (name.StartsWith(".") || name.StartsWith("-")) return false;
            return name.All(c => !ForbiddenChars.Contains(c) && !char.IsControl(c));
        }

        /// <summary>
        /// The "valid and not already taken" gate for the UI's New Branch
        /// button -- a name can be well-formed per IsValidBranchName but
        /// still collide with an existing branch.
        /// </summary>
        public static bool CanCreate(BranchConfig config, string name) =>
            IsValidBranchName(name) && Find(config, name) == null;
    }
}
