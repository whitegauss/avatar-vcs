using System;
using AvatarVcs.Editor.Model;
using AvatarVcs.Editor.Reflection;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.AvatarReferences
{
    /// <summary>
    /// Applies whitelisted avatar-body properties. Overwrite-only: names absent
    /// from the state are left untouched (design doc 1.4.2), unlike containers
    /// which are destroyed and regenerated.
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

                var assetPath = AssetDatabase.GUIDToAssetPath(materialRef.guid);
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
    }
}
