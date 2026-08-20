using System;
using System.Collections.Generic;

namespace AvatarVcs.Editor.Model
{
    [Serializable]
    public class BlendShapeRef
    {
        public string name;
        public float weight;
    }

    [Serializable]
    public class MaterialRef
    {
        public int slot;
        public string guid;
    }

    /// <summary>
    /// Whitelisted avatar-body properties (design doc section 1.4). Unlike
    /// containers, applying this is overwrite-only: names absent from the JSON
    /// are left untouched.
    /// </summary>
    [Serializable]
    public class AvatarReferenceState
    {
        public string path;
        public List<BlendShapeRef> blendShapes = new();
        public List<MaterialRef> materials = new();
    }
}
