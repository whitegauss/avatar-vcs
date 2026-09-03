using System;
using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Core.Diagnostics;
using AvatarVcs.Core.MaterialSettings;
using AvatarVcs.Editor.AvatarReferences;
using AvatarVcs.Editor.Capture;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.Diagnostics;
using AvatarVcs.Editor.MaterialSettings;
using AvatarVcs.Core.Model;
using AvatarVcs.Editor.Reflection;
using AvatarVcs.Runtime;
using UnityEngine;

namespace AvatarVcs.Editor.Operations
{
    /// <summary>
    /// Captures the current state of a container into a ContainerSnapshot.
    /// Design doc section 3.1.
    /// </summary>
    public static class ContainerCapture
    {
        /// <summary>
        /// avatarRoot is used only to resolve scene-reference fields (see
        /// ComponentCapturer); it defaults to container when omitted.
        /// </summary>
        public static ContainerSnapshot CaptureContainer(Transform container, Transform avatarRoot = null, DiagnosticLog log = null)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            avatarRoot ??= container;

            var marker = container.GetComponent<AvatarVcsContainer>();
            if (marker == null)
                throw new ArgumentException($"'{container.name}' is not an AvatarVCS container (missing AvatarVcsContainer).", nameof(container));

            // KAN-20: a caller mid-commit passes its own DiagnosticLog; a
            // direct caller (tests) passes none, so make one and flush here.
            var ownsLog = log == null;
            log ??= new DiagnosticLog();
            try
            {
                return CaptureCore(container, avatarRoot, marker, log);
            }
            finally
            {
                if (ownsLog) UnityDiagnosticSink.Flush(log);
            }
        }

        private static ContainerSnapshot CaptureCore(Transform container, Transform avatarRoot, AvatarVcsContainer marker, DiagnosticLog log)
        {
            ReferenceResolver.WarnOnSameNameSiblings(container, $"container '{container.name}'", log);

            var childGuids = container.Cast<Transform>()
                .Select(child => (child, guid: ContainerManager.GetPrefabGuid(child.gameObject)))
                .ToList();
            var prefabGuids = childGuids
                .Select(c => c.guid)
                .Where(guid => !string.IsNullOrEmpty(guid))
                .ToList();

            // A container is destroyed and regenerated purely from
            // prefabGuids on every checkout (design doc 1.2). A child that
            // isn't a prefab instance has no guid to regenerate it from, so
            // anything placed here directly (Create Empty, a raw light/
            // camera/mesh, an unpacked prefab) is permanently lost the next
            // time this container round-trips through a commit.
            foreach (var (child, guid) in childGuids)
            {
                if (string.IsNullOrEmpty(guid))
                    log.Warn($"[AvatarVCS] '{child.name}' inside container '{container.name}' is not a prefab "
                        + "instance and will be permanently lost the next time this container is checked out. "
                        + "Turn it into a prefab and place the instance in the container instead.");
            }

            // Only components on the container root itself are captured (e.g. a
            // ModularAvatarMergeArmature placed to configure the container) --
            // matches the single "path": "" example in design doc section 2.1.
            // Components inside the placed prefab's own hierarchy are reproduced
            // by re-instantiating the prefab, not by capturing them here.
            var components = container.GetComponents<Component>()
                .Where(c => c != null && c is not Transform && c is not AvatarVcsContainer)
                .Select(c => ComponentCapturer.Capture(c, container, avatarRoot, log))
                .ToList();

            // KAN-70: containers regenerate their prefab instances clean on
            // checkout, so any BlendShape weight / material slot / active-tag-
            // layer the user adjusted inside one would be lost. Record them
            // (name/GUID-resolved, path-relative to the container) so
            // ContainerRestore can re-apply them after regeneration. Generic
            // component fields inside a prefab instance are deliberately NOT
            // recorded here -- that's structural territory the prefab owns.
            var blendShapes = new List<BlendShapeRef>();
            var materials = new List<MaterialRef>();
            var objectStates = new List<ObjectStateRef>();
            var materialSettings = new List<MaterialSettingsState>();
            foreach (var node in container.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (node == container) continue; // container root's own tag/active/layer is the snapshot's top-level fields
                var relPath = ReferenceResolver.GetRelativePath(node, container);
                objectStates.Add(new ObjectStateRef
                {
                    path = relPath,
                    activeSelf = node.gameObject.activeSelf,
                    tag = node.gameObject.tag,
                    layer = node.gameObject.layer,
                });
                AvatarReferenceCapture.CaptureRenderersInto(node, relPath, blendShapes, materials);
                CaptureMaterialSettingsInto(node, relPath, materialSettings);
            }

            return new ContainerSnapshot
            {
                containerId = container.name,
                containerGuid = marker.ContainerGuid,
                prefabGuids = prefabGuids,
                localPosition = container.localPosition,
                localRotation = container.localRotation,
                localScale = container.localScale,
                tag = container.gameObject.tag,
                activeSelf = container.gameObject.activeSelf,
                layer = container.gameObject.layer,
                components = components,
                blendShapes = blendShapes,
                materials = materials,
                objectStates = objectStates,
                materialSettings = materialSettings,
            };
        }

        // KAN-73: lilToon/poiyomi/MToon shader property values for every
        // supported-shader slot on this node's renderer, tagged with the
        // container-relative path. Mirrors AvatarReferenceCollector's
        // materialSettings loop, including its policy of silently skipping a
        // runtime-only / unsaved material (there's no asset to duplicate
        // from) and any shader outside the supported set -- material *slot*
        // tracking already covers every slot regardless.
        private static void CaptureMaterialSettingsInto(Transform node, string relPath, List<MaterialSettingsState> into)
        {
            var renderer = node.GetComponent<Renderer>();
            if (renderer == null) return;

            var sharedMats = renderer.sharedMaterials;
            for (var slot = 0; slot < sharedMats.Length; slot++)
            {
                var material = sharedMats[slot];
                if (material == null || material.shader == null) continue;
                if (!ShaderPropertyMap.IsSupported(material.shader.name)) continue;

                try
                {
                    into.Add(MaterialSettingsCapture.Capture(material, material.shader.name, relPath, slot));
                }
                catch (InvalidOperationException)
                {
                    // not a saved asset -- skip this slot (same as the Track
                    // Properties path)
                }
            }
        }
    }
}
