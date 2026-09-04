using System;
using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Core.Diagnostics;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.Diagnostics;
using AvatarVcs.Core.Model;
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
            CollectFromTrackedTargets(GameObject avatarRoot, DiagnosticLog log = null)
        {
            if (avatarRoot == null) throw new ArgumentNullException(nameof(avatarRoot));

            // KAN-20: pass a DiagnosticLog through to each per-target capture.
            // BranchManager.Commit / the checkout auto-commit pass their own;
            // a direct caller (tests) passes none, so make one and flush here.
            using var diagnostics = DiagnosticScope.OwnOrBorrow(ref log);

            return CollectCore(avatarRoot, log);
        }

        private static (List<AvatarReferenceState> avatarReferences, List<MaterialSettingsState> materialSettings)
            CollectCore(GameObject avatarRoot, DiagnosticLog log)
        {
            var avatarReferences = new List<AvatarReferenceState>();
            var materialSettings = new List<MaterialSettingsState>();

            // If a target's own ancestor is also tracked, the ancestor's
            // recursive capture (AvatarReferenceCapture.CaptureDescendantComponents)
            // already walks down into this target -- capturing it again as
            // its own independent entry would just duplicate every field into
            // two AvatarReferenceState rows with no new information. Only the
            // outermost tracked marker in any tracked/tracked chain runs.
            var trackedTargets = avatarRoot.GetComponentsInChildren<AvatarVcsTrackedReference>(includeInactive: true)
                // An AvatarVcsUntracked on the marker's own object or an
                // ancestor overrides it (KAN-11): AvatarReferenceCapture would
                // walk the marked subtree and skip every node, leaving an
                // empty AvatarReferenceState row. Drop the marker here.
                .Where(t => t.GetComponentInParent<AvatarVcsUntracked>(includeInactive: true) == null)
                .Where(t => t.transform.parent == null
                    || t.transform.parent.GetComponentInParent<AvatarVcsTrackedReference>(includeInactive: true) == null)
                .ToList();
            var vcsRoot = ContainerManager.FindRoot(avatarRoot)?.transform;
            foreach (var tracked in trackedTargets)
            {
                var target = tracked.transform;
                avatarReferences.Add(AvatarReferenceCapture.Capture(target, avatarRoot.transform, log));

                // Every renderer in the tracked subtree, not just the target's
                // own -- the default "Ensure Root" config tracks the avatar
                // root, whose renderers (if any) are never the ones carrying
                // lilToon settings; Body/accessories are always descendants.
                // Mirrors AvatarReferenceCapture's [AvatarVCS]-subtree skip.
                foreach (var node in target.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    if (vcsRoot != null && node.IsChildOf(vcsRoot)) continue;
                    // Mirror AvatarReferenceCapture's AvatarVcsUntracked skip
                    // (KAN-11): an opted-out subtree contributes no
                    // materialSettings either.
                    if (node.GetComponentInParent<AvatarVcsUntracked>(includeInactive: true) != null) continue;

                    var path = ReferenceResolver.GetRelativePath(node, avatarRoot.transform);
                    // Shared with ContainerCapture (KAN-78). This used to be a
                    // second copy of the same loop that omitted the
                    // material.shader null check, so a material whose shader
                    // asset had been deleted aborted the entire commit.
                    AvatarReferenceCapture.CaptureMaterialSettingsInto(node, path, materialSettings);
                }
            }

            return (avatarReferences, materialSettings);
        }
    }
}
