using System;

namespace AvatarVcs.Core.Model
{
    /// <summary>
    /// A reference to a live scene object (GameObject/Transform/Component),
    /// as opposed to AssetRef which points at a persistent asset. Resolved by
    /// path relative to the avatar root rather than the container root: the
    /// referenced object is often outside the container entirely (e.g. a bone
    /// on the avatar's own Armature that a MergeArmature-style component
    /// points at).
    /// </summary>
    [Serializable]
    public class SceneRef
    {
        public string key;
        public string path;
        public string type; // full type name of the referenced object (GameObject/Transform/Component)
    }
}
