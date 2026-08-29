using System;
using System.Linq;
using AvatarVcs.Core.Diff;
using AvatarVcs.Core.History;
using AvatarVcs.Editor.History;
using AvatarVcs.Core.Model;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.UI
{
    // Compare mode (design doc 5.2): toggle between two commits without
    // taking an auto-commit per flip.
    public partial class AvatarVcsWindow
    {
        private void DrawCompareBar()
        {
            if (!compareModeActive)
            {
                if (commits.Count < 2) return;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Compare", GUILayout.Width(60));

                var labels = commits.Select(CommitLabel).ToArray();
                var ids = commits.Select(c => c.commitId).ToArray();

                var aIndex = Array.IndexOf(ids, compareCommitAId);
                var newAIndex = EditorGUILayout.Popup(aIndex, labels);
                compareCommitAId = newAIndex >= 0 ? ids[newAIndex] : null;

                var bIndex = Array.IndexOf(ids, compareCommitBId);
                var newBIndex = EditorGUILayout.Popup(bIndex, labels);
                compareCommitBId = newBIndex >= 0 ? ids[newBIndex] : null;

                GUI.enabled = !string.IsNullOrEmpty(compareCommitAId)
                    && !string.IsNullOrEmpty(compareCommitBId)
                    && compareCommitAId != compareCommitBId;
                if (GUILayout.Button("Start Compare", GUILayout.Width(110)))
                    StartCompare();
                GUI.enabled = true;

                EditorGUILayout.EndHorizontal();
                return;
            }

            var activeId = compareShowingB ? compareCommitBId : compareCommitAId;
            EditorGUILayout.HelpBox(
                $"Compare mode: viewing '{CommitMessage(activeId)}' ({(compareShowingB ? "B" : "A")}). "
                + "Toggling does not create commits, so don't edit the scene here -- any edit will be "
                + "discarded (with a confirmation) the moment you toggle or exit.",
                MessageType.Warning);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(compareShowingB ? "Show A" : "Show B", GUILayout.Width(100)))
                ToggleCompare();
            if (GUILayout.Button("Keep This as Current State", GUILayout.Width(180)))
                ExitCompare(keepCurrent: true);
            if (GUILayout.Button("Restore Original", GUILayout.Width(130)))
                ExitCompare(keepCurrent: false);
            EditorGUILayout.EndHorizontal();
        }

        private void StartCompare()
        {
            var sourceBranch = config.currentBranch;
            var originalHeadId = CurrentHeadId();
            var commitA = compareCommitAId;

            RunCheckout(() =>
            {
                var commit = CommitStore.LoadCommit(avatarGuid, commitA);

                // Only take the safety-net auto-commit if there's actually
                // uncommitted work to protect; otherwise "Restore Original"
                // would land on a redundant [auto] commit instead of the
                // real original head, cluttering history for nothing.
                CheckoutResult result;
                if (HasUncommittedChanges(originalHeadId))
                {
                    result = CheckoutOperation.Checkout(commit, avatarRoot, sourceBranch, originalHeadId);
                    if (result.IsSuccess) compareReturnCommitId = result.AutoCommitId;
                }
                else
                {
                    result = CheckoutOperation.CheckoutWithoutAutoCommit(commit, avatarRoot);
                    if (result.IsSuccess) compareReturnCommitId = originalHeadId;
                }

                if (result.IsSuccess)
                {
                    compareModeActive = true;
                    compareShowingB = false;
                }
                return result;
            });
        }

        private bool HasUncommittedChanges(string headCommitId)
        {
            if (string.IsNullOrEmpty(headCommitId)) return true;
            var head = CommitStore.LoadCommit(avatarGuid, headCommitId);
            if (head == null) return true;
            return SnapshotDiffer.Diff(head, CaptureLiveState()).Any(d => d.kind != DiffKind.Unchanged);
        }

        private void ToggleCompare()
        {
            // Compare mode's toggle always re-applies a fixed historical
            // commit, silently overwriting anything hand-edited in the
            // scene since the last toggle -- exactly the "no warning at
            // all" surprise CODE_REVIEW.md 3.4 called out. This is the one
            // spot in compare mode where a discard isn't already the whole
            // point of the button (unlike Restore Original / Keep As
            // Current), so it's the one that needs a confirmation.
            var currentlyShownId = compareShowingB ? compareCommitBId : compareCommitAId;
            if (HasUncommittedChanges(currentlyShownId))
            {
                if (!EditorUtility.DisplayDialog("Discard Scene Edits?",
                        "The scene has changed since you entered (or last toggled) compare mode. "
                        + "Toggling now will discard those changes -- compare mode doesn't take a "
                        + "safety-net commit on toggle.",
                        "Discard and Toggle", "Cancel"))
                    return;
            }

            var targetId = compareShowingB ? compareCommitAId : compareCommitBId;

            RunCheckout(() =>
            {
                var target = CommitStore.LoadCommit(avatarGuid, targetId);
                var result = CheckoutOperation.CheckoutWithoutAutoCommit(target, avatarRoot);
                if (result.IsSuccess)
                    compareShowingB = !compareShowingB;
                return result;
            });
        }

        private void ExitCompare(bool keepCurrent)
        {
            var targetCommitId = keepCurrent
                ? (compareShowingB ? compareCommitBId : compareCommitAId)
                : compareReturnCommitId;

            compareModeActive = false;
            RunCheckout(() => BranchManager.RestoreToCommit(avatarRoot, targetCommitId));
        }
    }
}
