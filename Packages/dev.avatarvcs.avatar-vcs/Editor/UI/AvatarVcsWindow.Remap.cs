using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.UI
{
    // GUID remapping (design doc 6.4): shown when a checkout fails with
    // missing prefabs. The window turns each Object picker into a GUID and
    // pushes it into the presenter, which owns the pending-remap state and
    // the retry.
    public partial class AvatarVcsWindow
    {
        private void DrawRemapSection()
        {
            var missing = presenter.PendingMissingGuids;
            if (missing == null || missing.Count == 0) return;

            EditorGUILayout.HelpBox(
                "Checkout aborted: the following prefabs/materials could not be resolved. "
                + "Assign their replacement (e.g. after a re-import) and retry, or Cancel.",
                MessageType.Warning);

            foreach (var guid in missing)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(guid, GUILayout.Width(260));
                remapSelections.TryGetValue(guid, out var current);
                // pendingMissingGuids only ever comes from HasMissingPrefabs
                // (checkout pre-flight only checks container prefabs), so
                // restrict the picker to prefab assets.
                var picked = EditorGUILayout.ObjectField(current, typeof(GameObject), false);
                if (picked != current)
                {
                    remapSelections[guid] = picked;
                    var newGuid = picked != null
                        ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(picked))
                        : null;
                    presenter.SetRemapSelection(guid, newGuid);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();

            GUI.enabled = presenter.CanApplyRemap();
            if (GUILayout.Button("Apply Remapping and Retry"))
            {
                remapSelections.Clear();
                presenter.ApplyRemapAndRetry();
            }
            GUI.enabled = true;

            if (GUILayout.Button("Cancel", GUILayout.Width(80)))
            {
                remapSelections.Clear();
                presenter.CancelRemap();
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
