using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.Reflection
{
    /// <summary>
    /// Path and asset reference resolution. v1 design doc sections 3.1/3.2.
    /// </summary>
    public static class ReferenceResolver
    {
        public static string GetRelativePath(Transform target, Transform root)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (target == root) return string.Empty;

            var segments = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                segments.Add(current.name);
                current = current.parent;
            }

            if (current != root)
                throw new ArgumentException("target is not a descendant of root.", nameof(target));

            segments.Reverse();
            return string.Join("/", segments);
        }

        public static Transform ResolvePath(string path, Transform root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            return string.IsNullOrEmpty(path) ? root : root.Find(path);
        }

        /// <summary>
        /// Resolves an asset by GUID + localId, distinguishing sub-assets that
        /// share a GUID (e.g. multiple materials inside one FBX).
        /// </summary>
        public static Object ResolveAsset(string guid, long localId)
        {
            if (string.IsNullOrEmpty(guid)) return null;

            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath)) return null;

            foreach (var candidate in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out var candidateGuid, out long candidateLocalId)
                    && candidateGuid == guid && candidateLocalId == localId)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
