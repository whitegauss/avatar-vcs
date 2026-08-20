using System;
using UnityEngine;

namespace AvatarVcs.Runtime
{
    /// <summary>
    /// Marker component identifying the "[AvatarVCS]" management root under an
    /// avatar. Carries an immutable avatarGuid used to key commit history
    /// storage (design doc 1.3.3), independent of GameObject name or of any
    /// VRChat SDK type (kept dependency-free).
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class AvatarVcsRoot : MonoBehaviour
    {
        [SerializeField] private string avatarGuid;

        public string AvatarGuid => avatarGuid;

        public void AssignGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                throw new ArgumentException("guid must not be empty.", nameof(guid));
            if (!string.IsNullOrEmpty(avatarGuid))
                throw new InvalidOperationException("avatarGuid is already assigned and is immutable.");

            avatarGuid = guid;
        }
    }
}
