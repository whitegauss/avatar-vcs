using System;
using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using AvatarVcs.Editor.Model;
using AvatarVcs.Editor.Operations;
using AvatarVcs.Runtime;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.UI
{
    /// <summary>
    /// Main window: branch switcher, commit history, and a structured diff
    /// view against the current live state. Design doc section 5.1.
    ///
    /// Deliberately avoids mutating the scene just from being open: reading
    /// history only ever calls ContainerManager.FindRoot (never EnsureRoot),
    /// so viewing an avatar with no commits yet doesn't silently create the
    /// "[AvatarVCS]" root -- that only happens as a side effect of the user's
    /// first explicit Commit.
    /// </summary>
    public class AvatarVcsWindow : EditorWindow
    {
        private GameObject avatarRoot;
        private string avatarGuid;
        private BranchConfig config;
        private List<CommitIndexEntry> commits = new();
        private string selectedCommitId;
        private List<ContainerDiff> selectedDiff = new();

        private string commitMessage = "";
        private bool showNewBranchField;
        private string newBranchName = "";

        private Vector2 historyScroll;
        private Vector2 diffScroll;
        private readonly Dictionary<string, bool> expandedContainers = new();

        [MenuItem("Window/AvatarVCS")]
        public static void Open() => GetWindow<AvatarVcsWindow>("AvatarVCS");

        public static void OpenFor(GameObject avatarRoot)
        {
            var window = GetWindow<AvatarVcsWindow>("AvatarVCS");
            window.avatarRoot = avatarRoot;
            window.avatarGuid = null;
        }

        private void OnGUI()
        {
            DrawAvatarSelector();

            if (avatarRoot == null)
            {
                EditorGUILayout.HelpBox("Assign the avatar root GameObject above.", MessageType.Info);
                return;
            }

            var root = ContainerManager.FindRoot(avatarRoot);
            var guid = root != null ? root.GetComponent<AvatarVcsRoot>().AvatarGuid : null;
            if (guid != avatarGuid)
            {
                avatarGuid = guid;
                Reload();
            }

            if (avatarGuid == null)
            {
                EditorGUILayout.HelpBox("No commits yet for this avatar.", MessageType.Info);
                DrawCommitBar();
                return;
            }

            DrawBranchBar();
            DrawUncommittedWarning();

            EditorGUILayout.BeginHorizontal();
            DrawHistoryPanel();
            DrawDiffPanel();
            EditorGUILayout.EndHorizontal();

            DrawCommitBar();
            DrawCheckoutBar();
        }

        private void DrawAvatarSelector()
        {
            EditorGUILayout.BeginHorizontal();
            var newRoot = (GameObject)EditorGUILayout.ObjectField("Avatar", avatarRoot, typeof(GameObject), true);
            if (GUILayout.Button("Use Selected", GUILayout.Width(100)) && Selection.activeGameObject != null)
                newRoot = Selection.activeGameObject;
            EditorGUILayout.EndHorizontal();

            if (newRoot != avatarRoot)
            {
                avatarRoot = newRoot;
                avatarGuid = null;
                selectedCommitId = null;
                selectedDiff = new List<ContainerDiff>();
            }
        }

        private void Reload()
        {
            config = CommitStore.LoadConfig(avatarGuid);
            commits = CommitStore.LoadIndex(avatarGuid).entries
                .OrderByDescending(e => e.timestamp)
                .ToList();

            var head = config.branches.FirstOrDefault(b => b.name == config.currentBranch)?.commitId;
            selectedCommitId = commits.Any(c => c.commitId == head) ? head : commits.FirstOrDefault()?.commitId;
            RecomputeSelectedDiff();
        }

        private void DrawBranchBar()
        {
            EditorGUILayout.BeginHorizontal();

            var branchNames = config.branches.Select(b => b.name).ToArray();
            var currentIndex = Array.IndexOf(branchNames, config.currentBranch);
            var newIndex = EditorGUILayout.Popup("Branch", currentIndex, branchNames);
            if (newIndex != currentIndex && newIndex >= 0)
            {
                var target = branchNames[newIndex];
                if (EditorUtility.DisplayDialog("Switch Branch",
                        $"Switch from '{config.currentBranch}' to '{target}'? Current changes will be auto-committed first.",
                        "Switch", "Cancel"))
                {
                    RunCheckout(() => BranchManager.SwitchBranch(avatarRoot, target));
                }
            }

            if (GUILayout.Button("+ New Branch", GUILayout.Width(100)))
                showNewBranchField = !showNewBranchField;

            EditorGUILayout.EndHorizontal();

            if (showNewBranchField)
            {
                EditorGUILayout.BeginHorizontal();
                newBranchName = EditorGUILayout.TextField(newBranchName);
                GUI.enabled = !string.IsNullOrEmpty(newBranchName) && !config.branches.Any(b => b.name == newBranchName);
                if (GUILayout.Button("Create", GUILayout.Width(80)))
                {
                    BranchManager.CreateBranch(avatarRoot, newBranchName);
                    newBranchName = "";
                    showNewBranchField = false;
                    Reload();
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawUncommittedWarning()
        {
            var uncommitted = selectedDiff.Any(d => d.kind != DiffKind.Unchanged) && selectedCommitId == CurrentHeadId();
            if (uncommitted)
                EditorGUILayout.HelpBox("Uncommitted changes in the scene (see diff below).", MessageType.Warning);
        }

        private string CurrentHeadId() => config.branches.FirstOrDefault(b => b.name == config.currentBranch)?.commitId;

        private void DrawHistoryPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(220));
            EditorGUILayout.LabelField("History", EditorStyles.boldLabel);

            historyScroll = EditorGUILayout.BeginScrollView(historyScroll);
            var headId = CurrentHeadId();
            foreach (var entry in commits.ToList())
            {
                var label = entry.commitId == headId ? $"* {entry.message}" : $"  {entry.message}";
                var selected = entry.commitId == selectedCommitId;

                EditorGUILayout.BeginHorizontal();

                var prevBg = GUI.backgroundColor;
                if (selected) GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);
                if (GUILayout.Button(label) && !selected)
                {
                    selectedCommitId = entry.commitId;
                    RecomputeSelectedDiff();
                }
                GUI.backgroundColor = prevBg;

                var isHead = entry.commitId == headId;
                GUI.enabled = !isHead;
                if (GUILayout.Button("x", GUILayout.Width(20)))
                    DeleteCommit(entry.commitId);
                GUI.enabled = true;

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawDiffPanel()
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Diff (selected commit -> current scene)", EditorStyles.boldLabel);
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

        private void DrawCommitBar()
        {
            EditorGUILayout.BeginHorizontal();
            commitMessage = EditorGUILayout.TextField(commitMessage);
            if (GUILayout.Button("Commit", GUILayout.Width(100)))
            {
                var message = string.IsNullOrEmpty(commitMessage) ? "Manual commit" : commitMessage;
                BranchManager.Commit(avatarRoot, message);
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
            if (GUILayout.Button("Checkout Selected Commit"))
                RunCheckout(() => BranchManager.RestoreToCommit(avatarRoot, selectedCommitId));
            GUI.enabled = true;
        }

        private void RunCheckout(Func<CheckoutResult> checkout)
        {
            var result = checkout();
            if (!result.IsSuccess)
            {
                EditorUtility.DisplayDialog("Checkout Failed",
                    "Missing prefabs, checkout aborted:\n" + string.Join("\n", result.MissingPrefabGuids),
                    "OK");
                return;
            }

            Reload();
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
                EditorUtility.DisplayDialog("Delete Failed", e.Message, "OK");
                return;
            }

            if (selectedCommitId == commitId) selectedCommitId = null;
            Reload();
        }

        private void RecomputeSelectedDiff()
        {
            selectedDiff = new List<ContainerDiff>();
            if (avatarRoot == null || avatarGuid == null || selectedCommitId == null) return;

            var selectedCommit = CommitStore.LoadCommit(avatarGuid, selectedCommitId);
            if (selectedCommit == null) return;

            // Root is guaranteed to exist here: avatarGuid is only non-null
            // once a commit exists, which itself guarantees EnsureRoot ran.
            var configRoot = ContainerManager.FindRoot(avatarRoot);
            var liveContainers = ContainerManager.GetContainers(configRoot)
                .Select(c => ContainerCapture.CaptureContainer(c, avatarRoot.transform))
                .ToList();
            var livePreview = new Commit { containers = liveContainers };

            selectedDiff = SnapshotDiffer.Diff(selectedCommit, livePreview);
        }
    }
}
