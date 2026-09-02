using System;
using System.Collections.Generic;
using UnityEngine;

namespace AvatarVcs.Core.Model
{
    /// <summary>
    /// In-memory snapshot of a single container's identity, prefab references,
    /// and transform. Mirrors the "containers[]" entries of the commit JSON
    /// in design doc section 2.1 (persistence itself is phase 3 scope).
    /// </summary>
    [Serializable]
    public class ContainerSnapshot
    {
        public string containerId;
        public string containerGuid;
        public List<string> prefabGuids = new();
        public Vector3 localPosition;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 localScale = Vector3.one;
        // The container root's Inspector tag (e.g. "EditorOnly" to keep it
        // out of an avatar upload, or the default "Untagged"). A freshly
        // created GameObject is always "Untagged", so this is also the
        // correct default for commits recorded before this field existed.
        public string tag = "Untagged";
        // The Inspector's active checkbox and layer dropdown. Same
        // backward-compatibility reasoning as tag: these default to a fresh
        // GameObject's actual defaults (active, "Default" layer = 0), so
        // commits recorded before these fields existed still restore
        // correctly.
        public bool activeSelf = true;
        public int layer;
        public List<ComponentState> components = new();

        // Property versioning for the container's regenerated subtree
        // (KAN-70). A container is destroy-and-regenerate from prefabGuids,
        // so per-object BlendShape weights / material slots / active-tag-layer
        // that the user tweaked inside a prefab instance would otherwise be
        // lost on checkout. These re-apply them on top after regeneration
        // (same "regenerate then overwrite" order as MaterialSettings).
        // Paths are relative to the container root. Absent in older commits
        // -> empty -> nothing re-applied -> pre-KAN-70 behaviour.
        public List<BlendShapeRef> blendShapes = new();
        public List<MaterialRef> materials = new();
        public List<ObjectStateRef> objectStates = new();
    }
}
