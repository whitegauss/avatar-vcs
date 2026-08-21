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

        private static List<ContainerDiff> DiffContainers(Commit before, Commit after)
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

        private static List<ContainerDiff> DiffAvatarReferences(Commit before, Commit after)
        {
            var beforeByPath = (before?.avatarReferences ?? new List<AvatarReferenceState>()).ToDictionary(r => r.path);
            var afterByPath = (after?.avatarReferences ?? new List<AvatarReferenceState>()).ToDictionary(r => r.path);

            var diffs = new List<ContainerDiff>();
            foreach (var path in beforeByPath.Keys.Union(afterByPath.Keys).OrderBy(p => p))
            {
                var label = $"avatarRef:{path}";
                var hasBefore = beforeByPath.TryGetValue(path, out var b);
                var hasAfter = afterByPath.TryGetValue(path, out var a);

                if (!hasBefore) { diffs.Add(new ContainerDiff { containerId = label, kind = DiffKind.Added }); continue; }
                if (!hasAfter) { diffs.Add(new ContainerDiff { containerId = label, kind = DiffKind.Removed }); continue; }

                var notes = new List<string>();

                var beforeShapes = b.blendShapes.ToDictionary(s => s.name, s => s.weight);
                var afterShapes = a.blendShapes.ToDictionary(s => s.name, s => s.weight);
                foreach (var name in beforeShapes.Keys.Union(afterShapes.Keys).OrderBy(n => n))
                {
                    beforeShapes.TryGetValue(name, out var bw);
                    afterShapes.TryGetValue(name, out var aw);
                    if (bw != aw)
                        notes.Add($"blendShape '{name}': {bw} -> {aw}");
                }

                var beforeMats = b.materials.ToDictionary(m => m.slot, m => m.guid);
                var afterMats = a.materials.ToDictionary(m => m.slot, m => m.guid);
                foreach (var slot in beforeMats.Keys.Union(afterMats.Keys).OrderBy(s => s))
                {
                    beforeMats.TryGetValue(slot, out var bg);
                    afterMats.TryGetValue(slot, out var ag);
                    if (bg != ag)
                        notes.Add($"material slot {slot}: '{bg}' -> '{ag}'");
                }

                diffs.Add(new ContainerDiff
                {
                    containerId = label,
                    kind = notes.Count > 0 ? DiffKind.Changed : DiffKind.Unchanged,
                    changeNotes = notes,
                });
            }
            return diffs;
        }

        private static List<ContainerDiff> DiffMaterialSettings(Commit before, Commit after)
        {
            var beforeByKey = (before?.materialSettings ?? new List<MaterialSettingsState>())
                .ToDictionary(m => $"{m.targetPath}[{m.slot}]");
            var afterByKey = (after?.materialSettings ?? new List<MaterialSettingsState>())
                .ToDictionary(m => $"{m.targetPath}[{m.slot}]");

            var diffs = new List<ContainerDiff>();
            foreach (var key in beforeByKey.Keys.Union(afterByKey.Keys).OrderBy(k => k))
            {
                var label = $"material:{key}";
                var hasBefore = beforeByKey.TryGetValue(key, out var b);
                var hasAfter = afterByKey.TryGetValue(key, out var a);

                if (!hasBefore) { diffs.Add(new ContainerDiff { containerId = label, kind = DiffKind.Added }); continue; }
                if (!hasAfter) { diffs.Add(new ContainerDiff { containerId = label, kind = DiffKind.Removed }); continue; }

                var notes = new List<string>();
                if (b.sourceMaterialGuid != a.sourceMaterialGuid)
                    notes.Add($"sourceMaterialGuid: '{b.sourceMaterialGuid}' -> '{a.sourceMaterialGuid}'");
                if (b.shader != a.shader)
                    notes.Add($"shader: '{b.shader}' -> '{a.shader}'");

                var beforeProps = b.properties.ToDictionary(p => p.name, p => p.value);
                var afterProps = a.properties.ToDictionary(p => p.name, p => p.value);
                foreach (var name in beforeProps.Keys.Union(afterProps.Keys).OrderBy(n => n))
                {
                    beforeProps.TryGetValue(name, out var bv);
                    afterProps.TryGetValue(name, out var av);
                    if (bv != av)
                        notes.Add($"{name}: '{bv}' -> '{av}'");
                }

                diffs.Add(new ContainerDiff
                {
                    containerId = label,
                    kind = notes.Count > 0 ? DiffKind.Changed : DiffKind.Unchanged,
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

            if (before.tag != after.tag)
                notes.Add($"tag: '{before.tag}' -> '{after.tag}'");

            if (before.activeSelf != after.activeSelf)
                notes.Add($"active: {before.activeSelf} -> {after.activeSelf}");

            if (before.layer != after.layer)
                notes.Add($"layer: {before.layer} -> {after.layer}");

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
                foreach (var sceneRef in component.sceneRefs)
                    result[$"{component.type}@{component.path}.{sceneRef.key}"] = $"{sceneRef.path} ({sceneRef.type})";
            }
            return result;
        }
    }
}
