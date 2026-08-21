using System;
using UnityEngine;

namespace AvatarVcs.Runtime
{
    /// <summary>
    /// Marker component for a single managed container. Carries an immutable
    /// containerGuid so identity survives GameObject renames. Design doc section 1.3.1.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class AvatarVcsContainer : MonoBehaviour
    {
        [SerializeField] private string containerGuid;

        public string ContainerGuid => containerGuid;

        public void AssignGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                throw new ArgumentException("guid must not be empty.", nameof(guid));
            if (!GuidShape.IsValid(guid))
                throw new ArgumentException(
                    $"guid must be a 32-character lowercase hex string (as produced by Guid.NewGuid().ToString(\"N\")); got '{guid}'.",
                    nameof(guid));
            if (!string.IsNullOrEmpty(containerGuid))
                throw new InvalidOperationException("containerGuid is already assigned and is immutable.");

            containerGuid = guid;
        }

        // Ctrl+D duplication clones serialized field data verbatim, so a
        // duplicated container starts out sharing its source's containerGuid
        // -- silently breaking every guid-keyed lookup (commit deletion's
        // shared-asset check, diff/restore by containerGuid, ...). OnValidate
        // runs after a duplicate is created, so this self-heals: whichever of
        // a colliding pair has the lower sibling index keeps the guid, the
        // other regenerates. Deterministic given a stable hierarchy, so it
        // converges instead of flip-flopping on repeated calls.
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(containerGuid) || transform.parent == null) return;

            var parent = transform.parent;
            var mySiblingIndex = transform.GetSiblingIndex();
            for (var i = 0; i < parent.childCount; i++)
            {
                var sibling = parent.GetChild(i);
                if (sibling == transform) continue;

                var siblingContainer = sibling.GetComponent<AvatarVcsContainer>();
                if (siblingContainer != null
                    && siblingContainer.containerGuid == containerGuid
                    && sibling.GetSiblingIndex() < mySiblingIndex)
                {
                    containerGuid = Guid.NewGuid().ToString("N");
                    break;
                }
            }
        }
    }
}
