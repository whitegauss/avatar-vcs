using System;
using System.Collections.Generic;
using AvatarVcs.Core.Model;

namespace AvatarVcs.Core.History
{
    /// <summary>
    /// Result of probing one recorded asset's current on-disk state, for
    /// AssetVersionComparer.BuildWarnings. Not a readonly struct: see
    /// BlockedCommit's doc comment for why (object-initializer fields vs.
    /// C#'s per-field readonly rule).
    /// </summary>
    public struct AssetVersionProbe
    {
        public bool Exists;
        public string ContentHash;
    }

    /// <summary>
    /// Records and checks asset content hashes (design doc section 6.3),
    /// with the AssetDatabase lookups themselves left to the caller (via
    /// tuples in, or a probe callback) so the assembly/comparison logic can
    /// be tested without a project. Never blocks anything -- the tool has no
    /// asset backup, so a mismatch only explains why a restored result may
    /// look different, it can't be fixed by this tool.
    /// </summary>
    public static class AssetVersionComparer
    {
        public static List<AssetVersionEntry> BuildEntries(
            IEnumerable<(string guid, string assetName, string contentHash)> assets, string recordedAt)
        {
            var entries = new List<AssetVersionEntry>();

            foreach (var (guid, assetName, contentHash) in assets)
            {
                entries.Add(new AssetVersionEntry
                {
                    guid = guid,
                    assetName = assetName,
                    contentHash = contentHash,
                    recordedAt = recordedAt,
                });
            }

            return entries;
        }

        /// <summary>
        /// Returns a human-readable warning per entry whose asset is now
        /// missing or has a different content hash than when recorded.
        /// probe is expected to resolve entry.guid through the current GUID
        /// remapping before looking it up -- keep that order, getting it
        /// backwards was a real bug (see AssetVersionAndGuidRemapTests).
        /// </summary>
        public static List<string> BuildWarnings(IEnumerable<AssetVersionEntry> recorded, Func<string, AssetVersionProbe> probe)
        {
            var warnings = new List<string>();
            if (recorded == null) return warnings;

            foreach (var entry in recorded)
            {
                var result = probe(entry.guid);
                if (!result.Exists)
                {
                    warnings.Add($"'{entry.assetName}' ({entry.guid}) is no longer in the project.");
                    continue;
                }

                if (result.ContentHash != entry.contentHash)
                    warnings.Add($"'{entry.assetName}' has changed since this commit was recorded; the result may look different.");
            }

            return warnings;
        }
    }
}
