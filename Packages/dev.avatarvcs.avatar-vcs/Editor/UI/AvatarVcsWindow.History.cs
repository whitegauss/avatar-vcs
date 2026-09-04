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

        /// <summary>
        /// A free-form note on the selected commit, editable after the fact.
        /// Separate from the commit message, which names the commit in the
        /// list and is fixed once made.
        ///
        /// The draft is held in the window, not written through on every
        /// keystroke: OnGUI runs constantly, and saving per character would
        /// rewrite the commit file each frame the field has focus.
        /// </summary>
        /// <summary>
        /// The note's first line, clipped, for the collapsed foldout label --
        /// enough to tell whether a note exists and roughly what it says
        /// without giving up the vertical space to show it.
        /// </summary>
        private static string FirstLine(string note)
        {
            var line = note.Split('\n')[0].Trim();
            return line.Length <= 60 ? line : line.Substring(0, 57) + "...";
        }

        private void DrawNotePanel()
        {
            var commitId = presenter.SelectedCommitId;
            if (commitId == null) return;

            // Selection moved: drop whatever was being typed for the old one
            // rather than carrying it onto a different commit.
            if (commitId != noteDraftCommitId)
            {
                noteDraftCommitId = commitId;
                noteDraft = presenter.SelectedCommitNote();
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Collapsed by default and one line tall when shut. A note is
            // written once and read rarely, so it should not take a fixed
            // slice of the window away from the history list -- which is what
            // is actually being used most of the time.
            var summary = string.IsNullOrEmpty(noteDraft)
                ? "Note"
                : $"Note: {FirstLine(noteDraft)}";
            noteExpanded = EditorGUILayout.Foldout(noteExpanded, summary, toggleOnLabelClick: true);

            if (!noteExpanded)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            noteScroll = EditorGUILayout.BeginScrollView(noteScroll, GUILayout.Height(72));
            var edited = EditorGUILayout.TextArea(noteDraft ?? "", GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            var dirty = edited != (noteDraft ?? "");
            if (dirty) noteDraft = edited;

            EditorGUILayout.BeginHorizontal();
            var saved = presenter.SelectedCommitNote();
            var unsaved = (noteDraft ?? "") != saved;

            GUI.enabled = unsaved;
            if (GUILayout.Button("Save Note", GUILayout.Width(90)))
            {
                if (presenter.SaveNoteOnSelectedCommit(noteDraft))
                    // Read back what was actually stored: a whitespace-only
                    // note is saved as absent, and leaving the draft as typed
                    // would leave "unsaved" showing forever against a note
                    // that had in fact been saved.
                    noteDraft = presenter.SelectedCommitNote();
                GUI.FocusControl(null);
            }
            if (GUILayout.Button("Revert", GUILayout.Width(70)))
            {
                noteDraft = saved;
                GUI.FocusControl(null);
            }
            GUI.enabled = true;

            if (unsaved) EditorGUILayout.LabelField("unsaved", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawDiffPanel()
        {
            EditorGUILayout.BeginVertical();

            // Collapsed by default, and shut means genuinely off: the
            // presenter computes nothing while DiffEnabled is false. A diff
            // against the live scene captures the whole avatar, and the
            // window asks for one after any scene edit, so leaving this open
            // taxes every edit the user makes.
            var wasExpanded = diffExpanded;
            diffExpanded = EditorGUILayout.Foldout(diffExpanded, "Changes", toggleOnLabelClick: true);
            if (diffExpanded != wasExpanded) presenter.DiffEnabled = diffExpanded;

            if (!diffExpanded)
            {
                EditorGUILayout.EndVertical();
                return;
            }

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
