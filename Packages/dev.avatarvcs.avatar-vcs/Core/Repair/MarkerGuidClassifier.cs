using System.Collections.Generic;
using System.Linq;

namespace AvatarVcs.Core.Repair
{
    public enum MarkerKind
    {
        /// <summary>Not identifiable from the evidence available; left alone.</summary>
        Unknown,
        Root,
        Container,
        Tracked,
        Untracked,
    }

    /// <summary>
    /// What the editor side could work out about every block sharing one
    /// missing script guid. All blocks with the same guid are the same
    /// component type, which is what makes a guid (rather than a block) the
    /// unit of classification: evidence found on any one of them settles the
    /// whole group.
    /// </summary>
    public sealed class MissingScriptGroup
    {
        public string scriptGuid;

        /// <summary>Some block carries an avatarGuid field: AvatarVcsRoot.</summary>
        public bool anyBlockHasAvatarGuid;

        /// <summary>Some block carries a containerGuid field: AvatarVcsContainer.</summary>
        public bool anyBlockHasContainerGuid;

        /// <summary>
        /// Every block sits on a GameObject inside an avatar that has AvatarVCS
        /// history. False for a missing script belonging to some other broken
        /// package, which is the case this exists to keep our hands off.
        /// </summary>
        public bool allBlocksUnderAvatar;

        /// <summary>
        /// Some block's path is one this avatar's commit recorded as a tracked
        /// target (a commit's avatarReferences[].path). Only a
        /// AvatarVcsTrackedReference ever produces one of those.
        /// </summary>
        public bool anyBlockPathTracked;
    }

    /// <summary>
    /// Decides which marker component each missing script guid used to be.
    ///
    /// Three of the four are positively identifiable: AvatarVcsRoot and
    /// AvatarVcsContainer by the field they serialize, and
    /// AvatarVcsTrackedReference by the fact that a commit recorded the object
    /// it sits on as a tracked target. AvatarVcsUntracked has neither -- it has
    /// no fields, and its whole purpose is to keep its subtree *out* of
    /// commits, so nothing on disk names it.
    ///
    /// So it is only ever reached by elimination, and then under conditions
    /// tight enough that guessing wrong is unlikely: exactly two field-less
    /// groups inside the avatar, one of them positively Tracked. Anything less
    /// certain stays Unknown and is reported rather than repaired -- adding
    /// AvatarVcsTrackedReference where AvatarVcsUntracked belongs would start
    /// versioning a subtree the user deliberately opted out of, and the
    /// reverse would silently stop versioning one they wanted.
    /// </summary>
    public static class MarkerGuidClassifier
    {
        public static Dictionary<string, MarkerKind> Classify(IReadOnlyList<MissingScriptGroup> groups)
        {
            var result = new Dictionary<string, MarkerKind>();
            if (groups == null) return result;

            foreach (var group in groups)
            {
                result[group.scriptGuid] =
                    group.anyBlockHasAvatarGuid ? MarkerKind.Root
                    : group.anyBlockHasContainerGuid ? MarkerKind.Container
                    : group.allBlocksUnderAvatar && group.anyBlockPathTracked ? MarkerKind.Tracked
                    : MarkerKind.Unknown;
            }

            var undecided = groups
                .Where(g => result[g.scriptGuid] == MarkerKind.Unknown && g.allBlocksUnderAvatar)
                .ToList();

            if (undecided.Count == 1 && result.ContainsValue(MarkerKind.Tracked))
                result[undecided[0].scriptGuid] = MarkerKind.Untracked;

            return result;
        }
    }
}
