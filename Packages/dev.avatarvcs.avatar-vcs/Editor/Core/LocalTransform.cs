using UnityEngine;

namespace AvatarVcs.Editor.Core
{
    /// <summary>
    /// Small shared helper for the one transform operation this package does
    /// over and over: park a freshly created or freshly reparented object at
    /// its parent's origin.
    /// </summary>
    public static class LocalTransform
    {
        /// <summary>
        /// Identity local position, rotation and scale.
        ///
        /// This is what "the object sits exactly where its parent does" means
        /// for the [AvatarVCS] root, a container, an adopted wrapper and a
        /// regenerated prefab instance alike -- all four are structural
        /// holders whose own placement is never tracked, so it is always the
        /// full triple, never a subset. Writing it out at each site invited
        /// exactly one of the three being forgotten.
        /// </summary>
        public static void Reset(Transform transform)
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }
    }
}
