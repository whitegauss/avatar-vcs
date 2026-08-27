using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Core.Model;

namespace AvatarVcs.Core.History
{
    /// <summary>
    /// Result of following a GUID remapping chain: the resolved guid, and
    /// whether resolution hit a cycle (in which case Guid is the unresolved
    /// value at the point the cycle was detected, not any particular node of
    /// the cycle). Not a readonly struct: see BlockedCommit's doc comment for
    /// why (object-initializer fields vs. C#'s per-field readonly rule).
    /// </summary>
    public struct GuidResolution
    {
        public string Guid;
        public bool CycleDetected;
    }

    /// <summary>
    /// Project-wide GUID remapping (design doc section 6.4): a re-imported
    /// asset gets a new GUID, which would otherwise make every commit that
    /// referenced it unresolvable forever. Pure chain-following and cycle
    /// detection, split out of GuidRemapper so it can be tested without a
    /// project or a ProjectSettings file.
    /// </summary>
    public static class GuidRemapResolver
    {
        /// <summary>
        /// Skips a mapping that's null, or whose oldGuid/newGuid is
        /// null/empty, before it ever reaches the dictionary -- matches
        /// SnapshotDiffer.SafeToDictionary's "if (key == null) continue;"
        /// and the reasoning behind it: GuidRemapConfig is deserialized from
        /// the hand-editable ProjectSettings/AvatarVcs/guid-remapping.json,
        /// where a bare "{}" entry yields oldGuid == null, and
        /// Dictionary&lt;string,string&gt;'s indexer/ContainsKey throw
        /// ArgumentNullException on a null key. A corrupt/hand-edited entry
        /// should degrade to "ignored", not crash every resolution.
        /// First-mapping-wins if oldGuid somehow repeats (shouldn't happen
        /// via AddOrUpdate, which overwrites in place, but a hand-edited
        /// config could contain it) -- matches the linear-scan
        /// FirstOrDefault this replaces.
        /// </summary>
        public static Dictionary<string, string> BuildIndex(GuidRemapConfig config)
        {
            var index = new Dictionary<string, string>();
            foreach (var mapping in config.mappings)
            {
                if (mapping == null) continue;
                if (string.IsNullOrEmpty(mapping.oldGuid) || string.IsNullOrEmpty(mapping.newGuid)) continue;
                if (!index.ContainsKey(mapping.oldGuid))
                    index[mapping.oldGuid] = mapping.newGuid;
            }
            return index;
        }

        /// <summary>
        /// Follows the remapping chain (A-&gt;B-&gt;C resolves A to C) with a cycle
        /// guard. Hop budget is index.Count (the number of distinct source
        /// guids): a correct, non-cycling chain can visit at most that many
        /// distinct nodes, so exhausting the budget while the current node
        /// still has an outgoing mapping means a cycle. A null/empty next
        /// hop stops the walk immediately rather than continuing with it as
        /// current -- BuildIndex never produces one, but this keeps Resolve
        /// itself safe against a hand-built index that does.
        /// </summary>
        public static GuidResolution Resolve(IReadOnlyDictionary<string, string> index, string guid)
        {
            if (string.IsNullOrEmpty(guid)) return new GuidResolution { Guid = guid, CycleDetected = false };

            var current = guid;
            var hops = 0;
            var cap = index.Count;
            for (; hops < cap; hops++)
            {
                if (!index.TryGetValue(current, out var next)) break;
                if (string.IsNullOrEmpty(next)) break;
                current = next;
            }

            var cycleDetected = hops == cap && index.ContainsKey(current);
            return new GuidResolution { Guid = current, CycleDetected = cycleDetected };
        }

        public static GuidResolution Resolve(GuidRemapConfig config, string guid) =>
            Resolve(BuildIndex(config), guid);

        public static void AddOrUpdate(GuidRemapConfig config, string oldGuid, string newGuid)
        {
            var existing = config.mappings.FirstOrDefault(m => m.oldGuid == oldGuid);
            if (existing != null)
                existing.newGuid = newGuid;
            else
                config.mappings.Add(new GuidRemapEntry { oldGuid = oldGuid, newGuid = newGuid });
        }
    }
}
