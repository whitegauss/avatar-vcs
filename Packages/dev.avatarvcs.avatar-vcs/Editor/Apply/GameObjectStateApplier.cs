using System;
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
        /// actually defined in this project's Tag Manager. Returns a
        /// ready-to-log warning message if the tag couldn't be applied, or
        /// null if everything applied cleanly (including "no tag recorded").
        /// tagContext is a short caller-supplied phrase identifying what's
        /// being restored (e.g. "container 'hair'" or "'Body/Toggle'") for
        /// the warning message.
        /// </summary>
        public static string Apply(GameObject go, bool activeSelf, string tag, int layer, string tagContext, string undoName)
        {
            if (go == null) throw new ArgumentNullException(nameof(go));

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

            if (string.IsNullOrEmpty(tag) || tag == go.tag) return null;

            if (Array.IndexOf(UnityEditorInternal.InternalEditorUtility.tags, tag) < 0)
                return $"[AvatarVCS] Tag '{tag}' recorded for {tagContext} is not defined in this project's Tag Manager; left as '{go.tag}'.";

            Undo.RecordObject(go, undoName);
            go.tag = tag;
            return null;
        }
    }
}
