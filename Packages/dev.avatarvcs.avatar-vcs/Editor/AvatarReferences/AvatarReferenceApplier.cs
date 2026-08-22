using System;
using AvatarVcs.Editor.Apply;
using AvatarVcs.Editor.History;
using AvatarVcs.Editor.Model;
using AvatarVcs.Editor.Reflection;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.AvatarReferences
{
    /// <summary>
    /// Applies tracked avatar-side state. Overwrite-only: structure (objects/
    /// components absent from the state, or added since) is left untouched
    /// (design doc 1.4.2), unlike containers which are destroyed and
    /// regenerated.
    /// </summary>
    public static class AvatarReferenceApplier
    {
        public static void Apply(AvatarReferenceState state, Transform avatarRoot)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (avatarRoot == null) throw new ArgumentNullException(nameof(avatarRoot));

            var target = ReferenceResolver.ResolvePath(state.path, avatarRoot);
            if (target == null)
            {
                Debug.LogWarning($"[AvatarVCS] avatarReferences path '{state.path}' could not be resolved; skipped.");
                return;
            }

            ApplyBlendShapes(state, target);
            ApplyMaterials(state, target);
            ApplyComponents(state, target, avatarRoot);
            ApplyObjectStates(state, target);
        }

        private static void ApplyBlendShapes(AvatarReferenceState state, Transform target)
        {
            if (state.blendShapes.Count == 0) return;

            var renderer = target.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null || renderer.sharedMesh == null)
            {
                Debug.LogWarning($"[AvatarVCS] '{state.path}' has no SkinnedMeshRenderer with a mesh; blend shapes skipped.");
                return;
            }

            var mesh = renderer.sharedMesh;
            Undo.RecordObject(renderer, "AvatarVCS Apply BlendShapes");

            foreach (var shape in state.blendShapes)
            {
                var index = mesh.GetBlendShapeIndex(shape.name);
                if (index < 0)
                {
                    Debug.LogWarning($"[AvatarVCS] Blend shape '{shape.name}' not found on '{state.path}'; skipped.");
                    continue;
                }
                renderer.SetBlendShapeWeight(index, shape.weight);
            }
        }

        private static void ApplyMaterials(AvatarReferenceState state, Transform target)
        {
            if (state.materials.Count == 0) return;

            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
            {
                Debug.LogWarning($"[AvatarVCS] '{state.path}' has no Renderer; material references skipped.");
                return;
            }

            var materials = renderer.sharedMaterials;
            var changed = false;

            foreach (var materialRef in state.materials)
            {
                if (materialRef.slot < 0 || materialRef.slot >= materials.Length)
                {
                    Debug.LogWarning($"[AvatarVCS] Material slot {materialRef.slot} out of range on '{state.path}'; skipped.");
                    continue;
                }

                var assetPath = AssetDatabase.GUIDToAssetPath(GuidRemapper.Resolve(materialRef.guid));
                var material = string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                if (material == null)
                {
                    Debug.LogWarning($"[AvatarVCS] Material GUID '{materialRef.guid}' could not be resolved for slot {materialRef.slot} on '{state.path}'; skipped.");
                    continue;
                }

                materials[materialRef.slot] = material;
                changed = true;
            }

            if (changed)
            {
                Undo.RecordObject(renderer, "AvatarVCS Apply Materials");
                renderer.sharedMaterials = materials;
            }
        }

        // createIfMissing: false -- this is the deliberate "overwrite-only,
        // never create/destroy" zone (design doc 1.4.2). A component that
        // existed at commit time but is gone from the live target now is a
        // divergence the user made on purpose; re-adding it has no clean
        // semantics here (unlike containers, which rebuild the whole
        // subtree from scratch every checkout).
        private static void ApplyComponents(AvatarReferenceState state, Transform target, Transform avatarRoot)
        {
            foreach (var componentState in state.components)
            {
                var result = ComponentApplier.Apply(componentState, target.gameObject, avatarRoot.gameObject, createIfMissing: false);
                if (!result.IsSuccess)
                    Debug.LogWarning($"[AvatarVCS] Failed to restore component '{componentState.type}' on '{state.path}': {result.Message}");
            }
        }

        private static void ApplyObjectStates(AvatarReferenceState state, Transform target)
        {
            foreach (var objectState in state.objectStates)
            {
                var descendant = ReferenceResolver.ResolvePath(objectState.path, target);
                if (descendant == null)
                {
                    Debug.LogWarning($"[AvatarVCS] avatarReferences objectState path '{objectState.path}' under '{state.path}' could not be resolved; skipped.");
                    continue;
                }

                var go = descendant.gameObject;
                if (go.activeSelf != objectState.activeSelf)
                {
                    Undo.RecordObject(go, "AvatarVCS Apply Object State");
                    go.SetActive(objectState.activeSelf);
                }

                if (go.layer != objectState.layer)
                {
                    Undo.RecordObject(go, "AvatarVCS Apply Object State");
                    go.layer = objectState.layer;
                }

                ApplyTag(go, objectState, state.path);
            }
        }

        /// <summary>
        /// GameObject.tag throws if the tag isn't defined in this project's
        /// Tag Manager (e.g. a custom tag recorded in a commit made in a
        /// different project) -- same guard ContainerRestore.ApplyTag uses.
        /// </summary>
        private static void ApplyTag(GameObject go, ObjectStateRef objectState, string avatarReferencePath)
        {
            if (string.IsNullOrEmpty(objectState.tag) || objectState.tag == go.tag) return;

            try
            {
                Undo.RecordObject(go, "AvatarVCS Apply Object State");
                go.tag = objectState.tag;
            }
            catch (UnityException)
            {
                Debug.LogWarning($"[AvatarVCS] Tag '{objectState.tag}' recorded for '{avatarReferencePath}/{objectState.path}' "
                    + $"is not defined in this project's Tag Manager; left as '{go.tag}'.");
            }
        }
    }
}
