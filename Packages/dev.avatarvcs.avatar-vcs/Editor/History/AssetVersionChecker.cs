using System;
using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Editor.Model;
using UnityEditor;

namespace AvatarVcs.Editor.History
{
    /// <summary>
    /// Records and checks asset content hashes (design doc section 6.3).
    /// Never blocks anything -- the tool has no asset backup, so a mismatch
    /// only explains why a restored result may look different, it can't be
    /// fixed by this tool.
    /// </summary>
    public static class AssetVersionChecker
    {
        public static List<AssetVersionEntry> RecordVersions(IEnumerable<string> guids)
        {
            var recordedAt = DateTime.UtcNow.ToString("o");
            var entries = new List<AssetVersionEntry>();

            foreach (var guid in guids.Where(g => !string.IsNullOrEmpty(g)).Distinct())
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;

                entries.Add(new AssetVersionEntry
                {
                    guid = guid,
                    assetName = System.IO.Path.GetFileName(path),
                    contentHash = AssetDatabase.GetAssetDependencyHash(path).ToString(),
                    recordedAt = recordedAt,
                });
            }

            return entries;
        }

        /// <summary>
        /// Returns a human-readable warning per entry whose asset is now
        /// missing or has a different content hash than when recorded.
        /// </summary>
        public static List<string> CheckForChanges(List<AssetVersionEntry> recorded)
        {
            var warnings = new List<string>();

            foreach (var entry in recorded)
            {
                var path = AssetDatabase.GUIDToAssetPath(entry.guid);
                if (string.IsNullOrEmpty(path))
                {
                    warnings.Add($"'{entry.assetName}' ({entry.guid}) is no longer in the project.");
                    continue;
                }

                var currentHash = AssetDatabase.GetAssetDependencyHash(path).ToString();
                if (currentHash != entry.contentHash)
                    warnings.Add($"'{entry.assetName}' has changed since this commit was recorded; the result may look different.");
            }

            return warnings;
        }
    }
}
