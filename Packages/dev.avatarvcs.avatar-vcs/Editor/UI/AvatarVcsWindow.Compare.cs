using System;
using System.Linq;
using AvatarVcs.Core.Presentation;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.UI
{
    // Compare mode (design doc 5.2): toggle between two commits without
    // taking an auto-commit per flip. All transitions are the presenter's.
    public partial class AvatarVcsWindow
    {
        private void DrawCompareBar()
        {
            if (!presenter.CompareModeActive)
            {
                var commits = presenter.Commits;
                if (commits.Count < 2) return;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Compare", GUILayout.Width(60));

                var labels = commits.Select(AvatarVcsPresenter.CommitLabel).ToArray();
                var ids = commits.Select(c => c.commitId).ToArray();

                var aIndex = Array.IndexOf(ids, presenter.CompareCommitAId);
                var newAIndex = EditorGUILayout.Popup(aIndex, labels);
                presenter.CompareCommitAId = newAIndex >= 0 ? ids[newAIndex] : null;

                var bIndex = Array.IndexOf(ids, presenter.CompareCommitBId);
                var newBIndex = EditorGUILayout.Popup(bIndex, labels);
                presenter.CompareCommitBId = newBIndex >= 0 ? ids[newBIndex] : null;

                GUI.enabled = presenter.CanStartCompare();
                if (GUILayout.Button("Start Compare", GUILayout.Width(110)))
                    presenter.StartCompare();
                GUI.enabled = true;

                EditorGUILayout.EndHorizontal();
                return;
            }

            var activeId = presenter.CompareShowingB ? presenter.CompareCommitBId : presenter.CompareCommitAId;
            EditorGUILayout.HelpBox(
                $"Compare mode: viewing '{presenter.CommitMessageOf(activeId)}' ({(presenter.CompareShowingB ? "B" : "A")}). "
                + "Toggling does not create commits, so don't edit the scene here -- any edit will be "
                + "discarded (with a confirmation) the moment you toggle or exit.",
                MessageType.Warning);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(presenter.CompareShowingB ? "Show A" : "Show B", GUILayout.Width(100)))
                presenter.ToggleCompare();
            if (GUILayout.Button("Keep This as Current State", GUILayout.Width(180)))
                presenter.ExitCompare(keepCurrent: true);
            if (GUILayout.Button("Restore Original", GUILayout.Width(130)))
                presenter.ExitCompare(keepCurrent: false);
            EditorGUILayout.EndHorizontal();
        }
    }
}
