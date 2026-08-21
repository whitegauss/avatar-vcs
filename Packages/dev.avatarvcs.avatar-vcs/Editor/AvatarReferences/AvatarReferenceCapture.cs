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

                // Hierarchy show/hide (GameObject.activeSelf) lives on the
                // GameObject itself, not on any one Component's
                // SerializedObject, so it needs its own capture step
                // alongside the per-component walk below.
                state.activeStates.Add(new ActiveStateRef
                {
                    path = ReferenceResolver.GetRelativePath(descendant, target),
                    activeSelf = descendant.gameObject.activeSelf,
                });

                foreach (var component in descendant.GetComponents<Component>())
                {
                    if (component == null) continue; // missing script
                    if (component is AvatarVcsTrackedReference or AvatarVcsRoot or AvatarVcsContainer) continue;

                    if (component is Transform transform)
                    {
                        // Bone pose is never tracked -- a bone has no
                        // recoverable content of its own to fall back on if
                        // something went wrong (design doc 6.1: asset
                        // content isn't preserved), unlike a prefab
                        // instance placed under a tracked target (e.g. an
                        // accessory attached directly to an Armature bone,
                        // bypassing container management entirely), whose
                        // position IS worth restoring since the prefab
                        // itself is safely GUID-recoverable. Scoped to
                        // actual descendants, not target itself: target is
                        // the deliberately chosen tracking anchor (Body/
                        // Armature/avatar root), and forcing its own scene
                        // placement back on every checkout even when it
                        // happens to be a prefab instance would be an
                        // unrelated, surprising side effect.
                        if (descendant == target) continue;
                        if (ContainerManager.GetPrefabGuid(descendant.gameObject) == null) continue;

                        var transformState = CapturePrefabInstanceTransform(transform, target);
                        if (transformState.fields.Count > 0) state.components.Add(transformState);
                        continue;
                    }

                    var captured = ComponentCapturer.Capture(component, target, avatarRoot);
                    StripNarrowlyTrackedFields(component, captured);

                    if (captured.fields.Count == 0 && captured.assetRefs.Count == 0 && captured.sceneRefs.Count == 0)
                        continue; // nothing left worth recording after stripping

                    state.components.Add(captured);
                }
            }
        }

        /// <summary>
        /// Captures only position/rotation/scale, by property name, not the
        /// full generic ComponentCapturer walk -- Transform's SerializedObject
        /// also exposes structural properties (m_Father, m_Children,
        /// m_RootOrder) that must never be touched, so this deliberately
        /// stays a small explicit allowlist rather than "everything the
        /// walk finds".
        /// </summary>
        private static ComponentState CapturePrefabInstanceTransform(Transform transform, Transform containerRoot)
        {
            var state = new ComponentState
            {
                path = ReferenceResolver.GetRelativePath(transform, containerRoot),
                type = typeof(Transform).FullName,
            };

            var so = new SerializedObject(transform);
            foreach (var propertyName in new[] { "m_LocalPosition", "m_LocalRotation", "m_LocalScale" })
            {
                var prop = so.FindProperty(propertyName);
                if (prop != null && FieldCodec.TryEncode(prop, out var value, out var type))
                    state.fields.Add(new FieldValue { key = propertyName, value = value, type = type });
            }

            return state;
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
