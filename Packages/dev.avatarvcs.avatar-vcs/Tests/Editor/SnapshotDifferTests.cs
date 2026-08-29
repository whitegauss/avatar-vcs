using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Core.Diff;
using AvatarVcs.Core.Model;
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

        [Test]
        public void Diff_DetectsSceneRefChange_WithNote()
        {
            var before = MakeContainer("hair", "guid_a");
            before.components.Add(new ComponentState
            {
                path = "",
                type = "Fake.Component",
                sceneRefs = { new SceneRef { key = "target", path = "Armature/Hips", type = "UnityEngine.Transform" } },
            });

            var after = MakeContainer("hair", "guid_a");
            after.components.Add(new ComponentState
            {
                path = "",
                type = "Fake.Component",
                sceneRefs = { new SceneRef { key = "target", path = "Armature/Chest", type = "UnityEngine.Transform" } },
            });

            var diffs = SnapshotDiffer.Diff(MakeCommit(before), MakeCommit(after));

            var hairDiff = diffs.Single(d => d.containerId == "hair");
            Assert.AreEqual(DiffKind.Changed, hairDiff.kind);
            Assert.IsTrue(hairDiff.changeNotes.Any(n => n.Contains("target") && n.Contains("Hips") && n.Contains("Chest")),
                "a scene reference (e.g. a bone target) changing must show up as a diff note, not be silently ignored");
        }

        [Test]
        public void Diff_DetectsBlendShapeAndMaterialChange_InAvatarReferences()
        {
            var before = new Commit
            {
                avatarReferences =
                {
                    new AvatarReferenceState
                    {
                        path = "Body",
                        blendShapes = { new BlendShapeRef { name = "Shape_A", weight = 0f } },
                        materials = { new MaterialRef { slot = 0, guid = "guid_before" } },
                    },
                },
            };
            var after = new Commit
            {
                avatarReferences =
                {
                    new AvatarReferenceState
                    {
                        path = "Body",
                        blendShapes = { new BlendShapeRef { name = "Shape_A", weight = 100f } },
                        materials = { new MaterialRef { slot = 0, guid = "guid_after" } },
                    },
                },
            };

            var diffs = SnapshotDiffer.Diff(before, after);

            var bodyDiff = diffs.Single(d => d.containerId == "avatarRef:Body");
            Assert.AreEqual(DiffKind.Changed, bodyDiff.kind);
            Assert.IsTrue(bodyDiff.changeNotes.Any(n => n.Contains("Shape_A") && n.Contains("100")));
            Assert.IsTrue(bodyDiff.changeNotes.Any(n => n.Contains("slot 0") && n.Contains("guid_after")));
        }

        [Test]
        public void Diff_DetectsActiveTagAndLayerChange_InAvatarReferences()
        {
            var before = new Commit
            {
                avatarReferences =
                {
                    new AvatarReferenceState
                    {
                        path = "Body",
                        objectStates = { new ObjectStateRef { path = "Toggle", activeSelf = true, tag = "Untagged", layer = 0 } },
                    },
                },
            };
            var after = new Commit
            {
                avatarReferences =
                {
                    new AvatarReferenceState
                    {
                        path = "Body",
                        objectStates = { new ObjectStateRef { path = "Toggle", activeSelf = false, tag = "Player", layer = 3 } },
                    },
                },
            };

            var diffs = SnapshotDiffer.Diff(before, after);

            var bodyDiff = diffs.Single(d => d.containerId == "avatarRef:Body");
            Assert.AreEqual(DiffKind.Changed, bodyDiff.kind);
            Assert.IsTrue(bodyDiff.changeNotes.Any(n => n.Contains("active 'Toggle'") && n.Contains("False") && n.Contains("True")));
            Assert.IsTrue(bodyDiff.changeNotes.Any(n => n.Contains("tag 'Toggle'") && n.Contains("Untagged") && n.Contains("Player")));
            Assert.IsTrue(bodyDiff.changeNotes.Any(n => n.Contains("layer 'Toggle'") && n.Contains("0") && n.Contains("3")));
        }

        [Test]
        public void Diff_DetectsChangedGenericComponentField_InAvatarReferences()
        {
            var before = new Commit
            {
                avatarReferences =
                {
                    new AvatarReferenceState
                    {
                        path = "Body",
                        components =
                        {
                            new ComponentState
                            {
                                path = "Extra",
                                type = "Fake.Component",
                                fields = { new FieldValue { key = "value", value = "1", type = "float" } },
                            },
                        },
                    },
                },
            };
            var after = new Commit
            {
                avatarReferences =
                {
                    new AvatarReferenceState
                    {
                        path = "Body",
                        components =
                        {
                            new ComponentState
                            {
                                path = "Extra",
                                type = "Fake.Component",
                                fields = { new FieldValue { key = "value", value = "2", type = "float" } },
                            },
                        },
                    },
                },
            };

            var diffs = SnapshotDiffer.Diff(before, after);

            var bodyDiff = diffs.Single(d => d.containerId == "avatarRef:Body");
            Assert.AreEqual(DiffKind.Changed, bodyDiff.kind);
            Assert.IsTrue(bodyDiff.changeNotes.Any(n => n.Contains("value") && n.Contains("'1'") && n.Contains("'2'")));
        }

        [Test]
        public void Diff_UnchangedWhenAvatarReferenceComponentsIdentical()
        {
            AvatarReferenceState MakeState() => new()
            {
                path = "Body",
                components =
                {
                    new ComponentState
                    {
                        path = "Extra",
                        type = "Fake.Component",
                        fields = { new FieldValue { key = "value", value = "1", type = "float" } },
                    },
                },
            };

            var before = new Commit { avatarReferences = { MakeState() } };
            var after = new Commit { avatarReferences = { MakeState() } };

            var diffs = SnapshotDiffer.Diff(before, after);

            var bodyDiff = diffs.Single(d => d.containerId == "avatarRef:Body");
            Assert.AreEqual(DiffKind.Unchanged, bodyDiff.kind);
        }

        [Test]
        public void Diff_DetectsAddedAvatarReferencePath()
        {
            var before = new Commit();
            var after = new Commit
            {
                avatarReferences = { new AvatarReferenceState { path = "Body" } },
            };

            var diffs = SnapshotDiffer.Diff(before, after);

            var bodyDiff = diffs.Single(d => d.containerId == "avatarRef:Body");
            Assert.AreEqual(DiffKind.Added, bodyDiff.kind);
        }

        [Test]
        public void Diff_DetectsMaterialSettingsPropertyChange()
        {
            var before = new Commit
            {
                materialSettings =
                {
                    new MaterialSettingsState
                    {
                        targetPath = "Body",
                        slot = 0,
                        sourceMaterialGuid = "guid_src",
                        shader = "lilToon",
                        properties = { new MaterialPropertyValue { name = "_Color", type = "color", value = "1,1,1,1" } },
                    },
                },
            };
            var after = new Commit
            {
                materialSettings =
                {
                    new MaterialSettingsState
                    {
                        targetPath = "Body",
                        slot = 0,
                        sourceMaterialGuid = "guid_src",
                        shader = "lilToon",
                        properties = { new MaterialPropertyValue { name = "_Color", type = "color", value = "1,0,0,1" } },
                    },
                },
            };

            var diffs = SnapshotDiffer.Diff(before, after);

            var matDiff = diffs.Single(d => d.containerId == "material:Body[0]");
            Assert.AreEqual(DiffKind.Changed, matDiff.kind);
            Assert.IsTrue(matDiff.changeNotes.Any(n => n.Contains("_Color") && n.Contains("1,0,0,1")));
        }

        [Test]
        public void Diff_DetectsTagChange_WithNote()
        {
            var before = MakeContainer("hair", "guid_a");
            before.tag = "Untagged";
            var after = MakeContainer("hair", "guid_a");
            after.tag = "EditorOnly";

            var diffs = SnapshotDiffer.Diff(MakeCommit(before), MakeCommit(after));

            var hairDiff = diffs.Single(d => d.containerId == "hair");
            Assert.AreEqual(DiffKind.Changed, hairDiff.kind);
            Assert.IsTrue(hairDiff.changeNotes.Any(n => n.Contains("tag") && n.Contains("EditorOnly")));
        }

        [Test]
        public void Diff_DetectsActiveSelfChange_WithNote()
        {
            var before = MakeContainer("hair", "guid_a");
            before.activeSelf = true;
            var after = MakeContainer("hair", "guid_a");
            after.activeSelf = false;

            var diffs = SnapshotDiffer.Diff(MakeCommit(before), MakeCommit(after));

            var hairDiff = diffs.Single(d => d.containerId == "hair");
            Assert.AreEqual(DiffKind.Changed, hairDiff.kind);
            Assert.IsTrue(hairDiff.changeNotes.Any(n => n.Contains("active") && n.Contains("False")));
        }

        [Test]
        public void Diff_DetectsLayerChange_WithNote()
        {
            var before = MakeContainer("hair", "guid_a");
            before.layer = 0;
            var after = MakeContainer("hair", "guid_a");
            after.layer = 5;

            var diffs = SnapshotDiffer.Diff(MakeCommit(before), MakeCommit(after));

            var hairDiff = diffs.Single(d => d.containerId == "hair");
            Assert.AreEqual(DiffKind.Changed, hairDiff.kind);
            Assert.IsTrue(hairDiff.changeNotes.Any(n => n.Contains("layer") && n.Contains("5")));
        }

        [Test]
        public void Diff_DoesNotThrow_OnDuplicateContainerIds_InHandEditedCommit()
        {
            // A hand-edited/corrupted commit JSON can contain two entries
            // with the same key -- ToDictionary would throw ArgumentException
            // ("same key has already been added") and break the diff view
            // entirely; it must degrade gracefully instead.
            var before = MakeCommit(MakeContainer("hair", "guid_a"));
            var after = MakeCommit(MakeContainer("hair", "guid_b"), MakeContainer("hair", "guid_c"));

            List<ContainerDiff> diffs = null;
            Assert.DoesNotThrow(() => diffs = SnapshotDiffer.Diff(before, after));
            Assert.IsTrue(diffs.Any(d => d.containerId == "hair"));
        }

        [Test]
        public void Diff_DoesNotThrow_OnDuplicateBlendShapeOrObjectStatePaths_InHandEditedCommit()
        {
            var before = new Commit
            {
                avatarReferences =
                {
                    new AvatarReferenceState { path = "Body" },
                },
            };
            var after = new Commit
            {
                avatarReferences =
                {
                    new AvatarReferenceState
                    {
                        path = "Body",
                        blendShapes =
                        {
                            new BlendShapeRef { name = "Shape_A", weight = 10f },
                            new BlendShapeRef { name = "Shape_A", weight = 20f },
                        },
                        objectStates =
                        {
                            new ObjectStateRef { path = "Toggle", activeSelf = true },
                            new ObjectStateRef { path = "Toggle", activeSelf = false },
                        },
                    },
                },
            };

            Assert.DoesNotThrow(() => SnapshotDiffer.Diff(before, after));
        }

        [Test]
        public void Diff_DoesNotThrow_OnDuplicateOrNullMaterialSettings_InHandEditedCommit()
        {
            var before = new Commit
            {
                materialSettings =
                {
                    new MaterialSettingsState { targetPath = "Body", slot = 0, sourceMaterialGuid = "guid_a" },
                    new MaterialSettingsState { targetPath = "Body", slot = 0, sourceMaterialGuid = "guid_b" }, // duplicate slot
                },
            };
            var after = new Commit
            {
                materialSettings =
                {
                    new MaterialSettingsState { targetPath = null, slot = 0, sourceMaterialGuid = "guid_c" }, // null targetPath
                    new MaterialSettingsState { targetPath = "Body", slot = 0, sourceMaterialGuid = "guid_d" },
                },
            };

            List<ContainerDiff> diffs = null;
            Assert.DoesNotThrow(() => diffs = SnapshotDiffer.Diff(before, after));
            Assert.IsNotNull(diffs);
        }

        [Test]
        public void Diff_DoesNotThrow_OnDuplicateOrNullProperties_InMaterialSettings()
        {
            var before = new Commit
            {
                materialSettings =
                {
                    new MaterialSettingsState
                    {
                        targetPath = "Body",
                        slot = 0,
                        properties =
                        {
                            new MaterialPropertyValue { name = "_Color", type = "color", value = "1,1,1,1" },
                            new MaterialPropertyValue { name = "_Color", type = "color", value = "0,0,0,1" }, // duplicate
                        },
                    },
                },
            };
            var after = new Commit
            {
                materialSettings =
                {
                    new MaterialSettingsState
                    {
                        targetPath = "Body",
                        slot = 0,
                        properties =
                        {
                            new MaterialPropertyValue { name = null, type = "float", value = "1.0" }, // null name
                            new MaterialPropertyValue { name = "_Color", type = "color", value = "1,0,0,1" },
                        },
                    },
                },
            };

            List<ContainerDiff> diffs = null;
            Assert.DoesNotThrow(() => diffs = SnapshotDiffer.Diff(before, after));
            Assert.IsNotNull(diffs);
        }

        [Test]
        public void Diff_DoesNotThrow_OnComponentsWithNullOrEmptyFields_InHandEditedCommit()
        {
            var containerA = MakeContainer("hair", "guid_a");
            containerA.components.Add(new ComponentState
            {
                path = "",
                type = "Fake.Component",
                fields = { new FieldValue { key = "duplicateKey", value = "1", type = "int" }, new FieldValue { key = "duplicateKey", value = "2", type = "int" } },
                assetRefs = { new AssetRef { key = "duplicateAsset", guid = "guid1" }, new AssetRef { key = "duplicateAsset", guid = "guid2" } },
                sceneRefs = { new SceneRef { key = "duplicateScene", path = "P1", type = "T" }, new SceneRef { key = "duplicateScene", path = "P2", type = "T" } },
            });

            var containerB = MakeContainer("hair", "guid_a");

            Assert.DoesNotThrow(() => SnapshotDiffer.Diff(MakeCommit(containerA), MakeCommit(containerB)));
        }
    }
}
