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
        ///
        /// Convenience wrapper for restoring a single container in
        /// isolation (existing single-container callers, tests). A full
        /// checkout instead calls InstantiateContainerStructure for every
        /// container first, then ApplyContainerComponents for every
        /// container second (see CheckoutOperation.ApplyCommitToScene) --
        /// doing both passes per-container here would mean a component on
        /// one container's root that references an object inside a
        /// *different* container (not yet instantiated when this container's
        /// components are applied) fails to resolve, even though both
        /// containers exist by the end of the full checkout.
        /// </summary>
        public static GameObject InstantiateContainer(ContainerSnapshot snapshot, GameObject root)
        {
            var containerGo = InstantiateContainerStructure(snapshot, root);
            var avatarRoot = root.transform.parent != null ? root.transform.parent.gameObject : root;
            ApplyContainerComponents(snapshot, containerGo, avatarRoot);
            return containerGo;
        }

        /// <summary>
        /// First pass: destroys any existing same-id container and rebuilds
        /// its GameObject, transform, tag/layer/active state, marker, and
        /// prefab instances -- everything except snapshot.components, which
        /// ApplyContainerComponents applies as a separate second pass (see
        /// InstantiateContainer's doc comment for why the split matters).
        /// </summary>
        public static GameObject InstantiateContainerStructure(ContainerSnapshot snapshot, GameObject root)
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

            var tagWarning = GameObjectStateApplier.Apply(containerGo, snapshot.activeSelf, snapshot.tag, snapshot.layer,
                $"container '{snapshot.containerId}'", "Restore AvatarVCS Container");
            if (tagWarning != null) Debug.LogWarning(tagWarning);

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

            return containerGo;
        }

        /// <summary>
        /// Second pass: applies snapshot.components onto an already-built
        /// containerGo (see InstantiateContainerStructure). Split out so a
        /// full checkout can instantiate every container's structure before
        /// applying any container's components -- see InstantiateContainer's
        /// doc comment.
        /// </summary>
        public static void ApplyContainerComponents(ContainerSnapshot snapshot, GameObject containerGo, GameObject avatarRoot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (containerGo == null) throw new ArgumentNullException(nameof(containerGo));

            foreach (var componentState in snapshot.components)
            {
                var result = ComponentApplier.Apply(componentState, containerGo, avatarRoot, createIfMissing: true);
                if (!result.IsSuccess)
                    Debug.LogWarning($"[AvatarVCS] Failed to restore component '{componentState.type}' on '{snapshot.containerId}': {result.Message}");
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
