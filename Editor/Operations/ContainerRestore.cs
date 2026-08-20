using System;
using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Editor.Model;
using AvatarVcs.Runtime;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.Operations
{
    /// <summary>
    /// Restores a container from a snapshot by destroying any existing container
    /// with the same id and regenerating it from scratch. This destroy-then-recreate
    /// approach is the core idempotency claim of design doc section 1.2 / 3.2.
    /// </summary>
    public static class ContainerRestore
    {
        public static GameObject InstantiateContainer(ContainerSnapshot snapshot, GameObject root)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (root == null) throw new ArgumentNullException(nameof(root));

            var existing = root.transform.Find(snapshot.containerId);
            if (existing != null)
                Undo.DestroyObjectImmediate(existing.gameObject);

            var containerGo = new GameObject(snapshot.containerId);
            Undo.RegisterCreatedObjectUndo(containerGo, "Restore AvatarVCS Container");
            Undo.SetTransformParent(containerGo.transform, root.transform, "Restore AvatarVCS Container");
            containerGo.transform.localPosition = snapshot.localPosition;
            containerGo.transform.localRotation = snapshot.localRotation;
            containerGo.transform.localScale = snapshot.localScale;

            var marker = Undo.AddComponent<AvatarVcsContainer>(containerGo);
            marker.AssignGuid(snapshot.containerGuid);

            foreach (var prefabGuid in snapshot.prefabGuids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(prefabGuid);
                if (string.IsNullOrEmpty(assetPath))
                    throw new InvalidOperationException(
                        $"Prefab with GUID '{prefabGuid}' could not be resolved. Call HasMissingPrefabs before InstantiateContainer.");

                var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, containerGo.transform);
                Undo.RegisterCreatedObjectUndo(instance, "Restore AvatarVCS Container");
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
            }

            return containerGo;
        }

        /// <summary>
        /// Pre-flight check to run before InstantiateContainer, so a missing prefab
        /// is caught before the existing container is destroyed (design doc 3.2).
        /// </summary>
        public static bool HasMissingPrefabs(ContainerSnapshot snapshot, out List<string> missingGuids)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            missingGuids = snapshot.prefabGuids
                .Where(guid => string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)))
                .ToList();
            return missingGuids.Count > 0;
        }
    }
}
