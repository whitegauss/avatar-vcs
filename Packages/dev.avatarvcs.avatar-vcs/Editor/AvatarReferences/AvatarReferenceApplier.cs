using System;
using System.Linq;
using AvatarVcs.Editor.Apply;
using AvatarVcs.Editor.History;
using AvatarVcs.Core.Model;
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

        // blendShapes/materials are grouped by BlendShapeRef.path /
        // MaterialRef.path -- "" (or a pre-KAN-10 commit's absent key,
        // which JsonUtility leaves null) means the tracked target itself,
        // otherwise a descendant renderer. Each group resolves its own
        // renderer under target.
        private static void ApplyBlendShapes(AvatarReferenceState state, Transform target)
        {
            if (state.blendShapes.Count == 0) return;

            foreach (var group in state.blendShapes.GroupBy(b => b.path ?? string.Empty))
            {
                var where = JoinPath(state.path, group.Key);
                var node = ReferenceResolver.ResolvePath(group.Key, target);
                var renderer = node == null ? null : node.GetComponent<SkinnedMeshRenderer>();
                if (renderer == null || renderer.sharedMesh == null)
                {
                    Debug.LogWarning($"[AvatarVCS] '{where}' has no SkinnedMeshRenderer with a mesh; blend shapes skipped.");
                    continue;
                }

                var mesh = renderer.sharedMesh;
                var recorded = false;

                foreach (var shape in group)
                {
                    var index = mesh.GetBlendShapeIndex(shape.name);
                    if (index < 0)
                    {
                        Debug.LogWarning($"[AvatarVCS] Blend shape '{shape.name}' not found on '{where}'; skipped.");
                        continue;
                    }
                    // Only write when the value actually differs (KAN-72):
                    // capture records every shape on every renderer, so an
                    // unconditional write would stamp a prefab-instance
                    // override on every renderer on every checkout.
                    if (Mathf.Approximately(renderer.GetBlendShapeWeight(index), shape.weight)) continue;

                    if (!recorded)
                    {
                        Undo.RecordObject(renderer, "AvatarVCS Apply BlendShapes");
                        recorded = true;
                    }
                    renderer.SetBlendShapeWeight(index, shape.weight);
                }
            }
        }

        private static void ApplyMaterials(AvatarReferenceState state, Transform target)
        {
            if (state.materials.Count == 0) return;

            foreach (var group in state.materials.GroupBy(m => m.path ?? string.Empty))
            {
                var where = JoinPath(state.path, group.Key);
                var node = ReferenceResolver.ResolvePath(group.Key, target);
                var renderer = node == null ? null : node.GetComponent<Renderer>();
                if (renderer == null)
                {
                    Debug.LogWarning($"[AvatarVCS] '{where}' has no Renderer; material references skipped.");
                    continue;
                }

                var materials = renderer.sharedMaterials;
                var changed = false;

                foreach (var materialRef in group)
                {
                    if (materialRef.slot < 0 || materialRef.slot >= materials.Length)
                    {
                        Debug.LogWarning($"[AvatarVCS] Material slot {materialRef.slot} out of range on '{where}'; skipped.");
                        continue;
                    }

                    var assetPath = AssetDatabase.GUIDToAssetPath(GuidRemapper.Resolve(materialRef.guid));
                    var material = string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                    if (material == null)
                    {
                        Debug.LogWarning($"[AvatarVCS] Material GUID '{materialRef.guid}' could not be resolved for slot {materialRef.slot} on '{where}'; skipped.");
                        continue;
                    }

                    // Only reassign when the slot actually points somewhere
                    // else (KAN-72): capture records every slot on every
                    // renderer, so an unconditional write would dirty the
                    // renderer / add a prefab override on every checkout.
                    if (materials[materialRef.slot] == material) continue;

                    materials[materialRef.slot] = material;
                    changed = true;
                }

                if (changed)
                {
                    Undo.RecordObject(renderer, "AvatarVCS Apply Materials");
                    renderer.sharedMaterials = materials;
                }
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

        // "Body" + "" -> "Body"; "" + "Chest" -> "Chest"; "Body" + "Chest"
        // -> "Body/Chest". Only used to build a readable location for a
        // warning, never for resolution.
        private static string JoinPath(string a, string b) =>
            string.IsNullOrEmpty(a) ? b ?? string.Empty
            : string.IsNullOrEmpty(b) ? a
            : $"{a}/{b}";

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

                var tagWarning = GameObjectStateApplier.Apply(descendant.gameObject, objectState.activeSelf, objectState.tag, objectState.layer,
                    $"'{state.path}/{objectState.path}'", "AvatarVCS Apply Object State");
                if (tagWarning != null) Debug.LogWarning(tagWarning);
            }
        }
    }
}
