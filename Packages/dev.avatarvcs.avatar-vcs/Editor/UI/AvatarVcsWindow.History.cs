using System.Linq;
using AvatarVcs.Core.Diff;
using AvatarVcs.Core.Model;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.UI
{
    // Commit history panel, the diff panel next to it, and commit deletion.
    // All decisions are the presenter's; this file only draws and dispatches.
    public partial class AvatarVcsWindow
    {
        private void DrawHistoryPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(220));
            EditorGUILayout.LabelField("History", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("* = current branch head (can't be deleted)", EditorStyles.miniLabel);

            historyScroll = EditorGUILayout.BeginScrollView(historyScroll);
            var headId = presenter.CurrentHeadId;
            foreach (var entry in presenter.Commits.ToList())
            {
                var label = entry.commitId == headId ? $"* {entry.message}" : $"  {entry.message}";
                var selected = entry.commitId == presenter.SelectedCommitId;

                EditorGUILayout.BeginHorizontal();

                var checkedForDeletion = presenter.SelectedForBulkDelete.Contains(entry.commitId);
                var newChecked = EditorGUILayout.Toggle(checkedForDeletion, GUILayout.Width(16));
                if (newChecked != checkedForDeletion)
                    presenter.SetBulkDeleteSelected(entry.commitId, newChecked);

                var prevBg = GUI.backgroundColor;
                if (selected) GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);
                // A long commit message must not widen this button past the
                // panel and push the delete button out of view.
                if (GUILayout.Button(label, GUILayout.MaxWidth(164)) && !selected)
                    presenter.SelectCommit(entry.commitId);
                GUI.backgroundColor = prevBg;

                // The button stays enabled even on the head -- IMGUI tooltips
                // on disabled controls are unreliable -- so clicking it while
                // it's the head gets an explicit explanation (from the
                // presenter) rather than doing nothing.
                var isHead = entry.commitId == headId;
                var deleteContent = new GUIContent("x", isHead
                    ? "This is the current branch's head. Checkout a different commit first to move the head away, then delete it."
                    : "Delete this commit (and any duplicate assets generated only for it).");
                if (GUILayout.Button(deleteContent, GUILayout.Width(20)))
                    presenter.DeleteCommit(entry.commitId);

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            var bulkCount = presenter.SelectedForBulkDelete.Count;
            GUI.enabled = bulkCount > 0;
            if (GUILayout.Button($"Delete Selected ({bulkCount})"))
                presenter.DeleteSelected();
            GUI.enabled = true;
            if (bulkCount > 0 && GUILayout.Button("Clear", GUILayout.Width(50)))
                presenter.ClearBulkDelete();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawDiffPanel()
        {
            EditorGUILayout.BeginVertical();

            var baseLabel = presenter.DiffBaseCommitId == null
                ? "current scene"
                : presenter.CommitMessageOf(presenter.DiffBaseCommitId);
            EditorGUILayout.LabelField($"Diff (selected commit -> {baseLabel})", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            var options = presenter.DiffBaseOptions().ToList();
            var labels = options.Select(o => o.label).ToArray();
            var currentIndex = options.FindIndex(o => o.id == presenter.DiffBaseCommitId);
            var newIndex = EditorGUILayout.Popup("Diff against", currentIndex < 0 ? 0 : currentIndex, labels);
            if (newIndex != currentIndex)
                presenter.SelectDiffBaseByIndex(newIndex);
            if (GUILayout.Button("Refresh", GUILayout.Width(70)))
                presenter.RecomputeSelectedDiff();
            EditorGUILayout.EndHorizontal();

            diffScroll = EditorGUILayout.BeginScrollView(diffScroll);
            foreach (var diff in presenter.SelectedDiff)
                DrawDiffEntry(diff);
            if (presenter.SelectedDiff.Count == 0)
                EditorGUILayout.LabelField("No containers.");
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawDiffEntry(ContainerDiff diff)
        {
            var tone = DiffRowFormatter.ToneOf(diff.kind);
            var color = tone switch
            {
                DiffTone.Added => Color.green,
                DiffTone.Removed => Color.red,
                DiffTone.Changed => Color.yellow,
                _ => GUI.color,
            };
            var label = DiffRowFormatter.RowLabel(diff);

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
    }
}
