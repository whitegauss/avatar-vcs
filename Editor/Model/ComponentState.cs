using System;
using System.Collections.Generic;

namespace AvatarVcs.Editor.Model
{
    /// <summary>
    /// Captured state of a single Component, obtained via SerializedObject.
    /// path is relative to the container root (empty = container root itself).
    /// Design doc section 2.1 / v1 section 2.2.
    /// </summary>
    [Serializable]
    public class ComponentState
    {
        public string path;
        public string type;
        public List<FieldValue> fields = new();
        public List<AssetRef> assetRefs = new();
    }
}
