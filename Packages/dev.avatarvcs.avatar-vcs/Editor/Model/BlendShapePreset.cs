using System;
using System.Collections.Generic;

namespace AvatarVcs.Editor.Model
{
    /// <summary>
    /// A standalone, shareable snapshot of one mesh's BlendShape weights
    /// (issue #58): unlike AvatarReferenceState, this is never written into
    /// a commit and carries no avatarGuid/path -- it's meant to be exported
    /// to a file and imported onto a *different* mesh (e.g. a shape-key
    /// pack sold to another creator), matched purely by BlendShape name.
    /// meshName is informational only (shown in the import dialog), never
    /// used to decide whether import is allowed.
    /// </summary>
    [Serializable]
    public class BlendShapePreset
    {
        public string meshName;
        public List<BlendShapeRef> blendShapes = new();
    }
}
