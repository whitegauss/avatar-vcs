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
        public static UnityEngine.Object ResolveAsset(string guid, long localId)
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

        /// <summary>
        /// Resolves a scene reference (design doc's distinction from asset
        /// references, section 3.1) given the target GameObject's Transform
        /// and the referenced object's full type name recorded at capture
        /// time: GameObject/Transform resolve directly, anything else via
        /// GetComponent(TypeResolver.Resolve(type)).
        /// </summary>
        public static UnityEngine.Object ResolveSceneReference(Transform target, string typeFullName)
        {
            if (target == null || string.IsNullOrEmpty(typeFullName)) return null;

            if (typeFullName == typeof(GameObject).FullName) return target.gameObject;
            if (typeFullName == typeof(Transform).FullName) return target;

            var type = TypeResolver.Resolve(typeFullName);
            return type == null ? null : target.GetComponent(type);
        }
    }
}
