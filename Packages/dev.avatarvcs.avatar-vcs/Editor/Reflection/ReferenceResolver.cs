using System;
using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Core.Diagnostics;
using AvatarVcs.Core.Reflection;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.Reflection
{
    /// <summary>
    /// Path and asset reference resolution. v1 design doc sections 3.1/3.2.
    /// </summary>
    public static class ReferenceResolver
    {
        /// <summary>
        /// Logs a warning for every GameObject in root's subtree that has two
        /// or more children sharing a name. GetRelativePath joins names and
        /// ResolvePath is Transform.Find, so only the first same-named
        /// sibling is ever reachable -- anything captured against a later one
        /// (ObjectStateRef / SceneRef / ComponentState.path) is silently
        /// restored onto the first instead. Warn, don't block: same stance as
        /// ContainerCapture's non-prefab-child warning. A real fix (a sibling
        /// index in the path) is a schema change tracked separately (KAN-15).
        /// The warning goes to the caller's DiagnosticLog (KAN-20) so it
        /// reaches CheckoutResult.Diagnostics like every other capture warning.
        /// </summary>
        public static void WarnOnSameNameSiblings(Transform root, string context, DiagnosticLog log)
        {
            if (root == null) return;

            var queue = new Queue<Transform>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();

                var dupes = node.Cast<Transform>()
                    .GroupBy(c => c.name)
                    .Where(g => g.Count() > 1)
                    .ToList();
                if (dupes.Count > 0)
                    log.Warn($"[AvatarVCS] {context}: '{node.name}' has same-named children ("
                        + string.Join(", ", dupes.Select(g => $"'{g.Key}' x{g.Count()}")) + "). "
                        + "Path-based restore can't tell same-named siblings apart, so their tracked state may be "
                        + "restored onto the wrong one -- give them unique names.");

                foreach (Transform child in node)
                    queue.Enqueue(child);
            }
        }

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
