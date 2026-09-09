using System.Collections.Generic;
using AvatarVcs.Core.Repair;
using NUnit.Framework;

namespace AvatarVcs.Tests.Core
{
    [Category("Core")]
    public class MarkerGuidClassifierTests
    {
        // Script guids as Unity generates them, taken from a real install of
        // the package back when it shipped without .meta files.
        private const string RootScript = "7452257372a7ab246a08439006804c43";
        private const string ContainerScript = "9b134eb6dcb359a4fa0a92ce61605055";
        private const string TrackedScript = "cedd2c4154039cc4d885355c3dbbd497";
        private const string UntrackedScript = "8e8f56864dfd9384ebff92c6f02a271a";
        private const string SomebodyElsesScript = "4ecd63eff4c8a9846b3b3b1e9dc70e1b";

        [Test]
        public void TheAvatarGuidFieldIdentifiesTheRoot()
        {
            var kinds = MarkerGuidClassifier.Classify(new List<MissingScriptGroup>
            {
                new MissingScriptGroup { scriptGuid = RootScript, anyBlockHasAvatarGuid = true, allBlocksUnderAvatar = true },
            });

            Assert.That(kinds[RootScript], Is.EqualTo(MarkerKind.Root));
        }

        [Test]
        public void TheContainerGuidFieldIdentifiesTheContainer()
        {
            var kinds = MarkerGuidClassifier.Classify(new List<MissingScriptGroup>
            {
                new MissingScriptGroup { scriptGuid = ContainerScript, anyBlockHasContainerGuid = true, allBlocksUnderAvatar = true },
            });

            Assert.That(kinds[ContainerScript], Is.EqualTo(MarkerKind.Container));
        }

        // A commit's avatarReferences[] entry exists only where a
        // AvatarVcsTrackedReference was, so a recorded path is proof.
        [Test]
        public void ARecordedTrackedPathIdentifiesTheTrackedMarker()
        {
            var kinds = MarkerGuidClassifier.Classify(new List<MissingScriptGroup>
            {
                new MissingScriptGroup { scriptGuid = TrackedScript, allBlocksUnderAvatar = true, anyBlockPathTracked = true },
            });

            Assert.That(kinds[TrackedScript], Is.EqualTo(MarkerKind.Tracked));
        }

        // Nothing on disk ever names an AvatarVcsUntracked -- keeping its
        // subtree out of commits is the entire point of it -- so it is only
        // reachable once the other field-less marker has been pinned down and
        // it is the only candidate left.
        [Test]
        public void TheRemainingFieldLessGroupIsUntrackedOnceTrackedIsIdentified()
        {
            var kinds = MarkerGuidClassifier.Classify(new List<MissingScriptGroup>
            {
                new MissingScriptGroup { scriptGuid = TrackedScript, allBlocksUnderAvatar = true, anyBlockPathTracked = true },
                new MissingScriptGroup { scriptGuid = UntrackedScript, allBlocksUnderAvatar = true },
            });

            Assert.That(kinds[TrackedScript], Is.EqualTo(MarkerKind.Tracked));
            Assert.That(kinds[UntrackedScript], Is.EqualTo(MarkerKind.Untracked));
        }

        [Test]
        public void WithNothingIdentifiedAsTrackedNoGuessIsMade()
        {
            var kinds = MarkerGuidClassifier.Classify(new List<MissingScriptGroup>
            {
                new MissingScriptGroup { scriptGuid = UntrackedScript, allBlocksUnderAvatar = true },
            });

            Assert.That(kinds[UntrackedScript], Is.EqualTo(MarkerKind.Unknown));
        }

        // Two candidates and one Tracked: which of the two is the Untracked
        // one is unknowable, and adding the wrong marker either starts
        // versioning an opted-out subtree or silently stops versioning a
        // tracked one. Both stay Unknown.
        [Test]
        public void TwoFieldLessCandidatesAreBothLeftAlone()
        {
            var kinds = MarkerGuidClassifier.Classify(new List<MissingScriptGroup>
            {
                new MissingScriptGroup { scriptGuid = TrackedScript, allBlocksUnderAvatar = true, anyBlockPathTracked = true },
                new MissingScriptGroup { scriptGuid = UntrackedScript, allBlocksUnderAvatar = true },
                new MissingScriptGroup { scriptGuid = SomebodyElsesScript, allBlocksUnderAvatar = true },
            });

            Assert.That(kinds[UntrackedScript], Is.EqualTo(MarkerKind.Unknown));
            Assert.That(kinds[SomebodyElsesScript], Is.EqualTo(MarkerKind.Unknown));
        }

        // Other packages break their own scripts. A missing script that isn't
        // inside an avatar is never one of ours, and must not be counted as
        // the last candidate either -- otherwise it would be "eliminated"
        // into an AvatarVcsUntracked on somebody else's component.
        [Test]
        public void AMissingScriptOutsideAnyAvatarIsNeverClassified()
        {
            var kinds = MarkerGuidClassifier.Classify(new List<MissingScriptGroup>
            {
                new MissingScriptGroup { scriptGuid = TrackedScript, allBlocksUnderAvatar = true, anyBlockPathTracked = true },
                new MissingScriptGroup { scriptGuid = SomebodyElsesScript, allBlocksUnderAvatar = false },
            });

            Assert.That(kinds[SomebodyElsesScript], Is.EqualTo(MarkerKind.Unknown));
        }

        [Test]
        public void NoGroupsIsNotAnError()
        {
            Assert.That(MarkerGuidClassifier.Classify(new List<MissingScriptGroup>()), Is.Empty);
            Assert.That(MarkerGuidClassifier.Classify(null), Is.Empty);
        }
    }
}
