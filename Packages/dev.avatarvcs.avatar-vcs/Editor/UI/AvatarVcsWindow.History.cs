using System;
using System.Linq;
using AvatarVcs.Editor.History;
using AvatarVcs.Editor.Model;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.UI
{
    // Commit history panel, the diff panel next to it, and commit deletion.
    public partial class AvatarVcsWindow
    {
        private void DrawHistoryPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(220));
            EditorGUILayout.LabelField("History", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("* = current branch head (can't be deleted)", EditorStyles.miniLabel);

            historyScroll = EditorGUILayout.BeginScrollView(historyScroll);
            var headId = CurrentHeadId();
            foreach (var entry in commits.ToList())
            {
                var label = entry.commitId == headId ? $"* {entry.message}" : $"  {entry.message}";
                var selected = entry.commitId == selectedCommitId;

                EditorGUILayout.BeginHorizontal();

                var prevBg = GUI.backgroundColor;
                if (selected) GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);
                // A long commit message must not be allowed to expand past
                // this panel's width -- without a bound here, IMGUI would
                // widen this button to fit the text and push the delete
                // button below out of the visible/scrollable area, which
                // looks exactly like "there's no delete button".
                if (GUILayout.Button(label, GUILayout.MaxWidth(180)) && !selected)
                {
                    selectedCommitId = entry.commitId;
                    RecomputeSelectedDiff();
                }
                GUI.backgroundColor = prevBg;

                // Deleting a branch head would leave that branch pointing at
                // nothing. The button stays enabled rather than disabled --
                // IMGUI doesn't reliably show tooltips on disabled controls
                // -- so clicking it while it's the head always gets an
                // explicit, actionable explanation instead of doing nothing.
                var isHead = entry.commitId == headId;
                var deleteContent = new GUIContent("x", isHead
                    ? "This is the current branch's head. Checkout a different commit first to move the head away, then delete it."
                    : "Delete this commit (and any duplicate assets generated only for it).");
                if (GUILayout.Button(deleteContent, GUILayout.Width(20)))
                {
                    if (isHead)
                        EditorUtility.DisplayDialog("Can't Delete",
                            "This commit is the current branch's head. Checkout a different commit first to move the head away, then delete it.",
                            "OK");
                    else
                        DeleteCommit(entry.commitId);
                }

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawDiffPanel()
        {
            EditorGUILayout.BeginVertical();

            var baseLabel = diffBaseCommitId == null ? "current scene" : CommitMessage(diffBaseCommitId);
            EditorGUILayout.LabelField($"Diff (selected commit -> {baseLabel})", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            var options = new[] { "Current Scene" }.Concat(commits.Select(CommitLabel)).ToArray();
            var ids = new string[] { null }.Concat(commits.Select(c => c.commitId)).ToArray();
            var currentIndex = Array.IndexOf(ids, diffBaseCommitId);
            var newIndex = EditorGUILayout.Popup("Diff against", currentIndex < 0 ? 0 : currentIndex, options);
            var newBaseId = ids[Mathf.Clamp(newIndex, 0, ids.Length - 1)];
            if (newBaseId != diffBaseCommitId)
            {
                diffBaseCommitId = newBaseId;
                RecomputeSelectedDiff();
            }
            if (GUILayout.Button("Refresh", GUILayout.Width(70)))
                RecomputeSelectedDiff();
            EditorGUILayout.EndHorizontal();

            diffScroll = EditorGUILayout.BeginScrollView(diffScroll);
            foreach (var diff in selectedDiff)
                DrawDiffEntry(diff);
            if (selectedDiff.Count == 0)
                EditorGUILayout.LabelField("No containers.");
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawDiffEntry(ContainerDiff diff)
        {
            var symbol = diff.kind switch
            {
                DiffKind.Added => "+",
                DiffKind.Removed => "-",
                DiffKind.Changed => "~",
                _ => "=",
            };
            var color = diff.kind switch
            {
                DiffKind.Added => Color.green,
                DiffKind.Removed => Color.red,
                DiffKind.Changed => Color.yellow,
                _ => GUI.color,
            };
            var label = $"{symbol} {diff.containerId}";
            if (diff.kind == DiffKind.Unchanged) label += " (unchanged)";

            var prevColor = GUI.color;
            GUI.color = color;

            if (diff.changeNotes.Count > 0)
            {
                expandedContainers.TryGetValue(diff.containerId, out var expanded);
                expanded = EditorGUILayout.Foldout(expanded, label, true);
                expandedContainers[diff.containerId] = expanded;
                if (expanded)
                {
                    EditorGUI.indentLevel++;
                    GUI.color = prevColor;
                    foreach (var note in diff.changeNotes)
                        EditorGUILayout.LabelField(note);
                    EditorGUI.indentLevel--;
                }
            }
            else
            {
                EditorGUILayout.LabelField(label);
            }

            GUI.color = prevColor;
        }

        private void DeleteCommit(string commitId)
        {
            if (!EditorUtility.DisplayDialog("Delete Commit",
                    "Delete this commit and its generated assets (e.g. duplicate materials)? This cannot be undone.",
                    "Delete", "Cancel"))
                return;

            try
            {
                CommitStore.DeleteCommit(avatarGuid, commitId);
            }
            catch (InvalidOperationException e)
            {
                // The underlying message mentions a "force" escape hatch
                // that only exists in the C# API, not this window; steer
                // the user at the one path actually available here.
                EditorUtility.DisplayDialog("Delete Failed",
                    e.Message + "\n\nSwitch to that branch, checkout a different commit on it, then come back and delete this one.",
                    "OK");
                return;
            }

            if (selectedCommitId == commitId) selectedCommitId = null;
            Reload();
        }
    }
}
