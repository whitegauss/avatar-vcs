using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Editor.Model;

namespace AvatarVcs.Editor.History
{
    /// <summary>
    /// Structured commit-to-commit diff. Design doc section 3.3: containers
    /// are compared first (Added/Removed/Changed/Unchanged); a Changed
    /// container's changeNotes hold the field-level detail an expanded UI row
    /// would show.
    /// </summary>
    public static class SnapshotDiffer
    {
        public static List<ContainerDiff> Diff(Commit before, Commit after)
        {
            var beforeById = (before?.containers ?? new List<ContainerSnapshot>())
                .ToDictionary(c => c.containerId);
            var afterById = (after?.containers ?? new List<ContainerSnapshot>())
                .ToDictionary(c => c.containerId);

            var diffs = new List<ContainerDiff>();

            foreach (var id in beforeById.Keys.Union(afterById.Keys).OrderBy(id => id))
            {
                var hasBefore = beforeById.TryGetValue(id, out var b);
                var hasAfter = afterById.TryGetValue(id, out var a);

                if (!hasBefore)
                {
                    diffs.Add(new ContainerDiff
                    {
                        containerId = id,
                        kind = DiffKind.Added,
                        prefabNameAfter = DescribePrefabs(a),
                    });
                    continue;
                }

                if (!hasAfter)
                {
                    diffs.Add(new ContainerDiff
                    {
                        containerId = id,
                        kind = DiffKind.Removed,
                        prefabNameBefore = DescribePrefabs(b),
                    });
                    continue;
                }

                var notes = DescribeChanges(b, a);
                diffs.Add(new ContainerDiff
                {
                    containerId = id,
                    kind = notes.Count > 0 ? DiffKind.Changed : DiffKind.Unchanged,
                    prefabNameBefore = DescribePrefabs(b),
                    prefabNameAfter = DescribePrefabs(a),
                    changeNotes = notes,
                });
            }

            return diffs;
        }

        private static string DescribePrefabs(ContainerSnapshot snapshot) =>
            string.Join(",", snapshot.prefabGuids);

        private static List<string> DescribeChanges(ContainerSnapshot before, ContainerSnapshot after)
        {
            var notes = new List<string>();

            if (!before.prefabGuids.SequenceEqual(after.prefabGuids))
                notes.Add($"prefabGuids: [{string.Join(",", before.prefabGuids)}] -> [{string.Join(",", after.prefabGuids)}]");

            if (before.localPosition != after.localPosition
                || before.localRotation != after.localRotation
                || before.localScale != after.localScale)
                notes.Add("transform changed");

            var beforeFields = FlattenFields(before);
            var afterFields = FlattenFields(after);
            foreach (var key in beforeFields.Keys.Union(afterFields.Keys).OrderBy(k => k))
            {
                beforeFields.TryGetValue(key, out var beforeValue);
                afterFields.TryGetValue(key, out var afterValue);
                if (beforeValue != afterValue)
                    notes.Add($"{key}: '{beforeValue}' -> '{afterValue}'");
            }

            return notes;
        }

        private static Dictionary<string, string> FlattenFields(ContainerSnapshot snapshot)
        {
            var result = new Dictionary<string, string>();
            foreach (var component in snapshot.components)
            {
                foreach (var field in component.fields)
                    result[$"{component.type}@{component.path}.{field.key}"] = field.value;
                foreach (var assetRef in component.assetRefs)
                    result[$"{component.type}@{component.path}.{assetRef.key}"] = assetRef.guid;
            }
            return result;
        }
    }
}
