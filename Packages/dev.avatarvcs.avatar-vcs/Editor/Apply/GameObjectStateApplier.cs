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

            if (go.layer != layer)
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
