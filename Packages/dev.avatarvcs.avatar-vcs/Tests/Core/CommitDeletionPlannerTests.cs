using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Core.History;
using AvatarVcs.Core.Model;
using NUnit.Framework;

namespace AvatarVcs.Tests.Core
{
    /// <summary>
    /// The shared plan behind DeleteCommit and DeleteCommits: what's blocked
    /// (still a branch head), and which generated assets have no surviving
    /// referrer -- including the two-commits-in-one-batch-share-an-asset case.
    /// </summary>
    [Category("Core")]
    public class CommitDeletionPlannerTests
    {
        private const string A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private const string C = "cccccccccccccccccccccccccccccccc";

        private static BranchConfig ConfigWithHead(string branch, string commitId)
        {
            var config = new BranchConfig();
            config.branches.Add(new BranchEntry { name = branch, commitId = commitId });
            return config;
        }

        private static Commit CommitWithAssets(string id, params string[] generated) =>
            new() { commitId = id, generatedAssets = generated.ToList() };

        [Test]
        public void Plan_BlocksACommitThatIsStillABranchHead_UnlessForce()
        {
            var config = ConfigWithHead("main", A);
            var loaded = new Dictionary<string, Commit> { [A] = CommitWithAssets(A) };

            var blocked = CommitDeletionPlanner.Plan(config, loaded, new[] { A }, force: false);
            Assert.AreEqual(1, blocked.Blocked.Count);
            Assert.AreEqual("main", blocked.Blocked[0].BranchName);
            Assert.IsEmpty(blocked.CommitsToDelete);

            var forced = CommitDeletionPlanner.Plan(config, loaded, new[] { A }, force: true);
            Assert.IsEmpty(forced.Blocked);
            CollectionAssert.AreEqual(new[] { A }, forced.CommitsToDelete);
        }

        [Test]
        public void Plan_KeepsAnAssetStillReferencedByASurvivingCommit()
        {
            var config = ConfigWithHead("main", C);
            var loaded = new Dictionary<string, Commit>
            {
                [A] = CommitWithAssets(A, "shared-mat"),
                [C] = CommitWithAssets(C, "shared-mat"), // survivor still uses it
            };

            var plan = CommitDeletionPlanner.Plan(config, loaded, new[] { A }, force: false);

            CollectionAssert.AreEqual(new[] { A }, plan.CommitsToDelete);
            Assert.IsEmpty(plan.AssetGuidsToDelete, "the asset still has a referrer that survives");
        }

        [Test]
        public void Plan_DeletesAnAssetTwoCommitsInTheSameBatchShareWhenNeitherSurvives()
        {
            var config = ConfigWithHead("main", C);
            var loaded = new Dictionary<string, Commit>
            {
                [A] = CommitWithAssets(A, "shared-mat"),
                [B] = CommitWithAssets(B, "shared-mat"),
                [C] = CommitWithAssets(C), // survivor doesn't reference it
            };

            var plan = CommitDeletionPlanner.Plan(config, loaded, new[] { A, B }, force: false);

            CollectionAssert.AreEquivalent(new[] { A, B }, plan.CommitsToDelete);
            CollectionAssert.AreEqual(new[] { "shared-mat" }, plan.AssetGuidsToDelete,
                "no surviving commit references it, so it goes; listed once, not per-commit");
        }

        [Test]
        public void Plan_IgnoresMalformedRequestedIdsAndMissingLoadedCommits()
        {
            var config = new BranchConfig();
            var loaded = new Dictionary<string, Commit> { [A] = null }; // corrupt/unreadable entry

            var plan = CommitDeletionPlanner.Plan(config, loaded, new[] { "../bad", A, A }, force: false);

            CollectionAssert.AreEqual(new[] { A }, plan.CommitsToDelete, "invalid id dropped, valid id deduped");
            Assert.IsEmpty(plan.AssetGuidsToDelete, "a null loaded commit contributes no asset guids");
        }
    }
}
