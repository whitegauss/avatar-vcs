using System;
using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Core.Naming;
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
        /// Safe to call repeatedly: never creates a duplicate. Deliberately
        /// does not seed a default container -- this is called from deep,
        /// container-count-agnostic internal plumbing (CommitBuilder,
        /// BranchManager, CheckoutOperation) as well as the user-facing
        /// "Ensure Root" command, and only the latter should ever add
        /// anything beyond the root itself. See
        /// AvatarVcsMenu.EnsureRootMenuItem for the seeded-default-container
        /// UX.
        /// </summary>
        public static GameObject EnsureRoot(GameObject avatarRoot)
        {
            if (avatarRoot == null) throw new ArgumentNullException(nameof(avatarRoot));

            var existing = FindRoot(avatarRoot);
            if (existing != null) return existing;

            var rootGo = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(rootGo, "Create AvatarVCS Root");
            Undo.SetTransformParent(rootGo.transform, avatarRoot.transform, "Create AvatarVCS Root");
            LocalTransform.Reset(rootGo.transform);
            var marker = Undo.AddComponent<AvatarVcsRoot>(rootGo);
            marker.AssignGuid(Guid.NewGuid().ToString("N"));

            return rootGo;
        }

        /// <summary>
        /// Name of the container seeded by EnsureRootAndDefaultContainer on
        /// its first-ever run for a given avatar. (EnsureRootWithDefaults no
        /// longer seeds one -- KAN-71.)
        /// </summary>
        public const string DefaultContainerId = "container_1";

        /// <summary>
        /// EnsureRoot, plus (only on the root's actual first creation) a
        /// default container, so there's immediately somewhere to place a
        /// prefab instead of requiring a separate Create Container step
        /// first. Kept separate from EnsureRoot itself, which internal
        /// plumbing (CommitBuilder, BranchManager, CheckoutOperation) also
        /// calls and must stay container-count-agnostic -- this is for the
        /// user-facing "Ensure Root" command only.
        /// </summary>
        public static GameObject EnsureRootAndDefaultContainer(GameObject avatarRoot)
        {
            var isNewRoot = FindRoot(avatarRoot) == null;
            var root = EnsureRoot(avatarRoot);
            if (isNewRoot)
                SeedDefaultContainer(root);

            return root;
        }

        /// <summary>
        /// EnsureRoot, plus (only on the root's actual first creation)
        /// AvatarVcsTrackedReference on the avatar root itself and on every
        /// top-level child that already exists -- issue #46: most users want
        /// their avatar body/armature/accessories tracked by default (e.g.
        /// toggling a default accessory on/off), rather than having to
        /// remember to opt each one in via Track Properties Here, and some
        /// assets (bone-attached colliders/accessories) can only be placed
        /// directly under Armature, bypassing container management entirely
        /// -- untracked, their changes wouldn't be recorded at all. Kept out
        /// of EnsureRoot itself for the same reason
        /// EnsureRootAndDefaultContainer is -- see that method's doc comment.
        ///
        /// This is what the "Ensure Root" command calls. It deliberately does
        /// NOT seed an empty container: the property tracking seeded here
        /// already covers the common case, and a loose prefab instance placed
        /// directly under "[AvatarVCS]" is wrapped into a container
        /// automatically at commit time
        /// (AdoptLoosePrefabInstancesAsContainers), so an empty
        /// "[AvatarVCS]/container_1" was just noise (KAN-71).
        /// EnsureRootAndDefaultContainer still exists for callers that
        /// explicitly want one.
        /// </summary>
        public static GameObject EnsureRootWithDefaults(GameObject avatarRoot)
        {
            var isNewRoot = FindRoot(avatarRoot) == null;
            var root = EnsureRoot(avatarRoot);
            if (isNewRoot)
                SeedDefaultTracking(avatarRoot, root);

            return root;
        }

        private static void SeedDefaultContainer(GameObject root) => CreateContainer(root, DefaultContainerId);

        private static void SeedDefaultTracking(GameObject avatarRoot, GameObject root)
        {
            TrackIfUntracked(avatarRoot);
            for (var i = 0; i < avatarRoot.transform.childCount; i++)
            {
                var child = avatarRoot.transform.GetChild(i).gameObject;
                if (child == root) continue; // [AvatarVCS] is container-managed, never tracked this way
                TrackIfUntracked(child);
            }
        }

        private static void TrackIfUntracked(GameObject go)
        {
            if (go.GetComponent<AvatarVcsTrackedReference>() == null)
                Undo.AddComponent<AvatarVcsTrackedReference>(go);
        }

        public static GameObject FindRoot(GameObject avatarRoot)
        {
            if (avatarRoot == null) throw new ArgumentNullException(nameof(avatarRoot));

            var child = avatarRoot.transform.Find(RootName);
            if (child != null && child.GetComponent<AvatarVcsRoot>() != null)
                return child.gameObject;

            // Fall back to the marker component if the fast name-based
            // lookup above missed: a manual Hierarchy rename of "[AvatarVCS]"
            // would otherwise make it permanently unfindable by this method,
            // and the next EnsureRoot would spin up a duplicate root next to
            // the (now orphaned) renamed one instead of reusing it.
            for (var i = 0; i < avatarRoot.transform.childCount; i++)
            {
                var candidate = avatarRoot.transform.GetChild(i);
                if (candidate.GetComponent<AvatarVcsRoot>() != null)
                    return candidate.gameObject;
            }

            return null;
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

            // Searches from as well as every ancestor for the "[AvatarVCS]"
            // root's marker component, so this resolves correctly no matter
            // how deep from sits inside a container's own hierarchy.
            // includeInactive: true because a container (or the whole
            // "[AvatarVCS]" root) can legitimately be toggled off.
            var root = from.GetComponentInParent<AvatarVcsRoot>(includeInactive: true);
            if (root != null && root.transform.parent != null) return root.transform.parent.gameObject;

            // The check above only catches "from is inside a container" --
            // AvatarVcsRoot lives on "[AvatarVCS]" itself, which is a
            // SIBLING of everything else under the avatar (Body, Armature,
            // ...), not an ancestor of them. Without this fallback walk,
            // Ensure Root on an arbitrary nested child outside any container
            // (e.g. deep under Body/Armature on an avatar that already has
            // "[AvatarVCS]") would find nothing here, fail the FindRoot(from)
            // check right after this method returns, and spin up a second,
            // nested "[AvatarVCS]" inside that child instead of resolving to
            // the avatar that's already tracked. Self-inclusive (checks
            // `from` itself first): if `from` already IS the avatar root,
            // this returns it directly -- callers no longer need a separate
            // FindRoot(from) check of their own (see
            // ResolveAvatarRootWithConfirmation).
            return FindAncestorWithRoot(from.transform)?.gameObject;
        }

        /// <summary>
        /// True if go itself, or any ancestor, has "[AvatarVCS]" as a direct
        /// child -- i.e. go is the avatar root itself, or sits somewhere
        /// underneath it (inside a container or anywhere else in the
        /// avatar's own hierarchy).
        /// </summary>
        public static bool IsUnderManagedAvatar(GameObject go) =>
            go != null && FindAncestorWithRoot(go.transform) != null;

        /// <summary>
        /// Shared walk behind FindEnclosingAvatarRoot's fallback pass and
        /// IsUnderManagedAvatar: climbs from (inclusive) up to the scene
        /// root, returning the first ancestor whose own direct children
        /// include an existing "[AvatarVCS]" (per FindRoot), or null if none
        /// do.
        /// </summary>
        private static Transform FindAncestorWithRoot(Transform from)
        {
            for (var t = from; t != null; t = t.parent)
            {
                if (FindRoot(t.gameObject) != null) return t;
            }

            return null;
        }

        /// <summary>
        /// True when turning <paramref name="selection"/> into an avatar root
        /// would be adopting a GameObject that has no existing AvatarVCS
        /// structure anywhere in its ancestor chain -- so the caller should
        /// confirm first (it could just as easily be a single outfit item as
        /// the avatar itself). False for null, and for a selection that's
        /// already the avatar root or sits under one (that resolves with no
        /// prompt). KAN-21 phase 4-6: the pure decision, split out from the
        /// dialog in ResolveAvatarRootWithConfirmation so it can be tested
        /// headlessly.
        /// </summary>
        public static bool NeedsAdoptionConfirmation(GameObject selection) =>
            selection != null && FindEnclosingAvatarRoot(selection) == null;

        /// <summary>
        /// Resolves the avatar to operate on from a raw Hierarchy selection,
        /// for any entry point that turns "whatever's selected" into an
        /// avatarRoot (the GameObject menu, the window's avatar picker). If
        /// selection is already inside an existing AvatarVCS structure (a
        /// container, something inside one, or the "[AvatarVCS]" root
        /// itself), walks up to the actual owning avatar automatically via
        /// FindEnclosingAvatarRoot. If selection has no existing structure
        /// at all (see NeedsAdoptionConfirmation), confirms with the user
        /// before treating it as a brand new avatar root. Returns null if
        /// selection is null or the user cancels; callers decide what
        /// "cancel" means for them (abort vs. keep whatever was previously
        /// selected).
        ///
        /// The one modal in this class: a thin Editor-side wrapper over the
        /// pure FindEnclosingAvatarRoot / NeedsAdoptionConfirmation pair.
        /// </summary>
        public static GameObject ResolveAvatarRootWithConfirmation(GameObject selection, string actionDescription)
        {
            if (selection == null) return null;

            // FindEnclosingAvatarRoot is self-inclusive: if selection is
            // already the avatar root itself, it comes back here too.
            var enclosing = FindEnclosingAvatarRoot(selection);
            if (enclosing != null) return enclosing;

            return EditorUtility.DisplayDialog("Start Tracking This Object?",
                    $"'{selection.name}' has no AvatarVCS history yet. {actionDescription}\n\n"
                    + "If you meant to select your actual avatar's root GameObject (or something inside its existing containers), cancel and select that instead.",
                    "Start Tracking", "Cancel")
                ? selection
                : null;
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
            LocalTransform.Reset(containerGo.transform);

            var marker = Undo.AddComponent<AvatarVcsContainer>(containerGo);
            marker.AssignGuid(Guid.NewGuid().ToString("N"));

            return containerGo;
        }

        /// <summary>
        /// Like CreateContainer, but disambiguates desiredContainerId against
        /// the names already under root ("new_container", "new_container_1",
        /// ...) instead of throwing when it's taken. For the "Create
        /// Container" menu command, which passes a fixed base name and would
        /// otherwise surface a raw InvalidOperationException on its second
        /// use. Uses the same SiblingNamer.MakeUnique as
        /// AdoptLoosePrefabInstancesAsContainers; CreateContainer's own
        /// uniqueness check compares against every child by name, so the
        /// candidate set is every child, not just containers.
        /// </summary>
        public static GameObject CreateContainerWithUniqueName(GameObject root, string desiredContainerId)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (string.IsNullOrEmpty(desiredContainerId))
                throw new ArgumentException("desiredContainerId must not be empty.", nameof(desiredContainerId));
            if (root.GetComponent<AvatarVcsRoot>() == null)
                throw new ArgumentException("root must be an AvatarVCS root GameObject (see EnsureRoot).", nameof(root));

            return CreateContainer(root, SiblingNamer.MakeUnique(DirectChildNames(root), desiredContainerId));
        }

        /// <summary>
        /// The names of every direct child of root, as the candidate set for
        /// SiblingNamer.MakeUnique -- CreateContainer's uniqueness check is by
        /// child name (not by marker), so a unique container name has to avoid
        /// every child, not just the containers.
        /// </summary>
        private static HashSet<string> DirectChildNames(GameObject root) =>
            root.transform.Cast<Transform>().Select(t => t.name).ToHashSet();

        public static Transform[] GetContainers(GameObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            return root.transform.Cast<Transform>()
                .Where(t => t.GetComponent<AvatarVcsContainer>() != null)
                .ToArray();
        }

        /// <summary>
        /// Catches two ways a container structure can violate the tool's
        /// invariants (design doc 1.3.1: containers live directly under
        /// "[AvatarVCS]", are not nested, and are uniquely named) without
        /// going through CreateContainer -- a manual Hierarchy rename or
        /// drag-and-drop can still produce either. Both silently corrupt
        /// commits (duplicate names collide on the same key; a nested
        /// container isn't itself a prefab instance, so it's invisible to
        /// its parent's capture and is lost on the next checkout) rather
        /// than failing loudly, so this is meant to be called right before
        /// a commit is taken.
        /// </summary>
        public static void ValidateContainers(GameObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            var containers = GetContainers(root);

            var duplicateNames = containers
                .GroupBy(t => t.name)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (duplicateNames.Count > 0)
                throw new InvalidOperationException(
                    $"Duplicate container name(s) under '{root.name}': {string.Join(", ", duplicateNames)}. "
                    + "Rename one of them before committing -- containerId must be unique.");

            foreach (var container in containers)
            {
                var nested = container.GetComponentsInChildren<AvatarVcsContainer>(includeInactive: true)
                    .FirstOrDefault(c => c.transform != container);
                if (nested != null)
                    throw new InvalidOperationException(
                        $"Container '{container.name}' has another container ('{nested.name}') nested inside it. "
                        + $"Containers cannot be nested -- move '{nested.name}' directly under '{root.name}' instead.");
            }
        }

        /// <summary>
        /// Issue #70: dropping a prefab instance directly under "[AvatarVCS]"
        /// (skipping Create Container entirely) is meant to just work.
        /// Auto-wraps any such loose direct child in a freshly-created
        /// container GameObject (exactly what Create Container + manually
        /// dragging the prefab inside would produce), rather than adding
        /// AvatarVcsContainer directly onto the prefab instance itself --
        /// ContainerRestore always regenerates a container as an empty
        /// wrapper with the prefab(s) instantiated as its children (see
        /// InstantiateContainerStructure), so a container that IS the
        /// prefab instance itself has no children for CaptureContainer to
        /// read a prefabGuid from, and can't be reproduced by that restore
        /// path at all -- a real Unity prefab instance can only be created
        /// via PrefabUtility.InstantiatePrefab, not retroactively turned an
        /// existing GameObject into one. Wrapping keeps 100% compatibility
        /// with the existing capture/restore model; only the user-facing
        /// step (no manual Create Container needed) changes.
        /// Meant to be called right before a commit is taken, same as
        /// ValidateContainers. Idempotent: already-marked children (real
        /// containers) and non-prefab-instance children (nothing to
        /// regenerate them from, same restriction CaptureContainer already
        /// warns about) are left untouched.
        /// </summary>
        public static void AdoptLoosePrefabInstancesAsContainers(GameObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            // Snapshot with ToList first: this loop reparents children out
            // from under root as it goes, which would otherwise disturb
            // Transform's own live child enumeration mid-iteration.
            foreach (var child in root.transform.Cast<Transform>().ToList())
            {
                if (child.GetComponent<AvatarVcsContainer>() != null) continue;
                if (GetPrefabGuid(child.gameObject) == null) continue;

                // Every direct child of root except the one about to move
                // into the wrapper: checking against it would find the name
                // the wrapper is taking over and needlessly disambiguate it.
                // Computed before the wrapper is parented, so the wrapper
                // doesn't collide with itself either.
                var siblingNames = root.transform.Cast<Transform>()
                    .Where(t => t != child)
                    .Select(t => t.name)
                    .ToHashSet();

                var wrapper = new GameObject(SiblingNamer.MakeUnique(siblingNames, child.name));
                Undo.RegisterCreatedObjectUndo(wrapper, "Adopt Prefab As Container");

                // Park the wrapper at root's own origin BEFORE the child moves
                // in. Undo.SetTransformParent keeps world pose, so resetting
                // the wrapper's local transform after the child was already
                // inside dragged the child along with it -- on an avatar not
                // sitting at the world origin that displaced every adopted
                // prefab by the avatar's own offset (issue #73).
                Undo.SetTransformParent(wrapper.transform, root.transform, "Adopt Prefab As Container");
                LocalTransform.Reset(wrapper.transform);

                Undo.SetTransformParent(child, wrapper.transform, "Adopt Prefab As Container");

                var marker = Undo.AddComponent<AvatarVcsContainer>(wrapper);
                marker.AssignGuid(Guid.NewGuid().ToString("N"));
            }
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
