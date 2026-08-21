using System;
using System.Linq;
using AvatarVcs.Editor.Capture;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.Model;
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
        public static ContainerSnapshot CaptureContainer(Transform container, Transform avatarRoot = null)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            avatarRoot ??= container;

            var marker = container.GetComponent<AvatarVcsContainer>();
            if (marker == null)
                throw new ArgumentException($"'{container.name}' is not an AvatarVCS container (missing AvatarVcsContainer).", nameof(container));

            var prefabGuids = container.Cast<Transform>()
                .Select(child => ContainerManager.GetPrefabGuid(child.gameObject))
                .Where(guid => !string.IsNullOrEmpty(guid))
                .ToList();

            // Only components on the container root itself are captured (e.g. a
            // ModularAvatarMergeArmature placed to configure the container) --
            // matches the single "path": "" example in design doc section 2.1.
            // Components inside the placed prefab's own hierarchy are reproduced
            // by re-instantiating the prefab, not by capturing them here.
            var components = container.GetComponents<Component>()
                .Where(c => c != null && c is not Transform && c is not AvatarVcsContainer)
                .Select(c => ComponentCapturer.Capture(c, container, avatarRoot))
                .ToList();

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
            };
        }
    }
}
