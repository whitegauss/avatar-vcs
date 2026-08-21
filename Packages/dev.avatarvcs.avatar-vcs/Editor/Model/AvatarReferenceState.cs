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
    /// Tracked avatar-side state for one marked subtree (design doc section
    /// 1.4): BlendShape weights and material references on the target itself
    /// (name/GUID-resolved, tolerant of mesh/material updates), plus the
    /// existing field values of every other component on the target and its
    /// descendants (path-resolved, same shape as ContainerSnapshot.components).
    /// Unlike containers, applying this is overwrite-only: structure (objects/
    /// components absent from the JSON, or added since) is never touched.
    /// </summary>
    [Serializable]
    public class AvatarReferenceState
    {
        public string path;
        public List<BlendShapeRef> blendShapes = new();
        public List<MaterialRef> materials = new();
        public List<ComponentState> components = new();
    }
}
