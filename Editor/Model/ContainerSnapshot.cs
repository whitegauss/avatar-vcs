using System;
using System.Collections.Generic;
using UnityEngine;

namespace AvatarVcs.Editor.Model
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
    }
}
