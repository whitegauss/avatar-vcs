using System.Linq;
using AvatarVcs.Editor.History;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.UI
{
    // GUID remapping (design doc 6.4): shown when a checkout fails with
    // missing prefabs, so the user can point each one at its re-imported
    // replacement and retry.
    public partial class AvatarVcsWindow
    {
        private void DrawRemapSection()
        {
            if (pendingMissingGuids == null || pendingMissingGuids.Count == 0) return;

            EditorGUILayout.HelpBox(
                "Checkout aborted: the following prefabs/materials could not be resolved. "
                + "Assign their replacement (e.g. after a re-import) and retry, or Cancel.",
                MessageType.Warning);

            foreach (var guid in pendingMissingGuids)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(guid, GUILayout.Width(260));
                remapSelections.TryGetValue(guid, out var current);
                // pendingMissingGuids only ever comes from HasMissingPrefabs
                // (CheckoutOperation only pre-flight-checks container prefabs,
                // never materials), so restrict the picker to prefab assets.
                var picked = EditorGUILayout.ObjectField(current, typeof(GameObject), false);
                remapSelections[guid] = picked;
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();

            GUI.enabled = pendingMissingGuids.All(g => remapSelections.TryGetValue(g, out var o) && o != null);
            if (GUILayout.Button("Apply Remapping and Retry"))
            {
                foreach (var guid in pendingMissingGuids)
                {
                    var newPath = AssetDatabase.GetAssetPath(remapSelections[guid]);
                    var newGuid = AssetDatabase.AssetPathToGUID(newPath);
                    GuidRemapper.AddMapping(guid, newGuid);
                }

                var retry = pendingRetryCheckout;
                pendingMissingGuids = null;
                remapSelections.Clear();
                pendingRetryCheckout = null;
                RunCheckout(retry);
            }
            GUI.enabled = true;

            if (GUILayout.Button("Cancel", GUILayout.Width(80)))
            {
                pendingMissingGuids = null;
                remapSelections.Clear();
                pendingRetryCheckout = null;
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
