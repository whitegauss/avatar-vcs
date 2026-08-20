using System;
using System.Linq;
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
        public static ContainerSnapshot CaptureContainer(Transform container)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));

            var marker = container.GetComponent<AvatarVcsContainer>();
            if (marker == null)
                throw new ArgumentException($"'{container.name}' is not an AvatarVCS container (missing AvatarVcsContainer).", nameof(container));

            var prefabGuids = container.Cast<Transform>()
                .Select(child => ContainerManager.GetPrefabGuid(child.gameObject))
                .Where(guid => !string.IsNullOrEmpty(guid))
                .ToList();

            return new ContainerSnapshot
            {
                containerId = container.name,
                containerGuid = marker.ContainerGuid,
                prefabGuids = prefabGuids,
                localPosition = container.localPosition,
                localRotation = container.localRotation,
                localScale = container.localScale,
            };
        }
    }
}
