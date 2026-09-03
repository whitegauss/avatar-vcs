using AvatarVcs.Core.History;
using AvatarVcs.Core.Model;
using NUnit.Framework;

namespace AvatarVcs.Tests.Core
{
    /// <summary>
    /// Pure BranchConfig operations. Name-validation cases moved here from
    /// HistoryRobustnessTests (KAN-22 5-2); the rest is new coverage of head
    /// bookkeeping and the New Branch button gate.
    /// </summary>
    [Category("Core")]
    public class BranchConfigOpsTests
    {
        [Test]
        public void IsValidBranchName_RejectsUnsafeNames()
        {
            Assert.IsFalse(BranchConfigOps.IsValidBranchName(null));
            Assert.IsFalse(BranchConfigOps.IsValidBranchName(""));
            Assert.IsFalse(BranchConfigOps.IsValidBranchName("  padded  "));
            Assert.IsFalse(BranchConfigOps.IsValidBranchName(".hidden"));
            Assert.IsFalse(BranchConfigOps.IsValidBranchName("-flag-like"));
            Assert.IsFalse(BranchConfigOps.IsValidBranchName("has/slash"));
            Assert.IsFalse(BranchConfigOps.IsValidBranchName("has\\backslash"));
            Assert.IsFalse(BranchConfigOps.IsValidBranchName("has:colon"));
            Assert.IsFalse(BranchConfigOps.IsValidBranchName("has*star"));
            Assert.IsFalse(BranchConfigOps.IsValidBranchName("has\"quote"));
            Assert.IsFalse(BranchConfigOps.IsValidBranchName("has\tcontrol"));
        }

        [Test]
        public void IsValidBranchName_AcceptsSafeNames()
        {
            Assert.IsTrue(BranchConfigOps.IsValidBranchName("main"));
            Assert.IsTrue(BranchConfigOps.IsValidBranchName("hair-long"));
            Assert.IsTrue(BranchConfigOps.IsValidBranchName("outfit_v2"));
            Assert.IsTrue(BranchConfigOps.IsValidBranchName("髪ロング")); // Japanese is fine
        }

        [Test]
        public void SetHead_UpdatesExistingBranchOrAddsNew()
        {
            var config = new BranchConfig();
            config.branches.Add(new BranchEntry { name = "main", commitId = "c1" });

            BranchConfigOps.SetHead(config, "main", "c2");
            Assert.AreEqual("c2", BranchConfigOps.HeadOf(config, "main"));

            BranchConfigOps.SetHead(config, "feature", "c3");
            Assert.AreEqual("c3", BranchConfigOps.HeadOf(config, "feature"));
            Assert.AreEqual(2, config.branches.Count);
        }

        [Test]
        public void HeadOf_UnknownBranch_IsNull()
        {
            Assert.IsNull(BranchConfigOps.HeadOf(new BranchConfig(), "nope"));
        }

        [Test]
        public void BranchHeadedBy_NamesTheBlockingBranch_OrNull()
        {
            var config = new BranchConfig();
            config.branches.Add(new BranchEntry { name = "main", commitId = "c1" });
            config.branches.Add(new BranchEntry { name = "feature", commitId = "c2" });

            Assert.AreEqual("feature", BranchConfigOps.BranchHeadedBy(config, "c2"));
            Assert.IsNull(BranchConfigOps.BranchHeadedBy(config, "c9"));
        }

        [Test]
        public void CanCreate_RejectsInvalidOrDuplicate()
        {
            var config = new BranchConfig();
            config.branches.Add(new BranchEntry { name = "main", commitId = "c1" });

            Assert.IsFalse(BranchConfigOps.CanCreate(config, "bad/name"));
            Assert.IsFalse(BranchConfigOps.CanCreate(config, "main"), "duplicate");
            Assert.IsTrue(BranchConfigOps.CanCreate(config, "main-2"));
        }
    }
}
