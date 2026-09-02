using System.Linq;
using AvatarVcs.Core.History;
using AvatarVcs.Core.Model;
using AvatarVcs.Core.Presentation;
using AvatarVcs.Editor.AvatarReferences;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using AvatarVcs.Editor.Operations;
using AvatarVcs.Runtime;
using UnityEngine;

namespace AvatarVcs.Editor.UI
{
    /// <summary>
    /// IAvatarGateway backed by ContainerManager / BranchManager /
    /// CheckoutOperation / the capture helpers. KAN-21 phase 4-4. The window
    /// keeps ownership of the avatar root GameObject and pushes it in here
    /// via <see cref="AvatarRoot"/> whenever the selection changes.
    /// </summary>
    public sealed class EditorAvatarGateway : IAvatarGateway
    {
        public GameObject AvatarRoot { get; set; }

        public string FindAvatarGuid()
        {
            var root = ContainerManager.FindRoot(AvatarRoot);
            return root != null ? root.GetComponent<AvatarVcsRoot>().AvatarGuid : null;
        }

        public Commit CaptureLiveState()
        {
            var configRoot = ContainerManager.FindRoot(AvatarRoot);
            var liveContainers = ContainerManager.GetContainers(configRoot)
                .Select(c => ContainerCapture.CaptureContainer(c, AvatarRoot.transform))
                .ToList();
            var (avatarReferences, materialSettings) = AvatarReferenceCollector.CollectFromTrackedTargets(AvatarRoot);
            return new Commit
            {
                containers = liveContainers,
                avatarReferences = avatarReferences,
                materialSettings = materialSettings,
            };
        }

        public Commit CommitCurrentState(string message) => BranchManager.Commit(AvatarRoot, message);

        public void CreateBranch(string name) => BranchManager.CreateBranch(AvatarRoot, name);

        public CheckoutResult SwitchBranch(string name) => BranchManager.SwitchBranch(AvatarRoot, name);

        public CheckoutResult RestoreToCommit(string commitId) => BranchManager.RestoreToCommit(AvatarRoot, commitId);

        public CheckoutResult CheckoutForCompare(Commit commit, bool takeAutoCommit, string sourceBranch, string autoCommitParentId) =>
            takeAutoCommit
                ? CheckoutOperation.Checkout(commit, AvatarRoot, sourceBranch, autoCommitParentId)
                : CheckoutOperation.CheckoutWithoutAutoCommit(commit, AvatarRoot);

        public void RegisterGuidRemap(string fromGuid, string toGuid) => GuidRemapper.AddMapping(fromGuid, toGuid);
    }
}
