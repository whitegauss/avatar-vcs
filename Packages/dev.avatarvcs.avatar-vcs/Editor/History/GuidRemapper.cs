using System;
using System.IO;
using System.Linq;
using AvatarVcs.Editor.Model;
using UnityEngine;

namespace AvatarVcs.Editor.History
{
    /// <summary>
    /// Project-wide GUID remapping (design doc section 6.4): a re-imported
    /// asset gets a new GUID, which would otherwise make every commit that
    /// referenced it unresolvable forever. Once a user maps old -> new, every
    /// future resolution applies it automatically without asking again.
    /// </summary>
    public static class GuidRemapper
    {
        private static string ConfigPath =>
            Path.Combine("ProjectSettings", "AvatarVcs", "guid-remapping.json").Replace('\\', '/');

        // Separate from Load()'s cache: Load() must keep returning an
        // independent snapshot each call (callers mutate the result in
        // place), while this cache exists purely to spare the many Resolve()
        // calls in a single checkout's restore loop from re-reading the file.
        // Invalidated on every Save().
        private static GuidRemapConfig resolveCache;

        /// <summary>
        /// Returns the remapped GUID if one is recorded, else guid unchanged.
        /// Follows chained mappings (A-&gt;B-&gt;C resolves A to C) with a cycle guard.
        /// </summary>
        public static string Resolve(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return guid;

            resolveCache ??= Load();
            var mappings = resolveCache.mappings;
            var current = guid;
            // Cap hops at the mapping count: a correct chain can be at most
            // this long, so hitting the cap means a cycle.
            var hops = 0;
            for (; hops < mappings.Count; hops++)
            {
                var entry = mappings.FirstOrDefault(m => m.oldGuid == current);
                if (entry == null) break;
                current = entry.newGuid;
            }
            if (hops == mappings.Count && mappings.Any(m => m.oldGuid == current))
                Debug.LogWarning($"[AvatarVCS] GUID remapping for '{guid}' hit a cycle; using '{current}' unresolved.");
            return current;
        }

        public static void AddMapping(string oldGuid, string newGuid)
        {
            if (string.IsNullOrEmpty(oldGuid)) throw new ArgumentException("oldGuid must not be empty.", nameof(oldGuid));
            if (string.IsNullOrEmpty(newGuid)) throw new ArgumentException("newGuid must not be empty.", nameof(newGuid));

            var config = Load();
            var existing = config.mappings.FirstOrDefault(m => m.oldGuid == oldGuid);
            if (existing != null)
                existing.newGuid = newGuid;
            else
                config.mappings.Add(new GuidRemapEntry { oldGuid = oldGuid, newGuid = newGuid });

            Save(config);
        }

        /// <summary>
        /// JsonUtility.FromJson throws on malformed JSON (e.g. a file
        /// truncated by a crash mid-write, or a bad manual edit). Falls back
        /// to an empty config and warns instead of propagating -- matching
        /// CommitStore.TryLoadJson, since "no mappings recorded yet" is
        /// already a normal, recoverable state every caller here handles.
        /// </summary>
        public static GuidRemapConfig Load()
        {
            if (!File.Exists(ConfigPath)) return new GuidRemapConfig();

            try
            {
                return JsonUtility.FromJson<GuidRemapConfig>(File.ReadAllText(ConfigPath)) ?? new GuidRemapConfig();
            }
            catch (Exception e) when (e is ArgumentException or IOException)
            {
                Debug.LogWarning($"[AvatarVCS] Could not parse '{ConfigPath}' as {nameof(GuidRemapConfig)}; treating as empty. {e.Message}");
                return new GuidRemapConfig();
            }
        }

        /// <summary>
        /// Writes via a temp file in the same directory, then swaps it into
        /// place, matching CommitStore.WriteAtomically -- a crash or
        /// disk-full partway through a direct File.WriteAllText would
        /// otherwise leave a truncated file that permanently breaks every
        /// future Load() for this config.
        /// </summary>
        public static void Save(GuidRemapConfig config)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            var tempPath = $"{ConfigPath}.tmp";
            File.WriteAllText(tempPath, JsonUtility.ToJson(config, true));
            if (File.Exists(ConfigPath))
                File.Replace(tempPath, ConfigPath, null);
            else
                File.Move(tempPath, ConfigPath);
            resolveCache = null;
        }
    }
}
