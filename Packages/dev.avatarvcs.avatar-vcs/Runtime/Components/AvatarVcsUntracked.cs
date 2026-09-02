using UnityEngine;

namespace AvatarVcs.Runtime
{
    /// <summary>
    /// Excludes the GameObject it sits on, and its whole subtree, from
    /// avatarReferences capture -- even when an ancestor is a tracked target
    /// whose recursive walk would otherwise reach it (design doc 1.4). The
    /// default "Ensure Root" config marks the avatar root, so
    /// AvatarVcsTrackedReference's *absence* on a child stopped meaning
    /// anything; this is the explicit "don't version-control this outfit"
    /// opt-out. Add it via GameObject/AvatarVCS/Untrack Properties Here.
    ///
    /// Carries no guid (like AvatarVcsTrackedReference): it only needs to
    /// say "this subtree is opted out". "[AvatarVCS]" container subtrees are
    /// already excluded structurally and never need this.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class AvatarVcsUntracked : MonoBehaviour
    {
    }
}
