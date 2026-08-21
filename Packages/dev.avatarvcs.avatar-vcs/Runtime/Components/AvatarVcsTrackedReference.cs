using UnityEngine;

namespace AvatarVcs.Runtime
{
    /// <summary>
    /// Marker for an avatar-side subtree (e.g. "Body", "Armature", or the
    /// avatar root itself) whose state should be captured into a commit's
    /// avatarReferences (design doc 1.4): BlendShape weights and material
    /// slots on the marked target itself, plus every other component's
    /// existing field values on the target and its descendants -- except
    /// anything under "[AvatarVCS]", which is destroy/regenerate-managed by
    /// containers instead and is always excluded from this recursive walk.
    /// Unlike AvatarVcsContainer, this carries no guid: design doc 1.4.2
    /// resolves entries by hierarchy path and BlendShapes by name, so the
    /// marker only needs to say "this subtree is opted in" -- add it via
    /// GameObject/AvatarVCS/Track Properties Here.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class AvatarVcsTrackedReference : MonoBehaviour
    {
    }
}
