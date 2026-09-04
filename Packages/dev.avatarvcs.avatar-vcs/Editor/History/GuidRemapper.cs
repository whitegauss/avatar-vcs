using System;
using System.Collections.Generic;
using System.IO;
using AvatarVcs.Core.History;
using AvatarVcs.Core.Model;
using UnityEngine;

namespace AvatarVcs.Editor.History
{
    /// <summary>
    /// Project-wide GUID remapping (design doc section 6.4): a re-imported
    /// asset gets a new GUID, which would otherwise make every commit that
    /// referenced it unresolvable forever. Once a user maps old -> new, every
    /// future resolution applies it automatically without asking again.
    /// Chain-following/cycle-detection itself lives in
    /// AvatarVcs.Core.History.GuidRemapResolver; this class is the I/O half
    /// (file load/save, caching, and the cycle-warning log).
    /// </summary>
    public static class GuidRemapper
    {
        private static string ConfigPath => CommitPaths.GuidRemapFile;

        // Separate from Load()'s cache: Load() must keep returning an
        // independent snapshot each call (callers mutate the result in
        // place), while this cache exists purely to spare the many Resolve()
        // calls in a single checkout's restore loop from re-reading the file
        // and rebuilding the lookup index. Invalidated on every Save().
        private static Dictionary<string, string> resolveIndexCache;

        /// <summary>
        /// Returns the remapped GUID if one is recorded, else guid unchanged.
        /// Follows chained mappings (A-&gt;B-&gt;C resolves A to C) with a cycle guard.
        /// </summary>
        public static string Resolve(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return guid;

            resolveIndexCache ??= GuidRemapResolver.BuildIndex(Load());
            var resolution = GuidRemapResolver.Resolve(resolveIndexCache, guid);
            if (resolution.CycleDetected)
                Debug.LogWarning($"[AvatarVCS] GUID remapping for '{guid}' hit a cycle; using '{resolution.Guid}' unresolved.");
            return resolution.Guid;
        }

        public static void AddMapping(string oldGuid, string newGuid)
        {
            if (string.IsNullOrEmpty(oldGuid)) throw new ArgumentException("oldGuid must not be empty.", nameof(oldGuid));
            if (string.IsNullOrEmpty(newGuid)) throw new ArgumentException("newGuid must not be empty.", nameof(newGuid));

            var config = Load();
            GuidRemapResolver.AddOrUpdate(config, oldGuid, newGuid);

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
        /// Writes through AtomicFile, the same writer CommitStore uses -- not
        /// merely the same approach. A crash or disk-full partway through a
        /// direct File.WriteAllText would leave a truncated file that
        /// permanently breaks every future Load() for this config.
        /// </summary>
        public static void Save(GuidRemapConfig config)
        {
            // This file had been left out of KAN-18's flush-to-disk
            // guarantee: a torn guid-remapping file breaks prefab resolution
            // for every commit that relies on a remap.
            AtomicFile.WriteAllText(ConfigPath, JsonUtility.ToJson(config, true));
            resolveIndexCache = null;
        }
    }
}
