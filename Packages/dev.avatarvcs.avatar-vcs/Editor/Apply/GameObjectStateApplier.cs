using System;
using AvatarVcs.Core.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.Apply
{
    /// <summary>
    /// Applies the three GameObject-level state fields both ContainerSnapshot
    /// (a container's own root) and ObjectStateRef (a Track Properties
    /// target/descendant) capture: activeSelf, tag, and layer. Shared here
    /// instead of duplicated per caller, since the one non-trivial part --
    /// GameObject.tag doesn't throw for an undefined tag, it logs an
    /// engine-level Debug.LogError and silently no-ops, so it must be
    /// validated against InternalEditorUtility.tags first rather than
    /// try/caught -- was independently fixed twice (ContainerRestore,
    /// AvatarReferenceApplier) after the same bug surfaced in both.
    /// </summary>
    public static class GameObjectStateApplier
    {
        /// <summary>
        /// Applies activeSelf and layer unconditionally (cheap, never fails);
        /// tag only if non-empty, different from go's current tag, and
        /// actually defined in this project's Tag Manager. A tag that can't be
        /// applied is reported to <paramref name="log"/> (KAN-20: was a
        /// returned string the caller logged); everything applying cleanly
        /// (including "no tag recorded") adds nothing. tagContext is a short
        /// caller-supplied phrase identifying what's being restored (e.g.
        /// "container 'hair'" or "'Body/Toggle'") for the warning message.
        /// </summary>
        public static void Apply(GameObject go, bool activeSelf, string tag, int layer, string tagContext, string undoName, DiagnosticLog log)
        {
            if (go == null) throw new ArgumentNullException(nameof(go));
            if (log == null) throw new ArgumentNullException(nameof(log));

            if (go.activeSelf != activeSelf)
            {
                Undo.RecordObject(go, undoName);
                go.SetActive(activeSelf);
            }

            // Unity has 32 layers and silently misbehaves outside 0..31 --
            // the recorded value comes from commit JSON, which this repo
            // treats as hand-editable and merge-corruptible everywhere else.
            // Warn and skip rather than clamp: 40 clamped to 31 is a
            // different, equally wrong layer, and silently moving an object
            // to one is worse than leaving it where it is.
            if (layer < 0 || layer > 31)
            {
                log.Warn($"[AvatarVCS] Layer {layer} recorded for {tagContext} is outside Unity's 0..31 range; "
                    + $"left as {go.layer}.");
            }
            else if (go.layer != layer)
            {
                Undo.RecordObject(go, undoName);
                go.layer = layer;
            }

            if (string.IsNullOrEmpty(tag) || tag == go.tag) return;

            if (Array.IndexOf(UnityEditorInternal.InternalEditorUtility.tags, tag) < 0)
            {
                log.Warn($"[AvatarVCS] Tag '{tag}' recorded for {tagContext} is not defined in this project's Tag Manager; left as '{go.tag}'.");
                return;
            }

            Undo.RecordObject(go, undoName);
            go.tag = tag;
        }
    }
}
