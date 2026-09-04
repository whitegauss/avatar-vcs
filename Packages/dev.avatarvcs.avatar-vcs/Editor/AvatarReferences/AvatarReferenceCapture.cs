using System;
using System.Collections.Generic;
using AvatarVcs.Core.Diagnostics;
using AvatarVcs.Core.MaterialSettings;
using AvatarVcs.Editor.Capture;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.Diagnostics;
using AvatarVcs.Editor.MaterialSettings;
using AvatarVcs.Core.Model;
using AvatarVcs.Editor.Reflection;
using AvatarVcs.Runtime;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.AvatarReferences
{
    /// <summary>
    /// Captures tracked avatar-side state (design doc 1.4.1/1.4.2) for a
    /// marked subtree: blend shape weights and material slot references for
    /// every SkinnedMeshRenderer/Renderer in the subtree (name/GUID-resolved,
    /// path-tagged), plus generic field values for every other component on
    /// the target and its descendants (path-resolved, same mechanism
    /// containers use for their own root). Never mutates the target.
    /// </summary>
    public static class AvatarReferenceCapture
    {
        public static AvatarReferenceState Capture(Transform target, Transform avatarRoot, DiagnosticLog log = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (avatarRoot == null) throw new ArgumentNullException(nameof(avatarRoot));

            // KAN-20: a caller mid-operation (a commit collecting every
            // tracked target) passes its own DiagnosticLog; a direct caller
            // (tests) passes none, so make one and flush it here.
            var ownsLog = log == null;
            log ??= new DiagnosticLog();
            try
            {
                var state = new AvatarReferenceState
                {
                    path = ReferenceResolver.GetRelativePath(target, avatarRoot),
                };

                CaptureDescendantComponents(target, avatarRoot, state, log);

                return state;
            }
            finally
            {
                if (ownsLog) UnityDiagnosticSink.Flush(log);
            }
        }

        /// <summary>
        /// Blend shape weights and material slot references for one renderer,
        /// tagged with relPath, appended to the given lists. Shared by the
        /// tracked-subtree walk here and ContainerCapture (KAN-70): the
        /// default "Ensure Root" config tracks the avatar root, so the
        /// renderers that actually matter (Body, accessories, ...) are always
        /// descendants, never the walk's starting node.
        /// </summary>
        internal static void CaptureRenderersInto(Transform node, string relPath,
            List<BlendShapeRef> blendShapes, List<MaterialRef> materials)
        {
            var skinnedRenderer = node.GetComponent<SkinnedMeshRenderer>();
            if (skinnedRenderer != null && skinnedRenderer.sharedMesh != null)
            {
                var mesh = skinnedRenderer.sharedMesh;
                // Every blend shape is recorded, including ones currently at
                // 0 -- a shape whose prefab/mesh default is non-zero (e.g. an
                // outfit shipping a shape pre-set to 100) can legitimately be
                // turned down to exactly 0 by the user, and that explicit
                // choice must round-trip through a commit like any other
                // value. (Design doc 1.4.2's "JSON absence means not tracked"
                // is about targets/paths never added to avatarReferences at
                // all, not about values within one that already is.)
                for (var i = 0; i < mesh.blendShapeCount; i++)
                {
                    blendShapes.Add(new BlendShapeRef
                    {
                        path = relPath,
                        name = mesh.GetBlendShapeName(i),
                        weight = skinnedRenderer.GetBlendShapeWeight(i),
                    });
                }
            }

            var renderer = node.GetComponent<Renderer>();
            if (renderer != null)
            {
                var sharedMats = renderer.sharedMaterials;
                for (var slot = 0; slot < sharedMats.Length; slot++)
                {
                    var material = sharedMats[slot];
                    if (material == null) continue;

                    var assetPath = AssetDatabase.GetAssetPath(material);
                    var guid = string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.AssetPathToGUID(assetPath);
                    if (string.IsNullOrEmpty(guid)) continue;

                    materials.Add(new MaterialRef { path = relPath, slot = slot, guid = guid });
                }
            }
        }

        /// <summary>
        /// Shader property values (lilToon/Poiyomi/MToon Color/Float) for every
        /// supported-shader slot on one renderer, tagged with relPath, appended
        /// to the given list.
        ///
        /// Shared by AvatarReferenceCollector (Track Properties targets) and
        /// ContainerCapture (a container's regenerated prefab instances) --
        /// they had a copy each and only ContainerCapture's guarded
        /// material.shader, so a material whose shader asset had been deleted
        /// took down the whole commit through the other one (KAN-78).
        ///
        /// A material that isn't a saved asset, and any shader outside the
        /// supported set, are skipped silently on purpose: materialSettings is
        /// a best-effort bonus on top of material *reference* tracking, which
        /// already covers every slot regardless of shader.
        /// </summary>
        internal static void CaptureMaterialSettingsInto(Transform node, string relPath, List<MaterialSettingsState> into)
        {
            var renderer = node.GetComponent<Renderer>();
            if (renderer == null) return;

            var sharedMats = renderer.sharedMaterials;
            for (var slot = 0; slot < sharedMats.Length; slot++)
            {
                var material = sharedMats[slot];
                // shader can be null for a material whose shader asset was
                // deleted or failed to import -- dereferencing it here used to
                // abort BranchManager.Commit outright.
                if (material == null || material.shader == null) continue;
                if (!ShaderPropertyMap.IsSupported(material.shader.name)) continue;

                try
                {
                    into.Add(MaterialSettingsCapture.Capture(material, material.shader.name, relPath, slot));
                }
                catch (InvalidOperationException)
                {
                    // not a saved asset -- nothing to duplicate from, skip
                }
            }
        }

        private static void CaptureDescendantComponents(Transform target, Transform avatarRoot, AvatarReferenceState state, DiagnosticLog log)
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

                // AvatarVcsUntracked opts a subtree back out even though an
                // ancestor target is tracked -- the "don't version-control
                // this outfit" exclusion (KAN-11). GetComponentInParent
                // includes descendant itself, so this drops the marked node
                // and everything under it.
                if (descendant.GetComponentInParent<AvatarVcsUntracked>(includeInactive: true) != null) continue;

                var relPath = ReferenceResolver.GetRelativePath(descendant, target);

                // activeSelf/tag/layer live on the GameObject itself, not on
                // any one Component's SerializedObject, so they need their
                // own capture step alongside the per-component walk below
                // (same three fields ContainerCapture already records for a
                // container's own root).
                state.objectStates.Add(new ObjectStateRef
                {
                    path = relPath,
                    activeSelf = descendant.gameObject.activeSelf,
                    tag = descendant.gameObject.tag,
                    layer = descendant.gameObject.layer,
                });

                // Name/GUID-resolved blend shape + material capture for this
                // node's renderer, before the generic component walk below
                // (StripNarrowlyTrackedFields then drops the same fields from
                // the generic Renderer capture -- now correctly, because this
                // narrow path finally covers every descendant, not just the
                // target).
                CaptureRenderersInto(descendant, relPath, state.blendShapes, state.materials);

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
                        if (!IsOwnPrefabInstanceRoot(descendant.gameObject)) continue;

                        var transformState = CapturePrefabInstanceTransform(transform, target);
                        if (transformState.fields.Count > 0) state.components.Add(transformState);
                        continue;
                    }

                    var captured = ComponentCapturer.Capture(component, target, avatarRoot, log);
                    StripNarrowlyTrackedFields(component, captured);

                    if (captured.fields.Count == 0 && captured.assetRefs.Count == 0 && captured.sceneRefs.Count == 0)
                        continue; // nothing left worth recording after stripping

                    state.components.Add(captured);
                }
            }
        }

        /// <summary>
        /// True only when go is the root of a prefab instance of its own --
        /// an accessory dropped onto a bone -- and not merely some object
        /// that happens to live inside a larger prefab instance.
        ///
        /// GetPrefabGuid (GetCorrespondingObjectFromSource) was used here and
        /// answers a different question: it is non-null for EVERY object
        /// inside a prefab instance. Real avatars are prefab instances, so it
        /// excluded nothing and every bone in the Armature got its pose
        /// captured -- the exact thing the caller's comment says must never
        /// happen. It is also the single largest slice of a commit: in one
        /// real project 534 of a commit's 678 captured components were bones,
        /// 36% of the captured component data.
        /// </summary>
        private static bool IsOwnPrefabInstanceRoot(GameObject go) =>
            PrefabUtility.GetNearestPrefabInstanceRoot(go) == go;

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
        /// name / by GUID with GuidRemapper support) by CaptureRenderersInto,
        /// which now runs for every descendant renderer, not just the
        /// target's; capturing them again here as fragile index-based array
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
