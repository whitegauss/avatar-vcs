using UnityEngine;

namespace AvatarVcs.Runtime
{
    /// <summary>
    /// Marker for an avatar-body target (e.g. "Body") whose BlendShape
    /// weights and material references should be captured into a commit's
    /// avatarReferences (design doc 1.4). Unlike AvatarVcsContainer, this
    /// carries no guid: design doc 1.4.2 resolves entries by hierarchy path
    /// and BlendShapes by name, so the marker only needs to say "this
    /// transform is opted in" -- add it via GameObject/AvatarVCS/Track Body
    /// Properties Here.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class AvatarVcsTrackedReference : MonoBehaviour
    {
    }
}
