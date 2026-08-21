using System;
using AvatarVcs.Editor.Capture;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.Model;
using AvatarVcs.Editor.Reflection;
using AvatarVcs.Runtime;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.AvatarReferences
{
    /// <summary>
    /// Captures tracked avatar-side state (design doc 1.4.1/1.4.2) for a
    /// marked subtree: blend shape weights and material slot references on
    /// the target itself (name/GUID-resolved), plus generic field values for
    /// every other component on the target and its descendants (path-
    /// resolved, same mechanism containers use for their own root). Never
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
                // Every blend shape on a tracked target is recorded, including
                // ones currently at 0 -- a shape whose prefab/mesh default is
                // non-zero (e.g. an outfit shipping a shape pre-set to 100)
                // can legitimately be turned down to exactly 0 by the user,
                // and that explicit choice must round-trip through a commit
                // like any other value. (Design doc 1.4.2's "JSON absence
                // means not tracked" is about targets/paths never added to
                // avatarReferences at all, not about values within one that
                // already is.)
                for (var i = 0; i < mesh.blendShapeCount; i++)
                {
                    state.blendShapes.Add(new BlendShapeRef
                    {
                        name = mesh.GetBlendShapeName(i),
                        weight = skinnedRenderer.GetBlendShapeWeight(i),
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

            CaptureDescendantComponents(target, avatarRoot, state);

            return state;
        }

        private static void CaptureDescendantComponents(Transform target, Transform avatarRoot, AvatarReferenceState state)
        {
            var vcsRoot = ContainerManager.FindRoot(avatarRoot.gameObject)?.transform;

            // includeInactive:true + target itself is included by Unity's own
            // GetComponentsInChildren semantics -- deliberate: a tracked
            // avatar root's OWN other components (VRCAvatarDescriptor,
            // Animator, ...) must be captured too, not just its descendants.
            // Don't "fix" this into a Skip(1).
            foreach (var descendant in target.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                // [AvatarVCS] is destroy/regenerate-managed by containers;
                // must never be double-captured here. IsChildOf (not a name/
                // string compare) so this survives a manual rename of
                // "[AvatarVCS]" the same way ContainerManager.FindRoot itself
                // does.
                if (vcsRoot != null && descendant.IsChildOf(vcsRoot)) continue;

                foreach (var component in descendant.GetComponents<Component>())
                {
                    if (component == null) continue; // missing script
                    if (component is Transform) continue; // bone pose is never tracked here
                    if (component is AvatarVcsTrackedReference or AvatarVcsRoot or AvatarVcsContainer) continue;

                    var captured = ComponentCapturer.Capture(component, target, avatarRoot);
                    StripNarrowlyTrackedFields(component, captured);

                    if (captured.fields.Count == 0 && captured.assetRefs.Count == 0 && captured.sceneRefs.Count == 0)
                        continue; // nothing left worth recording after stripping

                    state.components.Add(captured);
                }
            }
        }

        /// <summary>
        /// m_BlendShapeWeights/m_Materials are already captured robustly (by
        /// name / by GUID with GuidRemapper support) via the narrow path
        /// above; capturing them again here as fragile index-based array
        /// entries would double-manage the same data. Scoped to `is
        /// Renderer` so it never touches an unrelated component that happens
        /// to share a field name.
        /// </summary>
        private static void StripNarrowlyTrackedFields(Component component, ComponentState captured)
        {
            if (component is not Renderer) return;

            captured.fields.RemoveAll(f => f.key.StartsWith("m_Materials") || f.key.StartsWith("m_BlendShapeWeights"));
            captured.assetRefs.RemoveAll(a => a.key.StartsWith("m_Materials"));
        }
    }
}
