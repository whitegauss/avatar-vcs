using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AvatarVcs.Core.History;
using AvatarVcs.Core.Model;
using UnityEditor;

namespace AvatarVcs.Editor.History
{
    /// <summary>
    /// Records and checks asset content hashes (design doc section 6.3).
    /// Never blocks anything -- the tool has no asset backup, so a mismatch
    /// only explains why a restored result may look different, it can't be
    /// fixed by this tool. Entry assembly and warning-building themselves
    /// live in AvatarVcs.Core.History.AssetVersionComparer; this class is
    /// the I/O half (AssetDatabase lookups and the GUID-remap-then-lookup
    /// probe).
    /// </summary>
    public static class AssetVersionChecker
    {
        public static List<AssetVersionEntry> RecordVersions(IEnumerable<string> guids)
        {
            var recordedAt = DateTime.UtcNow.ToString("o");

            var assets = guids.Where(g => !string.IsNullOrEmpty(g)).Distinct()
                .Select(guid => (guid, path: AssetDatabase.GUIDToAssetPath(guid)))
                .Where(x => !string.IsNullOrEmpty(x.path))
                .Select(x => (
                    guid: x.guid,
                    assetName: Path.GetFileName(x.path),
                    contentHash: AssetDatabase.GetAssetDependencyHash(x.path).ToString()));

            return AssetVersionComparer.BuildEntries(assets, recordedAt);
        }

        /// <summary>
        /// Returns a human-readable warning per entry whose asset is now
        /// missing or has a different content hash than when recorded.
        /// </summary>
        public static List<string> CheckForChanges(List<AssetVersionEntry> recorded)
        {
            return AssetVersionComparer.BuildWarnings(recorded, guid =>
            {
                var path = AssetDatabase.GUIDToAssetPath(GuidRemapper.Resolve(guid));
                return string.IsNullOrEmpty(path)
                    ? new AssetVersionProbe { Exists = false }
                    : new AssetVersionProbe { Exists = true, ContentHash = AssetDatabase.GetAssetDependencyHash(path).ToString() };
            });
        }
    }
}
