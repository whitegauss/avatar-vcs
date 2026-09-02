using System;
using System.Linq;
using AvatarVcs.Core.History;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using AvatarVcs.Core.Model;
using AvatarVcs.Runtime;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.UI
{
    // Branch switcher/creation, commit, and checkout-selected-commit.
    public partial class AvatarVcsWindow
    {
        /// <summary>
        /// The branch dropdown, "+ New Branch" toggle, and (when open) the
        /// new-branch name field. Switch and create both run their actual
        /// work -- which can throw and pop a dialog -- only after the
        /// EditorGUILayout horizontal group is closed, so a failure can't
        /// unwind through an open IMGUI layout group (KAN-7).
        /// </summary>
        private void DrawBranchBar()
        {
            EditorGUILayout.BeginHorizontal();

            var branchNames = config.branches.Select(b => b.name).ToArray();
            var currentIndex = Array.IndexOf(branchNames, config.currentBranch);
            var newIndex = EditorGUILayout.Popup("Branch", currentIndex, branchNames);
            string switchTarget = null;
            if (newIndex != currentIndex && newIndex >= 0)
                switchTarget = branchNames[newIndex];

            if (GUILayout.Button("+ New Branch", GUILayout.Width(100)))
                showNewBranchField = !showNewBranchField;

            EditorGUILayout.EndHorizontal();

            // Run the branch switch -- which can throw and pop a dialog -- only
            // after the layout group is closed, for the same reason the Create
            // and Commit handlers below defer their work past EndHorizontal:
            // RunCheckout only catches InvalidOperationException, so anything
            // else (corrupt config, deleted avatar root, a Unity prefab-API
            // exception) would unwind straight out of OnGUI through this
            // EndHorizontal and leave IMGUI spewing "Invalid GUILayout state"
            // until the window was reopened.
            if (switchTarget != null
                && ConfirmDiscardIfUncommitted("Switch Branch", $"Switch from '{config.currentBranch}' to '{switchTarget}'?"))
                RunCheckout(() => BranchManager.SwitchBranch(avatarRoot, switchTarget));

            if (showNewBranchField)
            {
                EditorGUILayout.BeginHorizontal();
                newBranchName = EditorGUILayout.TextField(newBranchName);
                var isValid = BranchConfigOps.CanCreate(config, newBranchName);
                GUI.enabled = isValid;
                var createClicked = GUILayout.Button("Create", GUILayout.Width(80));
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                // Same reasoning as DrawCommitBar: CreateBranch can throw
                // (CanCreate gates the button, but its checks and
                // CreateBranch's own validation aren't guaranteed identical),
                // and there was no catch here at all -- an exception would
                // unwind straight out of OnGUI past this EndHorizontal.
                if (createClicked)
                {
                    try
                    {
                        BranchManager.CreateBranch(avatarRoot, newBranchName);
                        newBranchName = "";
                        showNewBranchField = false;
                        Reload();
                    }
                    catch (Exception e) when (e is ArgumentException or InvalidOperationException)
                    {
                        EditorUtility.DisplayDialog("Create Branch Failed", e.Message, "OK");
                    }
                }

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

        private const string CommitMessageControlName = "AvatarVcsCommitMessageField";

        /// <summary>
        /// The commit message field and Commit button (Enter in the field
        /// commits too). The commit itself -- which can throw, show a
        /// "Commit Failed" dialog, and early-return -- runs only after the
        /// EditorGUILayout horizontal group is closed, so a failed commit
        /// can't skip EndHorizontal and leave IMGUI in an invalid layout
        /// state (KAN-7).
        /// </summary>
        private void DrawCommitBar()
        {
            EditorGUILayout.BeginHorizontal();
            GUI.SetNextControlName(CommitMessageControlName);
            commitMessage = EditorGUILayout.TextField(commitMessage);

            // Enter while the message field has focus commits, same as
            // clicking the button -- typing a message then reaching for the
            // mouse just to click Commit is friction for the single most
            // frequent action in this window.
            var enterInMessageField = Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                && GUI.GetNameOfFocusedControl() == CommitMessageControlName;
            if (enterInMessageField) Event.current.Use();

            var commitClicked = GUILayout.Button("Commit", GUILayout.Width(100)) || enterInMessageField;
            EditorGUILayout.EndHorizontal();

            // Run the commit -- which can throw, pop a dialog, and early-return
            // -- only after the layout group is closed. Doing it inside the
            // BeginHorizontal/EndHorizontal pair meant a failed commit (the
            // exact case this dialog exists to report: duplicate/nested
            // containers) skipped EndHorizontal and left IMGUI spewing
            // "Invalid GUILayout state" until the window was reopened.
            if (!commitClicked)
                return;

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
