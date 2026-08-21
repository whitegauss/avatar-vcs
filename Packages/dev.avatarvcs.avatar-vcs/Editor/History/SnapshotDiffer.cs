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
        /// <summary>
        /// Design doc 1.4's avatarReferences/materialSettings are outside
        /// the container model but still worth surfacing when they differ,
        /// so they're folded into the same flat list as extra rows (labeled
        /// "avatarRef:{path}" / "material:{targetPath}[{slot}]") rather than
        /// requiring a second, differently-shaped diff view.
        /// </summary>
        public static List<ContainerDiff> Diff(Commit before, Commit after)
        {
            var diffs = new List<ContainerDiff>();
            diffs.AddRange(DiffContainers(before, after));
            diffs.AddRange(DiffAvatarReferences(before, after));
            diffs.AddRange(DiffMaterialSettings(before, after));
            return diffs;
        }

        private static List<ContainerDiff> DiffContainers(Commit before, Commit after) =>
            DiffByKey(
                before?.containers ?? new List<ContainerSnapshot>(),
                after?.containers ?? new List<ContainerSnapshot>(),
                c => c.containerId,
                id => id,
                DescribeContainerChanges,
                DescribePrefabs,
                DescribePrefabs);

        private static List<ContainerDiff> DiffAvatarReferences(Commit before, Commit after) =>
            DiffByKey(
                before?.avatarReferences ?? new List<AvatarReferenceState>(),
                after?.avatarReferences ?? new List<AvatarReferenceState>(),
                r => r.path,
                path => $"avatarRef:{path}",
                DescribeAvatarReferenceChanges);

        private static List<ContainerDiff> DiffMaterialSettings(Commit before, Commit after) =>
            DiffByKey(
                before?.materialSettings ?? new List<MaterialSettingsState>(),
                after?.materialSettings ?? new List<MaterialSettingsState>(),
                m => $"{m.targetPath}[{m.slot}]",
                key => $"material:{key}",
                DescribeMaterialSettingsChanges);

        /// <summary>
        /// Shared shape behind DiffContainers/DiffAvatarReferences/
        /// DiffMaterialSettings: key both snapshots by keySelector, union the
        /// keys, and classify each as Added/Removed/Changed/Unchanged.
        /// describeBefore/describeAfter are optional -- only containers show
        /// a prefab summary on their diff rows.
        /// </summary>
        private static List<ContainerDiff> DiffByKey<T>(
            IEnumerable<T> beforeItems,
            IEnumerable<T> afterItems,
            System.Func<T, string> keySelector,
            System.Func<string, string> label,
            System.Func<T, T, List<string>> changeNotes,
            System.Func<T, string> describeBefore = null,
            System.Func<T, string> describeAfter = null)
        {
            var beforeByKey = beforeItems.ToDictionary(keySelector);
            var afterByKey = afterItems.ToDictionary(keySelector);

            var diffs = new List<ContainerDiff>();
            foreach (var key in beforeByKey.Keys.Union(afterByKey.Keys).OrderBy(k => k))
            {
                var id = label(key);
                var hasBefore = beforeByKey.TryGetValue(key, out var b);
                var hasAfter = afterByKey.TryGetValue(key, out var a);

                if (!hasBefore)
                {
                    diffs.Add(new ContainerDiff
                    {
                        containerId = id,
                        kind = DiffKind.Added,
                        prefabNameAfter = describeAfter?.Invoke(a),
                    });
                    continue;
                }

                if (!hasAfter)
                {
                    diffs.Add(new ContainerDiff
                    {
                        containerId = id,
                        kind = DiffKind.Removed,
                        prefabNameBefore = describeBefore?.Invoke(b),
                    });
                    continue;
                }

                var notes = changeNotes(b, a);
                diffs.Add(new ContainerDiff
                {
                    containerId = id,
                    kind = notes.Count > 0 ? DiffKind.Changed : DiffKind.Unchanged,
                    prefabNameBefore = describeBefore?.Invoke(b),
                    prefabNameAfter = describeAfter?.Invoke(a),
                    changeNotes = notes,
                });
            }

            return diffs;
        }

        private static string DescribePrefabs(ContainerSnapshot snapshot) =>
            string.Join(",", snapshot.prefabGuids);

        private static List<string> DescribeContainerChanges(ContainerSnapshot before, ContainerSnapshot after)
        {
            var notes = new List<string>();

            if (!before.prefabGuids.SequenceEqual(after.prefabGuids))
                notes.Add($"prefabGuids: [{string.Join(",", before.prefabGuids)}] -> [{string.Join(",", after.prefabGuids)}]");

            if (before.localPosition != after.localPosition
                || before.localRotation != after.localRotation
                || before.localScale != after.localScale)
                notes.Add("transform changed");

            if (before.tag != after.tag)
                notes.Add($"tag: '{before.tag}' -> '{after.tag}'");

            if (before.activeSelf != after.activeSelf)
                notes.Add($"active: {before.activeSelf} -> {after.activeSelf}");

            if (before.layer != after.layer)
                notes.Add($"layer: {before.layer} -> {after.layer}");

            notes.AddRange(DiffMap(FlattenFields(before), FlattenFields(after),
                (key, b, a) => $"{key}: '{b}' -> '{a}'"));

            return notes;
        }

        private static List<string> DescribeAvatarReferenceChanges(AvatarReferenceState before, AvatarReferenceState after)
        {
            var notes = new List<string>();

            notes.AddRange(DiffMap(
                before.blendShapes.ToDictionary(s => s.name, s => s.weight),
                after.blendShapes.ToDictionary(s => s.name, s => s.weight),
                (name, b, a) => $"blendShape '{name}': {b} -> {a}"));

            notes.AddRange(DiffMap(
                before.materials.ToDictionary(m => m.slot, m => m.guid),
                after.materials.ToDictionary(m => m.slot, m => m.guid),
                (slot, b, a) => $"material slot {slot}: '{b}' -> '{a}'"));

            return notes;
        }

        private static List<string> DescribeMaterialSettingsChanges(MaterialSettingsState before, MaterialSettingsState after)
        {
            var notes = new List<string>();

            if (before.sourceMaterialGuid != after.sourceMaterialGuid)
                notes.Add($"sourceMaterialGuid: '{before.sourceMaterialGuid}' -> '{after.sourceMaterialGuid}'");
            if (before.shader != after.shader)
                notes.Add($"shader: '{before.shader}' -> '{after.shader}'");

            notes.AddRange(DiffMap(
                before.properties.ToDictionary(p => p.name, p => p.value),
                after.properties.ToDictionary(p => p.name, p => p.value),
                (name, b, a) => $"{name}: '{b}' -> '{a}'"));

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
                foreach (var sceneRef in component.sceneRefs)
                    result[$"{component.type}@{component.path}.{sceneRef.key}"] = $"{sceneRef.path} ({sceneRef.type})";
            }
            return result;
        }

        /// <summary>
        /// Union two before/after maps by key, in key order, and emit one
        /// note per differing value via describe. Shared by every "diff a
        /// flat field-name/slot -> value map" spot above (container fields,
        /// blend shapes, material slots, shader properties).
        /// </summary>
        private static IEnumerable<string> DiffMap<TKey, TValue>(
            Dictionary<TKey, TValue> before,
            Dictionary<TKey, TValue> after,
            System.Func<TKey, TValue, TValue, string> describe)
        {
            foreach (var key in before.Keys.Union(after.Keys).OrderBy(k => k))
            {
                before.TryGetValue(key, out var b);
                after.TryGetValue(key, out var a);
                if (!EqualityComparer<TValue>.Default.Equals(b, a))
                    yield return describe(key, b, a);
            }
        }
    }
}
