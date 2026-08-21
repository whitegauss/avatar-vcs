using System;
using System.Linq;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using AvatarVcs.Editor.Model;
using AvatarVcs.Runtime;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.UI
{
    // Branch switcher/creation, commit, and checkout-selected-commit.
    public partial class AvatarVcsWindow
    {
        private void DrawBranchBar()
        {
            EditorGUILayout.BeginHorizontal();

            var branchNames = config.branches.Select(b => b.name).ToArray();
            var currentIndex = Array.IndexOf(branchNames, config.currentBranch);
            var newIndex = EditorGUILayout.Popup("Branch", currentIndex, branchNames);
            if (newIndex != currentIndex && newIndex >= 0)
            {
                var target = branchNames[newIndex];
                if (ConfirmDiscardIfUncommitted("Switch Branch", $"Switch from '{config.currentBranch}' to '{target}'?"))
                    RunCheckout(() => BranchManager.SwitchBranch(avatarRoot, target));
            }

            if (GUILayout.Button("+ New Branch", GUILayout.Width(100)))
                showNewBranchField = !showNewBranchField;

            EditorGUILayout.EndHorizontal();

            if (showNewBranchField)
            {
                EditorGUILayout.BeginHorizontal();
                newBranchName = EditorGUILayout.TextField(newBranchName);
                var isValid = BranchManager.IsValidBranchName(newBranchName) && !config.branches.Any(b => b.name == newBranchName);
                GUI.enabled = isValid;
                if (GUILayout.Button("Create", GUILayout.Width(80)))
                {
                    BranchManager.CreateBranch(avatarRoot, newBranchName);
                    newBranchName = "";
                    showNewBranchField = false;
                    Reload();
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(newBranchName) && !isValid)
                    EditorGUILayout.HelpBox(
                        "Invalid or duplicate branch name. Avoid / \\ : * ? \" < > | and leading/trailing whitespace or a leading '.' or '-'.",
                        MessageType.Warning);
            }
        }

        private void DrawUncommittedWarning()
        {
            var uncommitted = diffBaseCommitId == null
                && selectedDiff.Any(d => d.kind != DiffKind.Unchanged)
                && selectedCommitId == CurrentHeadId();
            if (uncommitted)
                EditorGUILayout.HelpBox("Uncommitted changes in the scene (see diff below).", MessageType.Warning);
        }

        private void DrawCommitBar()
        {
            EditorGUILayout.BeginHorizontal();
            commitMessage = EditorGUILayout.TextField(commitMessage);
            if (GUILayout.Button("Commit", GUILayout.Width(100)))
            {
                var message = string.IsNullOrEmpty(commitMessage) ? "Manual commit" : commitMessage;
                try
                {
                    BranchManager.Commit(avatarRoot, message);
                }
                catch (InvalidOperationException e)
                {
                    EditorUtility.DisplayDialog("Commit Failed", e.Message, "OK");
                    return;
                }
                commitMessage = "";
                // The very first commit creates the root (and its guid) as a
                // side effect; avatarGuid may still be null/stale here.
                avatarGuid = ContainerManager.FindRoot(avatarRoot).GetComponent<AvatarVcsRoot>().AvatarGuid;
                Reload();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCheckoutBar()
        {
            GUI.enabled = selectedCommitId != null && selectedCommitId != CurrentHeadId();
            if (GUILayout.Button("Checkout Selected Commit")
                && ConfirmDiscardIfUncommitted("Checkout Commit", "Checkout the selected commit?"))
                RunCheckout(() => BranchManager.RestoreToCommit(avatarRoot, selectedCommitId));
            GUI.enabled = true;
        }

        // Branch switch and checkout-selected-commit both overwrite the
        // scene without taking a safety-net commit first (design choice:
        // Ctrl+Z is the recovery path for uncommitted work, not another
        // [auto] commit cluttering history), so an actual uncommitted edit
        // gets an explicit heads-up instead of vanishing silently.
        private bool ConfirmDiscardIfUncommitted(string title, string action)
        {
            if (!HasUncommittedChanges(CurrentHeadId()))
                return EditorUtility.DisplayDialog(title, action, "OK", "Cancel");

            return EditorUtility.DisplayDialog(title,
                action + "\n\nUncommitted changes in the scene will be discarded (undo with Ctrl+Z if needed).",
                "Discard and Continue", "Cancel");
        }
    }
}
