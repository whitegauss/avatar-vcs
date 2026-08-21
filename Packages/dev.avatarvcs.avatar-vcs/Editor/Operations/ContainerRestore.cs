using System;
using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Editor.Apply;
using AvatarVcs.Editor.History;
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
        /// <summary>
        /// root is the "[AvatarVCS]" container root (not the avatar itself);
        /// its parent is used to resolve scene-reference fields that may
        /// point outside the container, per the EnsureRoot invariant that
        /// root is always parented directly under the avatar.
        /// </summary>
        public static GameObject InstantiateContainer(ContainerSnapshot snapshot, GameObject root)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (root == null) throw new ArgumentNullException(nameof(root));

            var avatarRoot = root.transform.parent != null ? root.transform.parent.gameObject : root;

            var existing = root.transform.Find(snapshot.containerId);
            if (existing != null)
                Undo.DestroyObjectImmediate(existing.gameObject);

            var containerGo = new GameObject(snapshot.containerId);
            Undo.RegisterCreatedObjectUndo(containerGo, "Restore AvatarVCS Container");
            Undo.SetTransformParent(containerGo.transform, root.transform, "Restore AvatarVCS Container");
            containerGo.transform.localPosition = snapshot.localPosition;
            containerGo.transform.localRotation = snapshot.localRotation;
            containerGo.transform.localScale = snapshot.localScale;
            ApplyTag(containerGo, snapshot);
            containerGo.layer = snapshot.layer;
            containerGo.SetActive(snapshot.activeSelf);

            var marker = Undo.AddComponent<AvatarVcsContainer>(containerGo);
            marker.AssignGuid(snapshot.containerGuid);

            foreach (var prefabGuid in snapshot.prefabGuids)
            {
                if (!TryResolvePrefabPath(prefabGuid, out var assetPath))
                    throw new InvalidOperationException(
                        $"Prefab with GUID '{prefabGuid}' could not be resolved. Call HasMissingPrefabs before InstantiateContainer.");

                var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, containerGo.transform);
                Undo.RegisterCreatedObjectUndo(instance, "Restore AvatarVCS Container");
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
            }

            foreach (var componentState in snapshot.components)
            {
                var result = ComponentApplier.Apply(componentState, containerGo, avatarRoot, createIfMissing: true);
                if (!result.IsSuccess)
                    Debug.LogWarning($"[AvatarVCS] Failed to restore component '{componentState.type}' on '{snapshot.containerId}': {result.Message}");
            }

            return containerGo;
        }

        /// <summary>
        /// GameObject.tag throws if the tag isn't defined in this project's
        /// Tag Manager (e.g. a custom tag recorded in a commit made in a
        /// different project). Warn and leave the default "Untagged" rather
        /// than aborting the whole restore over it.
        /// </summary>
        private static void ApplyTag(GameObject containerGo, ContainerSnapshot snapshot)
        {
            if (string.IsNullOrEmpty(snapshot.tag) || snapshot.tag == containerGo.tag) return;

            try
            {
                containerGo.tag = snapshot.tag;
            }
            catch (UnityException)
            {
                Debug.LogWarning($"[AvatarVCS] Tag '{snapshot.tag}' recorded for container '{snapshot.containerId}' "
                    + $"is not defined in this project's Tag Manager; left as '{containerGo.tag}'.");
            }
        }

        /// <summary>
        /// Pre-flight check to run before InstantiateContainer, so a missing prefab
        /// is caught before the existing container is destroyed (design doc 3.2).
        /// </summary>
        public static bool HasMissingPrefabs(ContainerSnapshot snapshot, out List<string> missingGuids)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            missingGuids = snapshot.prefabGuids
                .Where(guid => !TryResolvePrefabPath(guid, out _))
                .ToList();
            return missingGuids.Count > 0;
        }

        /// <summary>
        /// AssetDatabase.GUIDToAssetPath can keep returning a just-deleted
        /// asset's path for a while after AssetDatabase.DeleteAsset succeeds
        /// (confirmed empirically, not just a same-frame timing issue), so a
        /// non-empty path alone doesn't prove the asset still exists -- also
        /// confirm it actually loads. Consults GuidRemapper first (design doc
        /// 6.4): a re-imported prefab's new GUID is transparently substituted.
        /// </summary>
        private static bool TryResolvePrefabPath(string guid, out string assetPath)
        {
            assetPath = AssetDatabase.GUIDToAssetPath(GuidRemapper.Resolve(guid));
            if (string.IsNullOrEmpty(assetPath)) return false;
            return AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) != null;
        }
    }
}
