using System.Linq;
using AvatarVcs.Editor.History;
using AvatarVcs.Editor.Model;
using NUnit.Framework;
using UnityEngine;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// Covers phase 3 task 11 from DesignDoc_avatar-vcs.md section 7.3:
    /// container-level diffs are structured (Added/Removed/Changed/Unchanged),
    /// with field-level detail for Changed containers.
    /// </summary>
    public class SnapshotDifferTests
    {
        private static ContainerSnapshot MakeContainer(string id, string prefabGuid, Vector3? position = null)
        {
            return new ContainerSnapshot
            {
                containerId = id,
                containerGuid = "guid_" + id,
                prefabGuids = { prefabGuid },
                localPosition = position ?? Vector3.zero,
            };
        }

        private static Commit MakeCommit(params ContainerSnapshot[] containers) =>
            new() { containers = containers.ToList() };

        [Test]
        public void Diff_DetectsAddedContainer()
        {
            var before = MakeCommit(MakeContainer("outfit_a", "guid_a"));
            var after = MakeCommit(MakeContainer("outfit_a", "guid_a"), MakeContainer("hair", "guid_hair"));

            var diffs = SnapshotDiffer.Diff(before, after);

            var hairDiff = diffs.Single(d => d.containerId == "hair");
            Assert.AreEqual(DiffKind.Added, hairDiff.kind);
        }

        [Test]
        public void Diff_DetectsRemovedContainer()
        {
            var before = MakeCommit(MakeContainer("outfit_a", "guid_a"), MakeContainer("hair", "guid_hair"));
            var after = MakeCommit(MakeContainer("outfit_a", "guid_a"));

            var diffs = SnapshotDiffer.Diff(before, after);

            var hairDiff = diffs.Single(d => d.containerId == "hair");
            Assert.AreEqual(DiffKind.Removed, hairDiff.kind);
        }

        [Test]
        public void Diff_DetectsUnchangedContainer()
        {
            var before = MakeCommit(MakeContainer("outfit_a", "guid_a"));
            var after = MakeCommit(MakeContainer("outfit_a", "guid_a"));

            var diffs = SnapshotDiffer.Diff(before, after);

            var outfitDiff = diffs.Single(d => d.containerId == "outfit_a");
            Assert.AreEqual(DiffKind.Unchanged, outfitDiff.kind);
            Assert.IsEmpty(outfitDiff.changeNotes);
        }

        [Test]
        public void Diff_DetectsChangedPrefabGuid_WithNote()
        {
            var before = MakeCommit(MakeContainer("hair", "guid_long"));
            var after = MakeCommit(MakeContainer("hair", "guid_short"));

            var diffs = SnapshotDiffer.Diff(before, after);

            var hairDiff = diffs.Single(d => d.containerId == "hair");
            Assert.AreEqual(DiffKind.Changed, hairDiff.kind);
            Assert.IsTrue(hairDiff.changeNotes.Any(n => n.Contains("prefabGuids")));
        }

        [Test]
        public void Diff_DetectsTransformChange_WithNote()
        {
            var before = MakeCommit(MakeContainer("hair", "guid_a", Vector3.zero));
            var after = MakeCommit(MakeContainer("hair", "guid_a", new Vector3(1, 0, 0)));

            var diffs = SnapshotDiffer.Diff(before, after);

            var hairDiff = diffs.Single(d => d.containerId == "hair");
            Assert.AreEqual(DiffKind.Changed, hairDiff.kind);
            Assert.IsTrue(hairDiff.changeNotes.Any(n => n.Contains("transform")));
        }

        [Test]
        public void Diff_DetectsComponentFieldChange_WithNote()
        {
            var before = MakeContainer("hair", "guid_a");
            before.components.Add(new ComponentState
            {
                path = "",
                type = "Fake.Component",
                fields = { new FieldValue { key = "prefix", value = "A", type = "string" } },
            });

            var after = MakeContainer("hair", "guid_a");
            after.components.Add(new ComponentState
            {
                path = "",
                type = "Fake.Component",
                fields = { new FieldValue { key = "prefix", value = "B", type = "string" } },
            });

            var diffs = SnapshotDiffer.Diff(MakeCommit(before), MakeCommit(after));

            var hairDiff = diffs.Single(d => d.containerId == "hair");
            Assert.AreEqual(DiffKind.Changed, hairDiff.kind);
            Assert.IsTrue(hairDiff.changeNotes.Any(n => n.Contains("prefix") && n.Contains("'A'") && n.Contains("'B'")));
        }
    }
}
