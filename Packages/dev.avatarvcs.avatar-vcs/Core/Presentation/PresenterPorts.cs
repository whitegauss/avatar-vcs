using System.Collections.Generic;
using AvatarVcs.Core.History;
using AvatarVcs.Core.Model;

namespace AvatarVcs.Core.Presentation
{
    /// <summary>
    /// The seams AvatarVcsPresenter needs from the Editor side. Phase 4
    /// (KAN-21): the window keeps drawing and dispatching; everything with
    /// state or a decision in it moves behind these ports so it can be
    /// unit-tested against fakes with no scene, no AssetDatabase, no dialogs.
    /// </summary>
    public interface IHistoryStore
    {
        BranchConfig LoadConfig(string avatarGuid);
        CommitIndex LoadIndex(string avatarGuid);
        Commit LoadCommit(string avatarGuid, string commitId);
        void DeleteCommit(string avatarGuid, string commitId);

        /// <summary>Deletes each id that isn't a branch head; returns the ids it refused (still a head).</summary>
        List<string> DeleteCommits(string avatarGuid, IEnumerable<string> commitIds);

        /// <summary>
        /// Rewrites an existing commit in place. Only used to save an edited
        /// note: a commit's recorded state is immutable, its note is not.
        /// </summary>
        void SaveCommit(string avatarGuid, Commit commit);
    }

    /// <summary>
    /// Operations that touch the live avatar in the scene. The Editor
    /// implementation closes over the avatar root GameObject; the presenter
    /// only ever deals in the avatar's guid and the resulting data.
    /// </summary>
    public interface IAvatarGateway
    {
        /// <summary>The avatar's stored guid, or null if it has no [AvatarVCS] root yet.</summary>
        string FindAvatarGuid();

        /// <summary>The scene's current state in the same shape as a stored Commit (for diffing).</summary>
        Commit CaptureLiveState();

        /// <summary>Commits the scene's current state onto the current branch; returns the new commit.</summary>
        Commit CommitCurrentState(string message);

        void CreateBranch(string name);
        CheckoutResult SwitchBranch(string name);
        CheckoutResult RestoreToCommit(string commitId);

        /// <summary>
        /// Applies a specific commit for compare mode. takeAutoCommit picks
        /// between the safety-net checkout (CheckoutOperation.Checkout) and
        /// the no-auto-commit one; the auto-commit's parent/branch are only
        /// used when takeAutoCommit is true.
        /// </summary>
        CheckoutResult CheckoutForCompare(Commit commit, bool takeAutoCommit, string sourceBranch, string autoCommitParentId);

        void RegisterGuidRemap(string fromGuid, string toGuid);
    }

    /// <summary>Modal prompts. The Editor implementation wraps EditorUtility.DisplayDialog.</summary>
    public interface IUserPrompt
    {
        bool Confirm(string title, string body, string ok, string cancel);
        void Alert(string title, string body);
    }
}
