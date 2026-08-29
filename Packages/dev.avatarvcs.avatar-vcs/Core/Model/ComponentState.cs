using System;
using System.Collections.Generic;

namespace AvatarVcs.Core.Model
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
        // Which same-type component on the target GameObject this is (0 =
        // first), for GameObjects carrying more than one component of the
        // same type (e.g. multiple VRCPhysBone/constraints). Defaults to 0,
        // which also matches every commit written before this field existed
        // -- "the first one" was the only thing GetComponent(type) could
        // ever have meant.
        public int componentIndex;
        public List<FieldValue> fields = new();
        public List<AssetRef> assetRefs = new();
        public List<SceneRef> sceneRefs = new();
    }
}
