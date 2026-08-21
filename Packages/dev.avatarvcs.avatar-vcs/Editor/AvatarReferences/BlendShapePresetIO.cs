using System;
using System.Collections.Generic;
using AvatarVcs.Editor.Model;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.AvatarReferences
{
    /// <summary>
    /// Capture/apply for standalone BlendShape presets (issue #58), separate
    /// from the commit/checkout system entirely -- meant for sharing a
    /// BlendShape configuration outside this tool (e.g. a shape-key pack
    /// sold to another creator), applied by name onto whatever mesh the
    /// importer has, not tied to any particular avatarGuid/commit history.
    /// File I/O (dialogs, JSON read/write) is kept in AvatarVcsMenu; this is
    /// the pure, directly-testable half.
    /// </summary>
    public static class BlendShapePresetIO
    {
        /// <summary>
        /// Every BlendShape on the mesh is captured, including ones
        /// currently at 0 -- same rationale as AvatarReferenceCapture: a
        /// shape explicitly turned down to 0 is a deliberate choice the
        /// preset should reproduce, not something to omit.
        /// </summary>
        public static BlendShapePreset Capture(SkinnedMeshRenderer renderer)
        {
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            if (renderer.sharedMesh == null) throw new ArgumentException("renderer has no mesh.", nameof(renderer));

            var mesh = renderer.sharedMesh;
            var preset = new BlendShapePreset { meshName = mesh.name };
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                preset.blendShapes.Add(new BlendShapeRef
                {
                    name = mesh.GetBlendShapeName(i),
                    weight = renderer.GetBlendShapeWeight(i),
                });
            }

            return preset;
        }

        /// <summary>
        /// Applies by name; a preset entry whose name isn't on the target
        /// mesh is skipped (not an error) -- the whole point is importing
        /// onto a mesh that's similar but not necessarily identical to the
        /// one the preset was exported from. Returns the names that
        /// couldn't be matched, so the caller can report them.
        /// </summary>
        public static List<string> Apply(BlendShapePreset preset, SkinnedMeshRenderer renderer)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            if (renderer.sharedMesh == null) throw new ArgumentException("renderer has no mesh.", nameof(renderer));

            var mesh = renderer.sharedMesh;
            var skipped = new List<string>();
            Undo.RecordObject(renderer, "Import BlendShape Preset");

            foreach (var shape in preset.blendShapes)
            {
                // A hand-edited/corrupted preset file can be missing the
                // "name" key entirely (JsonUtility leaves it null rather
                // than failing to parse) -- GetBlendShapeIndex(null) throws,
                // so this must be treated the same as "not found" rather
                // than crashing the whole import.
                if (string.IsNullOrEmpty(shape.name))
                {
                    skipped.Add(shape.name ?? "(missing name)");
                    continue;
                }

                var index = mesh.GetBlendShapeIndex(shape.name);
                if (index < 0)
                {
                    skipped.Add(shape.name);
                    continue;
                }

                renderer.SetBlendShapeWeight(index, shape.weight);
            }

            return skipped;
        }
    }
}
