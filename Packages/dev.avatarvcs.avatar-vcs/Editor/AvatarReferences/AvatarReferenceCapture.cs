using System;
using AvatarVcs.Editor.Model;
using AvatarVcs.Editor.Reflection;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.AvatarReferences
{
    /// <summary>
    /// Captures whitelisted avatar-body properties (design doc 1.4.1/1.4.2):
    /// blend shape weights and material slot references. Read-only, never
    /// mutates the target.
    /// </summary>
    public static class AvatarReferenceCapture
    {
        public static AvatarReferenceState Capture(Transform target, Transform avatarRoot)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (avatarRoot == null) throw new ArgumentNullException(nameof(avatarRoot));

            var state = new AvatarReferenceState
            {
                path = ReferenceResolver.GetRelativePath(target, avatarRoot),
            };

            var skinnedRenderer = target.GetComponent<SkinnedMeshRenderer>();
            if (skinnedRenderer != null && skinnedRenderer.sharedMesh != null)
            {
                var mesh = skinnedRenderer.sharedMesh;
                for (var i = 0; i < mesh.blendShapeCount; i++)
                {
                    // Only non-default weights are recorded; JSON absence means
                    // "not tracked", not "reset to zero" (design doc 1.4.2).
                    var weight = skinnedRenderer.GetBlendShapeWeight(i);
                    if (weight == 0f) continue;

                    state.blendShapes.Add(new BlendShapeRef
                    {
                        name = mesh.GetBlendShapeName(i),
                        weight = weight,
                    });
                }
            }

            var renderer = target.GetComponent<Renderer>();
            if (renderer != null)
            {
                var materials = renderer.sharedMaterials;
                for (var slot = 0; slot < materials.Length; slot++)
                {
                    var material = materials[slot];
                    if (material == null) continue;

                    var assetPath = AssetDatabase.GetAssetPath(material);
                    var guid = string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.AssetPathToGUID(assetPath);
                    if (string.IsNullOrEmpty(guid)) continue;

                    state.materials.Add(new MaterialRef { slot = slot, guid = guid });
                }
            }

            return state;
        }
    }
}
