using System;
using System.Linq;
using AvatarVcs.Runtime;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.Core
{
    /// <summary>
    /// Creation and lookup of the "[AvatarVCS]" root and its containers.
    /// Design doc sections 1.1, 1.3.1, 1.3.2.
    /// </summary>
    public static class ContainerManager
    {
        public const string RootName = "[AvatarVCS]";

        /// <summary>
        /// Finds the existing management root under avatarRoot, or creates one.
        /// Safe to call repeatedly: never creates a duplicate.
        /// </summary>
        public static GameObject EnsureRoot(GameObject avatarRoot)
        {
            if (avatarRoot == null) throw new ArgumentNullException(nameof(avatarRoot));

            var existing = FindRoot(avatarRoot);
            if (existing != null) return existing;

            var rootGo = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(rootGo, "Create AvatarVCS Root");
            Undo.SetTransformParent(rootGo.transform, avatarRoot.transform, "Create AvatarVCS Root");
            rootGo.transform.localPosition = Vector3.zero;
            rootGo.transform.localRotation = Quaternion.identity;
            rootGo.transform.localScale = Vector3.one;
            var marker = Undo.AddComponent<AvatarVcsRoot>(rootGo);
            marker.AssignGuid(Guid.NewGuid().ToString("N"));

            return rootGo;
        }

        public static GameObject FindRoot(GameObject avatarRoot)
        {
            if (avatarRoot == null) throw new ArgumentNullException(nameof(avatarRoot));

            var child = avatarRoot.transform.Find(RootName);
            return child != null && child.GetComponent<AvatarVcsRoot>() != null ? child.gameObject : null;
        }

        /// <summary>
        /// Walks up from a raw Hierarchy selection to find the avatar it
        /// actually belongs to, so callers never mistake "a container, or
        /// something inside one" for the avatar root itself -- e.g.
        /// selecting an individual outfit prefab instance and hitting
        /// Commit must not silently spin up a brand new, unrelated
        /// "[AvatarVCS]" root nested inside that outfit.
        ///
        /// Returns null if no existing AvatarVCS structure is found
        /// anywhere in the ancestor chain, meaning `from` (or nothing) is a
        /// legitimate candidate for a brand new avatar root.
        /// </summary>
        public static GameObject FindEnclosingAvatarRoot(GameObject from)
        {
            if (from == null) return null;

            for (var t = from.transform; t != null; t = t.parent)
            {
                if (t.GetComponent<AvatarVcsRoot>() != null)
                    return t.parent != null ? t.parent.gameObject : null;

                if (t.GetComponent<AvatarVcsContainer>() != null)
                {
                    var rootParent = t.parent; // the "[AvatarVCS]" root
                    return rootParent != null && rootParent.parent != null ? rootParent.parent.gameObject : null;
                }
            }

            return null;
        }

        /// <summary>
        /// The avatar's stable identity, used to key commit history storage.
        /// Calls EnsureRoot, so a guid is always available even before any
        /// container exists.
        /// </summary>
        public static string GetAvatarGuid(GameObject avatarRoot)
        {
            var root = EnsureRoot(avatarRoot);
            return root.GetComponent<AvatarVcsRoot>().AvatarGuid;
        }

        /// <summary>
        /// Creates a new container directly under root. Containers may not be nested
        /// (design doc 1.3.1) and names must be unique among siblings.
        /// </summary>
        public static GameObject CreateContainer(GameObject root, string containerId)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (string.IsNullOrEmpty(containerId)) throw new ArgumentException("containerId must not be empty.", nameof(containerId));
            if (root.GetComponent<AvatarVcsRoot>() == null)
                throw new ArgumentException("root must be an AvatarVCS root GameObject (see EnsureRoot).", nameof(root));
            if (root.transform.Find(containerId) != null)
                throw new InvalidOperationException($"A container named '{containerId}' already exists under '{root.name}'.");

            var containerGo = new GameObject(containerId);
            Undo.RegisterCreatedObjectUndo(containerGo, "Create AvatarVCS Container");
            Undo.SetTransformParent(containerGo.transform, root.transform, "Create AvatarVCS Container");
            containerGo.transform.localPosition = Vector3.zero;
            containerGo.transform.localRotation = Quaternion.identity;
            containerGo.transform.localScale = Vector3.one;

            var marker = Undo.AddComponent<AvatarVcsContainer>(containerGo);
            marker.AssignGuid(Guid.NewGuid().ToString("N"));

            return containerGo;
        }

        public static Transform[] GetContainers(GameObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            return root.transform.Cast<Transform>()
                .Where(t => t.GetComponent<AvatarVcsContainer>() != null)
                .ToArray();
        }

        /// <summary>
        /// Resolves the GUID of the prefab asset instance derives from, via
        /// GetCorrespondingObjectFromSource. Returns null if instance is not a
        /// prefab instance.
        /// </summary>
        public static string GetPrefabGuid(GameObject instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));

            var source = PrefabUtility.GetCorrespondingObjectFromSource(instance);
            if (source == null) return null;

            var path = AssetDatabase.GetAssetPath(source);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
        }
    }
}
