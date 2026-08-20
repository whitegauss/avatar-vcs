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
            if (!string.IsNullOrEmpty(containerGuid))
                throw new InvalidOperationException("containerGuid is already assigned and is immutable.");

            containerGuid = guid;
        }
    }
}
