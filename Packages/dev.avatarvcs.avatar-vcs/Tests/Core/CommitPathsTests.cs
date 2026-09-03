using System;
using AvatarVcs.Core.History;
using NUnit.Framework;

namespace AvatarVcs.Tests.Core
{
    /// <summary>
    /// Storage layout paths, and the guarantee that an invalid guid never
    /// makes it into one.
    /// </summary>
    [Category("Core")]
    public class CommitPathsTests
    {
        private const string Guid = "0123456789abcdef0123456789abcdef";
        private const string CommitId = "fedcba9876543210fedcba9876543210";

        [Test]
        public void Paths_ForValidIds_MatchTheDocumentedLayout()
        {
            Assert.AreEqual($"{CommitPaths.AvatarsRoot}/{Guid}", CommitPaths.AvatarDir(Guid));
            Assert.AreEqual($"{CommitPaths.AvatarsRoot}/{Guid}/index.json", CommitPaths.IndexFile(Guid));
            Assert.AreEqual($"{CommitPaths.AvatarsRoot}/{Guid}/config.json", CommitPaths.ConfigFile(Guid));
            Assert.AreEqual($"{CommitPaths.AvatarsRoot}/{Guid}/commits/{CommitId}.json",
                CommitPaths.CommitFile(Guid, CommitId));
        }

        [Test]
        public void AvatarDir_RejectsAnInvalidAvatarGuid()
        {
            Assert.Throws<ArgumentException>(() => CommitPaths.AvatarDir("../escape"));
            Assert.Throws<ArgumentException>(() => CommitPaths.IndexFile("../escape"));
            Assert.Throws<ArgumentException>(() => CommitPaths.ConfigFile("../escape"));
        }

        [Test]
        public void CommitFile_RejectsAnInvalidCommitIdEvenWithAValidAvatarGuid()
        {
            Assert.Throws<ArgumentException>(() => CommitPaths.CommitFile(Guid, "../escape"));
        }
    }
}
