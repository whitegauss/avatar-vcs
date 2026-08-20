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

        /// <summary>Returns the remapped GUID if one is recorded, else guid unchanged.</summary>
        public static string Resolve(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return guid;

            var entry = Load().mappings.FirstOrDefault(m => m.oldGuid == guid);
            return entry?.newGuid ?? guid;
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

        public static GuidRemapConfig Load() =>
            File.Exists(ConfigPath) ? JsonUtility.FromJson<GuidRemapConfig>(File.ReadAllText(ConfigPath)) : new GuidRemapConfig();

        public static void Save(GuidRemapConfig config)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonUtility.ToJson(config, true));
        }
    }
}
