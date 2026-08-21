using System;
using System.Linq;
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
        // Must match AvatarVcs.Editor.Core.ContainerManager.RootName. Kept as
        // a separate literal since Runtime can't reference the Editor
        // assembly; ContainerManager.FindRoot no longer depends on this
        // name matching (see its GetComponentInChildren fallback), but
        // warning here still helps a user notice they've drifted from it.
        private const string ExpectedRootName = "[AvatarVCS]";

        [SerializeField] private string avatarGuid;

        public string AvatarGuid => avatarGuid;

        public void AssignGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                throw new ArgumentException("guid must not be empty.", nameof(guid));
            if (!IsValidGuidShape(guid))
                throw new ArgumentException(
                    $"guid must be a 32-character lowercase hex string (as produced by Guid.NewGuid().ToString(\"N\")); got '{guid}'.",
                    nameof(guid));
            if (!string.IsNullOrEmpty(avatarGuid))
                throw new InvalidOperationException("avatarGuid is already assigned and is immutable.");

            avatarGuid = guid;
        }

        // Catches a bug in this tool's own guid generation early. Does NOT
        // by itself protect against a malicious avatarGuid baked directly
        // into a shared scene/prefab's serialized data -- Unity deserializes
        // [SerializeField] fields directly, bypassing this method entirely.
        // CommitStore validates the same shape again at the point avatarGuid
        // is actually turned into a filesystem path, which is the real
        // defense boundary.
        private static bool IsValidGuidShape(string value)
        {
            if (value == null || value.Length != 32) return false;
            foreach (var c in value)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }
            return true;
        }

        // Duplicating a whole avatar (a common way to make a variant) clones
        // this along with everything else, so the duplicate starts out
        // sharing the original's avatarGuid -- two "different" avatars would
        // then read/write the exact same commit history storage. Self-heals
        // the common case of the duplicate avatar landing as a sibling of
        // the original: whichever avatar has the lower sibling index keeps
        // the guid, the other regenerates. Compares avatar-level siblings
        // (this object's parent's siblings), not this object's own siblings
        // -- each avatar has its own separate "[AvatarVCS]" child, so those
        // are never siblings of each other.
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(avatarGuid)) return;

            if (gameObject.name != ExpectedRootName)
                Debug.LogWarning($"[AvatarVCS] This GameObject was renamed from '{ExpectedRootName}'. "
                    + "It'll still be found and used correctly, but renaming it back is recommended to avoid confusion.");

            var avatarTransform = transform.parent; // EnsureRoot always parents this under the avatar
            if (avatarTransform == null) return;

            var avatarSiblings = avatarTransform.parent != null
                ? Enumerable.Range(0, avatarTransform.parent.childCount).Select(i => avatarTransform.parent.GetChild(i))
                : avatarTransform.gameObject.scene.GetRootGameObjects().Select(go => go.transform);

            var mySiblingIndex = avatarTransform.GetSiblingIndex();
            foreach (var sibling in avatarSiblings)
            {
                if (sibling == avatarTransform) continue;

                var siblingRoot = sibling.GetComponentInChildren<AvatarVcsRoot>(includeInactive: true);
                if (siblingRoot != null && siblingRoot != this
                    && siblingRoot.avatarGuid == avatarGuid
                    && sibling.GetSiblingIndex() < mySiblingIndex)
                {
                    avatarGuid = Guid.NewGuid().ToString("N");
                    break;
                }
            }
        }
    }
}
