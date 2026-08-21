using System;
using System.Collections.Generic;
using AvatarVcs.Editor.MaterialSettings;
using AvatarVcs.Editor.Model;
using AvatarVcs.Editor.Reflection;
using AvatarVcs.Runtime;
using UnityEngine;

namespace AvatarVcs.Editor.AvatarReferences
{
    /// <summary>
    /// Finds every AvatarVcsTrackedReference marker under avatarRoot and
    /// captures both halves of design doc 1.4's whitelist for each: blend
    /// shape weights/material references (avatarReferences), and, for slots
    /// on a supported shader, duplicable shader settings (materialSettings).
    ///
    /// This is the entry point BranchManager.Commit and the checkout
    /// safety-net auto-commit use to populate both lists -- previously
    /// nothing called AvatarReferenceCapture/MaterialSettingsCapture at all,
    /// so a tracked target's BlendShapes never made it into a commit even
    /// though capture and apply were both already implemented and tested.
    /// </summary>
    public static class AvatarReferenceCollector
    {
        public static (List<AvatarReferenceState> avatarReferences, List<MaterialSettingsState> materialSettings)
            CollectFromTrackedTargets(GameObject avatarRoot)
        {
            var avatarReferences = new List<AvatarReferenceState>();
            var materialSettings = new List<MaterialSettingsState>();

            var trackedTargets = avatarRoot.GetComponentsInChildren<AvatarVcsTrackedReference>(includeInactive: true);
            foreach (var tracked in trackedTargets)
            {
                var target = tracked.transform;
                avatarReferences.Add(AvatarReferenceCapture.Capture(target, avatarRoot.transform));

                var renderer = target.GetComponent<Renderer>();
                if (renderer == null) continue;

                var path = ReferenceResolver.GetRelativePath(target, avatarRoot.transform);
                var materials = renderer.sharedMaterials;
                for (var slot = 0; slot < materials.Length; slot++)
                {
                    var material = materials[slot];
                    // Unsaved/missing materials and shaders outside the MVP's
                    // lilToon-only ShaderPropertyMap are silently skipped here
                    // (not a warning): materialSettings is a best-effort bonus
                    // on top of avatarReferences' material *reference* tracking,
                    // which already covers every slot regardless of shader.
                    if (material == null || !ShaderPropertyMap.IsSupported(material.shader.name)) continue;

                    // Capture throws if material isn't a saved asset (e.g. a
                    // runtime-only instance); that's a legitimate state for a
                    // live scene, not a reason to abort the whole commit.
                    try
                    {
                        materialSettings.Add(MaterialSettingsCapture.Capture(material, material.shader.name, path, slot));
                    }
                    catch (InvalidOperationException)
                    {
                        // not a saved asset -- skip this slot
                    }
                }
            }

            return (avatarReferences, materialSettings);
        }
    }
}
