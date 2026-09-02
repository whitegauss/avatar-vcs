using System.Linq;
using AvatarVcs.Core.Presentation;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.UI
{
    // Branch switcher/creation, commit, and checkout-selected-commit.
    // The actual work is the presenter's; the "run it only after the IMGUI
    // layout group is closed" discipline (KAN-7) stays here because it's a
    // drawing concern.
    public partial class AvatarVcsWindow
    {
        private void DrawBranchBar()
        {
            EditorGUILayout.BeginHorizontal();

            var config = presenter.Config;
            var branchNames = config.branches.Select(b => b.name).ToArray();
            var currentIndex = System.Array.IndexOf(branchNames, config.currentBranch);
            var newIndex = EditorGUILayout.Popup("Branch", currentIndex, branchNames);
            string switchTarget = null;
            if (newIndex != currentIndex && newIndex >= 0)
                switchTarget = branchNames[newIndex];

            if (GUILayout.Button("+ New Branch", GUILayout.Width(100)))
                showNewBranchField = !showNewBranchField;

            EditorGUILayout.EndHorizontal();

            // Dispatch the switch only after the layout group is closed: the
            // presenter can throw/pop a dialog and an exception unwinding
            // through an open BeginHorizontal leaves IMGUI in an invalid
            // state (KAN-7).
            if (switchTarget != null)
                presenter.SwitchBranch(switchTarget);

            if (showNewBranchField)
            {
                EditorGUILayout.BeginHorizontal();
                newBranchName = EditorGUILayout.TextField(newBranchName);
                var isValid = presenter.CanCreateBranch(newBranchName);
                GUI.enabled = isValid;
                var createClicked = GUILayout.Button("Create", GUILayout.Width(80));
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                if (createClicked && presenter.CreateBranch(newBranchName))
                {
                    newBranchName = "";
                    showNewBranchField = false;
                }

                if (!string.IsNullOrEmpty(newBranchName) && !isValid)
                    EditorGUILayout.HelpBox(WindowMessages.InvalidBranchName, MessageType.Warning);
            }
        }

        private void DrawUncommittedWarning()
        {
            if (presenter.ShowUncommittedWarning())
                EditorGUILayout.HelpBox("Uncommitted changes in the scene (see diff below).", MessageType.Warning);
        }

        private const string CommitMessageControlName = "AvatarVcsCommitMessageField";

        private void DrawCommitBar()
        {
            EditorGUILayout.BeginHorizontal();
            GUI.SetNextControlName(CommitMessageControlName);
            commitMessage = EditorGUILayout.TextField(commitMessage);

            // Enter while the message field has focus commits, same as the
            // button -- reaching for the mouse just to commit is friction for
            // the most frequent action in this window.
            var enterInMessageField = Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                && GUI.GetNameOfFocusedControl() == CommitMessageControlName;
            if (enterInMessageField) Event.current.Use();

            var commitClicked = GUILayout.Button("Commit", GUILayout.Width(100)) || enterInMessageField;
            EditorGUILayout.EndHorizontal();

            // Dispatch only after EndHorizontal -- see DrawBranchBar (KAN-7).
            if (!commitClicked)
                return;

            if (presenter.CommitCurrent(commitMessage))
                commitMessage = "";
        }

        private void DrawCheckoutBar()
        {
            GUI.enabled = presenter.CanCheckoutSelected();
            if (GUILayout.Button("Checkout Selected Commit"))
                presenter.CheckoutSelected();
            GUI.enabled = true;
        }
    }
}
