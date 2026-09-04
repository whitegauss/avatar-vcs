using System.Collections.Generic;
using System.IO;
using System.Linq;
using AvatarVcs.Core.History;
using AvatarVcs.Runtime;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.History
{
    /// <summary>
    /// Enumerates the avatar histories stored under
    /// ProjectSettings/AvatarVcs/avatars and works out which of them still
    /// belong to an avatar in this project.
    ///
    /// The "still belongs" question is the dangerous one: this drives deletion
    /// of version-control history, and a Unity project holds many scenes of
    /// which at most a few are open. Looking only at the open scene would call
    /// every avatar in every other scene orphaned. So the answer comes from
    /// two sources, and either one is enough to keep a history:
    ///
    ///   1. AvatarVcsRoot components currently loaded -- covers an avatar in
    ///      an open scene, including one never saved, whose guid is in no file
    ///      yet.
    ///   2. The text of every scene and prefab asset -- covers every avatar in
    ///      a closed scene or saved inside a prefab. AvatarGuid is a plain
    ///      serialized string, so it appears verbatim in the YAML.
    /// </summary>
    public static class AvatarHistoryInventory
    {
        public static List<AvatarHistoryInfo> Scan()
        {
            var guids = StoredAvatarGuids();
            if (guids.Count == 0) return new List<AvatarHistoryInfo>();

            var referenced = FindReferencedGuids(guids);

            return guids.Select(guid =>
            {
                var index = CommitStore.LoadIndex(guid);
                var entries = index?.entries ?? new List<CommitIndexEntry>();
                return new AvatarHistoryInfo
                {
                    avatarGuid = guid,
                    isReferenced = referenced.Contains(guid),
                    commitCount = entries.Count,
                    newestCommitTimestamp = entries
                        .Select(e => e.timestamp)
                        .Where(t => !string.IsNullOrEmpty(t))
                        .OrderByDescending(t => t, System.StringComparer.Ordinal)
                        .FirstOrDefault(),
                    byteSize = DirectorySize(CommitPaths.AvatarDir(guid)),
                };
            }).ToList();
        }

        private static List<string> StoredAvatarGuids()
        {
            if (!Directory.Exists(CommitPaths.AvatarsRoot)) return new List<string>();

            return Directory.GetDirectories(CommitPaths.AvatarsRoot)
                .Select(Path.GetFileName)
                // Anything not shaped like an id was not written by us; leave
                // it alone rather than reporting it as deletable.
                .Where(CommitIdentifier.IsValidShape)
                .OrderBy(g => g, System.StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Which of candidates is still carried by an AvatarVcsRoot somewhere.
        /// Every scene/prefab file is read once and checked against the whole
        /// candidate set, rather than once per candidate, and the scan stops
        /// as soon as every candidate has been accounted for.
        /// </summary>
        private static HashSet<string> FindReferencedGuids(IReadOnlyCollection<string> candidates)
        {
            var found = new HashSet<string>();
            var remaining = new HashSet<string>(candidates);

            foreach (var root in Resources.FindObjectsOfTypeAll<AvatarVcsRoot>())
            {
                if (root == null || string.IsNullOrEmpty(root.AvatarGuid)) continue;
                if (remaining.Remove(root.AvatarGuid)) found.Add(root.AvatarGuid);
            }
            if (remaining.Count == 0) return found;

            var assetPaths = AssetDatabase.FindAssets("t:SceneAsset t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p) && p.StartsWith("Assets/", System.StringComparison.Ordinal))
                .Distinct()
                .ToList();

            try
            {
                for (var i = 0; i < assetPaths.Count && remaining.Count > 0; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "AvatarVCS", $"Looking for avatars ({i + 1}/{assetPaths.Count})",
                            (float)i / assetPaths.Count))
                    {
                        // Cancelled: treat every not-yet-cleared candidate as
                        // still referenced. A partial scan must never widen
                        // the deletion set.
                        found.UnionWith(remaining);
                        return found;
                    }

                    foreach (var guid in GuidsAppearingIn(assetPaths[i], remaining))
                    {
                        remaining.Remove(guid);
                        found.Add(guid);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return found;
        }

        /// <summary>
        /// Which of needles occur in the file at path, read in fixed-size
        /// chunks rather than all at once.
        ///
        /// A project that manages several avatars is exactly the project with
        /// large avatar prefabs, and File.ReadAllText on each of them would
        /// allocate the whole file as a string just to run Contains. Chunks
        /// overlap by (needle length - 1) characters so a guid straddling a
        /// boundary is still found.
        /// </summary>
        private static List<string> GuidsAppearingIn(string path, ICollection<string> needles)
        {
            var hits = new List<string>();
            if (needles.Count == 0) return hits;

            var overlap = needles.Max(n => n.Length) - 1;
            const int chunkSize = 1 << 16;
            var buffer = new char[chunkSize + overlap];

            try
            {
                using var reader = new StreamReader(path);
                var carried = 0;
                while (true)
                {
                    var read = reader.Read(buffer, carried, chunkSize);
                    if (read <= 0) break;

                    var window = new string(buffer, 0, carried + read);
                    foreach (var needle in needles)
                        if (!hits.Contains(needle) && window.Contains(needle))
                            hits.Add(needle);

                    if (hits.Count == needles.Count) break;

                    // Carry the tail forward so the next window can still
                    // match a needle split across this boundary.
                    carried = System.Math.Min(overlap, carried + read);
                    window.CopyTo(window.Length - carried, buffer, 0, carried);
                }
            }
            catch
            {
                // Binary-serialized scene, locked file, unreadable encoding:
                // this file simply clears nothing.
            }

            return hits;
        }

        private static long DirectorySize(string path)
        {
            if (!Directory.Exists(path)) return 0;
            long total = 0;
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch { /* vanished mid-scan; it contributes nothing */ }
            }
            return total;
        }
    }
}
