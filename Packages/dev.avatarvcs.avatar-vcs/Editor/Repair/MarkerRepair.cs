using System.Collections.Generic;
using System.IO;
using System.Linq;
using AvatarVcs.Core.History;
using AvatarVcs.Core.Repair;
using AvatarVcs.Editor.History;
using AvatarVcs.Editor.Reflection;
using AvatarVcs.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AvatarVcs.Editor.Repair
{
    public sealed class MarkerRepairAction
    {
        public GameObject target;
        public MarkerKind kind;

        /// <summary>The avatarGuid or containerGuid read back out of the scene file; null for the field-less markers.</summary>
        public string guid;

        /// <summary>Hierarchy path, for the confirmation dialog and the log.</summary>
        public string path;
    }

    public sealed class MarkerRepairPlan
    {
        public readonly List<MarkerRepairAction> actions = new List<MarkerRepairAction>();

        /// <summary>Missing scripts inside an avatar that couldn't be identified; described, never touched.</summary>
        public readonly List<string> unresolved = new List<string>();

        /// <summary>Scenes that couldn't be read (unsaved, or never saved).</summary>
        public readonly List<string> blockers = new List<string>();

        /// <summary>
        /// How many missing scripts on each GameObject this plan accounts for.
        /// Apply only clears an object's missing scripts when that number
        /// matches what the object actually has -- an unrelated broken
        /// component from some other package must survive untouched.
        /// </summary>
        public readonly Dictionary<GameObject, int> accountedMissing = new Dictionary<GameObject, int>();

        /// <summary>
        /// How many broken components this plan will clear. Not the same as
        /// actions.Count: a marker that was already put back on an earlier run
        /// leaves its broken component behind, and clearing that is the whole
        /// job the second time.
        /// </summary>
        public int BrokenComponents => accountedMissing.Values.Sum();
    }

    /// <summary>
    /// Puts back the AvatarVCS marker components that a package update turned
    /// into "missing script", with the guids they had.
    ///
    /// Up to 0.8.0-poc the package shipped without .meta files, so Unity
    /// invented a GUID for every script on each install and an update handed
    /// them new ones -- breaking every reference a scene held to
    /// AvatarVcsRoot/AvatarVcsContainer/AvatarVcsTrackedReference/
    /// AvatarVcsUntracked. Losing AvatarVcsRoot is the expensive one: its
    /// avatarGuid is the key the avatar's whole commit history is stored
    /// under (CommitPaths), so re-running Ensure Root gets a working tool
    /// with an empty history beside the real one.
    ///
    /// Unity keeps a missing component's serialized data in the scene file
    /// even though the API can't read it, so the guids are still there to be
    /// recovered -- which is why this reads the file on disk rather than the
    /// loaded scene, and refuses to run on a scene with unsaved changes.
    /// </summary>
    public static class MarkerRepair
    {
        public static MarkerRepairPlan Plan()
        {
            var plan = new MarkerRepairPlan();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) PlanScene(scene, plan);
            }

            return plan;
        }

        private static void PlanScene(Scene scene, MarkerRepairPlan plan)
        {
            if (string.IsNullOrEmpty(scene.path) || !File.Exists(scene.path))
            {
                plan.blockers.Add($"'{scene.name}' has never been saved, so there is no file to read the old guids out of.");
                return;
            }

            if (scene.isDirty)
            {
                plan.blockers.Add($"'{scene.name}' has unsaved changes. Save it first -- the guids come from the file on disk, "
                    + "and an unsaved scene's file is out of date.");
                return;
            }

            var sceneText = File.ReadAllText(scene.path);
            var blocks = SceneYamlMarkerScanner.ReadMonoBehaviours(sceneText);

            // A script Unity can still resolve is not broken, whoever owns it.
            var missing = blocks
                .Where(b => string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(b.scriptGuid)))
                .ToList();
            if (missing.Count == 0) return;

            var objectsByFileId = ObjectsByFileId(scene, SceneYamlMarkerScanner.ReadGameObjects(sceneText));
            var resolved = new List<(SceneMarkerBlock block, GameObject go)>();
            foreach (var block in missing)
            {
                if (objectsByFileId.TryGetValue(block.gameObjectFileId, out var go))
                    resolved.Add((block, go));
            }

            if (resolved.Count == 0) return;

            var avatars = FindAvatars(scene, resolved);
            var trackedPaths = new Dictionary<string, HashSet<string>>();

            var groups = resolved
                .GroupBy(r => r.block.scriptGuid)
                .Select(g => new MissingScriptGroup
                {
                    scriptGuid = g.Key,
                    anyBlockHasAvatarGuid = g.Any(r => r.block.avatarGuid != null),
                    anyBlockHasContainerGuid = g.Any(r => r.block.containerGuid != null),
                    allBlocksUnderAvatar = g.All(r => AvatarOf(avatars, r.go) != null),
                    anyBlockPathTracked = g.Any(r => IsRecordedAsTracked(avatars, trackedPaths, r.go)),
                })
                .ToList();

            var kinds = MarkerGuidClassifier.Classify(groups);

            foreach (var (block, go) in resolved)
            {
                var kind = kinds[block.scriptGuid];
                var path = HierarchyPath(go);

                if (kind == MarkerKind.Unknown)
                {
                    // Only worth reporting when it is plausibly ours. Other
                    // packages break their own scripts too, and a user with
                    // one of those doesn't need us listing it.
                    if (AvatarOf(avatars, go) != null)
                        plan.unresolved.Add(path);
                    continue;
                }

                // Counted whether or not a component gets added below. The
                // block was identified as one of ours, so the broken component
                // it describes is ours to clear -- including on a second run,
                // where the marker is already back and the broken one is all
                // that is left. Leaving it there is not cosmetic: Unity, the
                // VRChat SDK and VRCQuestTools all report a missing script,
                // and the user has no way to tell it apart from a real one.
                plan.accountedMissing.TryGetValue(go, out var count);
                plan.accountedMissing[go] = count + 1;

                // Already repaired (or never broken): nothing to add.
                if (HasMarker(go, kind)) continue;

                plan.actions.Add(new MarkerRepairAction
                {
                    target = go,
                    kind = kind,
                    guid = block.avatarGuid ?? block.containerGuid,
                    path = path,
                });
            }
        }

        public static int Apply(MarkerRepairPlan plan)
        {
            if (plan == null) return 0;

            var undoGroup = Undo.GetCurrentGroup();
            var applied = 0;

            foreach (var action in plan.actions)
            {
                if (action.target == null) continue;

                switch (action.kind)
                {
                    case MarkerKind.Root:
                        Undo.AddComponent<AvatarVcsRoot>(action.target).AssignGuid(action.guid);
                        break;
                    case MarkerKind.Container:
                        Undo.AddComponent<AvatarVcsContainer>(action.target).AssignGuid(action.guid);
                        break;
                    case MarkerKind.Tracked:
                        Undo.AddComponent<AvatarVcsTrackedReference>(action.target);
                        break;
                    case MarkerKind.Untracked:
                        Undo.AddComponent<AvatarVcsUntracked>(action.target);
                        break;
                    default:
                        continue;
                }

                applied++;
            }

            var stuck = new List<string>();
            foreach (var pair in plan.accountedMissing)
            {
                var go = pair.Key;
                if (go == null) continue;

                // Removal takes the whole object, so it may only run where
                // every missing script on it is one this plan accounted for.
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go) != pair.Value) continue;

                Undo.RegisterCompleteObjectUndo(go, "Repair AvatarVCS Markers");
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);

                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go) == 0) continue;

                // That call does nothing on a GameObject inside a prefab
                // instance -- restructuring an instance is not something it
                // will do -- and an avatar is a prefab instance, so this is
                // the normal case rather than the exception. Taking the entry
                // out of the object's own component list works there, and the
                // components in question were added to the instance in the
                // first place, so nothing of the prefab's is being removed.
                if (RemoveMissingComponentEntries(go) == 0) stuck.Add(HierarchyPath(go));
            }

            if (stuck.Count > 0)
            {
                Debug.LogWarning($"[AvatarVCS] Restored the markers, but {stuck.Count} broken component(s) could not be "
                    + "removed automatically. Remove them from the Inspector (the \"missing script\" entry) on:\n"
                    + string.Join("\n", stuck.Take(10)));
            }

            if (applied > 0)
            {
                foreach (var scene in plan.actions.Where(a => a.target != null).Select(a => a.target.scene).Distinct())
                    EditorSceneManager.MarkSceneDirty(scene);
            }

            Undo.CollapseUndoOperations(undoGroup);
            return applied;
        }

        /// <summary>
        /// Deletes the missing-script entries from a GameObject's own
        /// component list. Returns how many went.
        ///
        /// The supported API (GameObjectUtility.RemoveMonoBehavioursWithMissingScript)
        /// declines on prefab instances, which is where these markers mostly
        /// live. Editing m_Component directly is the same thing the Inspector's
        /// own "remove" does for a script it can't load.
        /// </summary>
        private static int RemoveMissingComponentEntries(GameObject go)
        {
            var serialized = new SerializedObject(go);
            var components = serialized.FindProperty("m_Component");
            if (components == null || !components.isArray) return 0;

            var removed = 0;
            for (var i = components.arraySize - 1; i >= 0; i--)
            {
                var entry = components.GetArrayElementAtIndex(i).FindPropertyRelative("component");
                if (entry == null || entry.objectReferenceValue != null) continue;

                components.DeleteArrayElementAtIndex(i);
                removed++;
            }

            if (removed > 0) serialized.ApplyModifiedProperties();

            return removed;
        }

        /// <summary>
        /// Every GameObject in the scene, keyed by the local file id the scene
        /// file refers to it by. GlobalObjectId's targetObjectId is that id,
        /// and it is stable across the script going missing -- unlike a name
        /// or a hierarchy path, which is what makes it the thing to match on.
        /// </summary>
        private static Dictionary<long, GameObject> ObjectsByFileId(
            Scene scene, List<SceneGameObjectEntry> sceneObjects)
        {
            // An object that came from a prefab is referred to by an id of the
            // scene's own making, while GlobalObjectId reports the pair (which
            // object of the prefab, which instance) -- so those need the file's
            // own stripped entries to be translated. Everything else is
            // referred to by exactly the id GlobalObjectId gives back.
            var byPrefabIdentity = new Dictionary<(long source, long instance), long>();
            foreach (var entry in sceneObjects)
            {
                if (entry.prefabInstanceFileId != 0)
                    byPrefabIdentity[(entry.sourceFileId, entry.prefabInstanceFileId)] = entry.fileId;
            }

            var map = new Dictionary<long, GameObject>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    var id = GlobalObjectId.GetGlobalObjectIdSlow(transform.gameObject);
                    var target = unchecked((long)id.targetObjectId);
                    var prefab = unchecked((long)id.targetPrefabId);

                    if (prefab == 0)
                    {
                        if (target != 0) map[target] = transform.gameObject;
                        continue;
                    }

                    if (byPrefabIdentity.TryGetValue((target, prefab), out var sceneFileId))
                        map[sceneFileId] = transform.gameObject;
                }
            }

            return map;
        }

        /// <summary>
        /// The avatars in this scene, as (avatar root, avatarGuid). Both a
        /// surviving AvatarVcsRoot and a broken one are usable: the broken
        /// one's block still names the avatarGuid, and the object it sits on
        /// is "[AvatarVCS]", whose parent is the avatar.
        /// </summary>
        private static List<(Transform root, string avatarGuid)> FindAvatars(
            Scene scene, List<(SceneMarkerBlock block, GameObject go)> resolved)
        {
            var avatars = new List<(Transform, string)>();

            foreach (var go in scene.GetRootGameObjects())
            {
                foreach (var marker in go.GetComponentsInChildren<AvatarVcsRoot>(includeInactive: true))
                {
                    if (marker.transform.parent != null)
                        avatars.Add((marker.transform.parent, marker.AvatarGuid));
                }
            }

            foreach (var (block, markerObject) in resolved)
            {
                if (block.avatarGuid == null || markerObject.transform.parent == null) continue;
                avatars.Add((markerObject.transform.parent, block.avatarGuid));
            }

            return avatars;
        }

        private static (Transform root, string avatarGuid)? AvatarOf(
            List<(Transform root, string avatarGuid)> avatars, GameObject go)
        {
            foreach (var avatar in avatars)
            {
                if (avatar.root != null && go.transform.IsChildOf(avatar.root)) return avatar;
            }

            return null;
        }

        /// <summary>
        /// True when a commit recorded this object as a tracked target. Only
        /// AvatarVcsTrackedReference produces an avatarReferences entry, so
        /// this is the one positive identification available for a marker
        /// that serializes no fields of its own.
        /// </summary>
        private static bool IsRecordedAsTracked(
            List<(Transform root, string avatarGuid)> avatars,
            Dictionary<string, HashSet<string>> cache,
            GameObject go)
        {
            var avatar = AvatarOf(avatars, go);
            if (avatar == null) return false;

            var avatarGuid = avatar.Value.avatarGuid;
            if (string.IsNullOrEmpty(avatarGuid)) return false;

            if (!cache.TryGetValue(avatarGuid, out var paths))
            {
                paths = TrackedPathsOfHead(avatarGuid);
                cache[avatarGuid] = paths;
            }

            return paths.Contains(ReferenceResolver.GetRelativePath(go.transform, avatar.Value.root));
        }

        private static HashSet<string> TrackedPathsOfHead(string avatarGuid)
        {
            var paths = new HashSet<string>();
            if (!CommitIdentifier.IsValidShape(avatarGuid)) return paths;

            var head = BranchConfigOps.CurrentHead(CommitStore.LoadConfig(avatarGuid));
            if (string.IsNullOrEmpty(head)) return paths;

            var commit = CommitStore.LoadCommit(avatarGuid, head);
            if (commit?.avatarReferences == null) return paths;

            foreach (var reference in commit.avatarReferences)
            {
                if (reference != null) paths.Add(reference.path ?? string.Empty);
            }

            return paths;
        }

        private static bool HasMarker(GameObject go, MarkerKind kind) => kind switch
        {
            MarkerKind.Root => go.GetComponent<AvatarVcsRoot>() != null,
            MarkerKind.Container => go.GetComponent<AvatarVcsContainer>() != null,
            MarkerKind.Tracked => go.GetComponent<AvatarVcsTrackedReference>() != null,
            MarkerKind.Untracked => go.GetComponent<AvatarVcsUntracked>() != null,
            _ => false,
        };

        private static string HierarchyPath(GameObject go)
        {
            var path = go.name;
            for (var parent = go.transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;

            return path;
        }
    }
}
