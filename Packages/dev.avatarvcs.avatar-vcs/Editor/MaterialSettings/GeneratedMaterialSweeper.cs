using System.Collections.Generic;
using System.IO;
using System.Linq;
using AvatarVcs.Core.History;
using AvatarVcs.Core.Model;
using AvatarVcs.Core.Naming;
using AvatarVcs.Editor.History;
using UnityEditor;

namespace AvatarVcs.Editor.MaterialSettings
{
    public sealed class GeneratedMaterialSweepPlan
    {
        /// <summary>Asset paths nothing points at any more.</summary>
        public readonly List<string> deletable = new List<string>();

        public long deletableBytes;

        /// <summary>Generated materials a renderer in an open scene is wearing.</summary>
        public int keptInUse;

        /// <summary>Generated materials some commit still names.</summary>
        public int keptReferenced;

        /// <summary>
        /// A commit that couldn't be read. Its references are unknown, so
        /// nothing may be deleted at all -- the same all-or-nothing rule
        /// AvatarHistoryCleanup uses for an incomplete scan.
        /// </summary>
        public readonly List<string> unreadableCommits = new List<string>();

        public bool Blocked => unreadableCommits.Count > 0;
    }

    /// <summary>
    /// Collects the duplicate materials checkouts leave behind.
    ///
    /// Up to 0.9.0-poc a checkout generated one duplicate per material slot
    /// per commit -- for every slot whose shader was supported, whether or
    /// not the user had ever touched it -- and capture then recorded the
    /// duplicate as the source, so the next checkout duplicated the
    /// duplicate. The avatar this was found on had 552 of them, 82% of every
    /// material in the project. Generating them stopped (MaterialSettingsApplier);
    /// this is how the ones already on disk go away.
    ///
    /// Deletion is deliberately conservative: only assets that look like ours
    /// (GeneratedAssetNaming), that no commit of any avatar names, and that
    /// no renderer in an open scene is wearing.
    /// </summary>
    public static class GeneratedMaterialSweeper
    {
        public static GeneratedMaterialSweepPlan Plan()
        {
            var plan = new GeneratedMaterialSweepPlan();

            var referenced = ReferencedMaterialGuids(plan);
            var inUse = CommitStore.MaterialGuidsUsedInLoadedScenes();

            foreach (var guid in AssetDatabase.FindAssets("t:Material"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!GeneratedAssetNaming.LooksGenerated(path)) continue;

                if (inUse.Contains(guid)) { plan.keptInUse++; continue; }
                if (referenced.Contains(guid)) { plan.keptReferenced++; continue; }

                plan.deletable.Add(path);
                plan.deletableBytes += FileSize(path);
            }

            return plan;
        }

        public static int Apply(GeneratedMaterialSweepPlan plan)
        {
            if (plan == null || plan.Blocked) return 0;

            var deleted = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in plan.deletable)
                {
                    if (AssetDatabase.DeleteAsset(path)) deleted++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            return deleted;
        }

        /// <summary>
        /// Every material guid any commit of any avatar still names --
        /// including sourceMaterialGuid, because the commits written before
        /// this was fixed point at duplicates as their source and deleting
        /// one would make that commit unrestorable.
        /// </summary>
        private static HashSet<string> ReferencedMaterialGuids(GeneratedMaterialSweepPlan plan)
        {
            var referenced = new HashSet<string>();
            if (!Directory.Exists(CommitPaths.AvatarsRoot)) return referenced;

            foreach (var dir in Directory.GetDirectories(CommitPaths.AvatarsRoot))
            {
                var avatarGuid = Path.GetFileName(dir);
                if (!CommitIdentifier.IsValidShape(avatarGuid)) continue;

                foreach (var entry in CommitStore.LoadIndex(avatarGuid).entries)
                {
                    if (string.IsNullOrEmpty(entry?.commitId)) continue;

                    var commit = CommitStore.LoadCommit(avatarGuid, entry.commitId);
                    if (commit == null)
                    {
                        plan.unreadableCommits.Add($"{avatarGuid}/{entry.commitId}");
                        continue;
                    }

                    Collect(commit, referenced);
                }
            }

            return referenced;
        }

        private static void Collect(Commit commit, HashSet<string> into)
        {
            foreach (var guid in commit.generatedAssets ?? new List<string>())
                Add(into, guid);

            CollectMaterialSettings(commit.materialSettings, into);

            foreach (var reference in commit.avatarReferences ?? new List<AvatarReferenceState>())
                CollectMaterialRefs(reference?.materials, into);

            foreach (var container in commit.containers ?? new List<ContainerSnapshot>())
            {
                if (container == null) continue;
                CollectMaterialSettings(container.materialSettings, into);
                CollectMaterialRefs(container.materials, into);
            }
        }

        private static void CollectMaterialSettings(List<MaterialSettingsState> settings, HashSet<string> into)
        {
            foreach (var setting in settings ?? new List<MaterialSettingsState>())
            {
                if (setting == null) continue;
                Add(into, setting.generatedGuid);
                Add(into, setting.sourceMaterialGuid);
            }
        }

        private static void CollectMaterialRefs(List<MaterialRef> materials, HashSet<string> into)
        {
            foreach (var material in materials ?? new List<MaterialRef>())
            {
                if (material != null) Add(into, material.guid);
            }
        }

        private static void Add(HashSet<string> into, string guid)
        {
            // Follows the same remapping a checkout would, so a re-imported
            // material's new guid counts as referenced too (GuidRemapper).
            if (!string.IsNullOrEmpty(guid)) into.Add(GuidRemapper.Resolve(guid));
        }

        private static long FileSize(string path)
        {
            try
            {
                return File.Exists(path) ? new FileInfo(path).Length : 0;
            }
            catch (IOException)
            {
                return 0;
            }
        }
    }
}
