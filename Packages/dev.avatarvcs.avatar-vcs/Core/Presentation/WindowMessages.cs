using System.Collections.Generic;
using System.Linq;

namespace AvatarVcs.Core.Presentation
{
    /// <summary>
    /// Dialog bodies and help strings for the AvatarVCS window, kept out of
    /// the IMGUI code (KAN-21 phase 4-3) so AvatarVcsPresenter's tests can
    /// assert on the exact wording without going through EditorUtility.
    /// Text is unchanged from the pre-refactor inline strings.
    /// </summary>
    public static class WindowMessages
    {
        public const string InvalidBranchName =
            "Invalid or duplicate branch name. Avoid / \\ : * ? \" < > | and leading/trailing whitespace or a leading '.' or '-'.";

        public const string DeleteCommitTitle = "Delete Commit";
        public const string DeleteCommitBody =
            "Delete this commit and its generated assets (e.g. duplicate materials)? This cannot be undone.";

        public const string CantDeleteHeadTitle = "Can't Delete";
        public const string CantDeleteHeadBody =
            "This commit is the current branch's head. Checkout a different commit first to move the head away, then delete it.";

        public const string DeleteFailedTitle = "Delete Failed";
        public const string DeleteBlockedSuffix =
            "\n\nSwitch to that branch, checkout a different commit on it, then come back and delete this one.";

        public const string BulkDeleteTitle = "Delete Selected Commits";
        public const string SomeNotDeletedTitle = "Some Commits Could Not Be Deleted";
        public const string BulkDeleteBlockedSuffix =
            "\n\nSwitch to the relevant branch, checkout a different commit on it, then come back and delete it.";

        public const string CheckoutFailedTitle = "Checkout Failed";
        public const string AssetVersionsChangedTitle = "Asset Versions Changed";

        public static string BulkDeleteBody(int count) =>
            $"Delete {count} commit(s) and their generated assets (e.g. duplicate materials)? This cannot be undone.";

        public static string BlockedByHead(string commitMessage) => $"{commitMessage}: is the head of its branch.";

        public static string SomeNotDeletedBody(IEnumerable<string> blockedMessages) =>
            string.Join("\n\n", blockedMessages) + BulkDeleteBlockedSuffix;

        public static string AssetVersionsChangedBody(IEnumerable<string> versionWarnings) =>
            "Checkout succeeded, but some referenced assets have changed since this commit was recorded "
            + "(the result may look different):\n\n" + string.Join("\n", versionWarnings);
    }
}
