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

        // Design doc 5.1: the diff panel compares the selected commit
        // against either the live scene (default, null) or another commit
        // picked here, to answer "what changed between these two commits".
        private string diffBaseCommitId;

        private string commitMessage = "";
        private bool showNewBranchField;
        private string newBranchName = "";

        private Vector2 historyScroll;
        private Vector2 diffScroll;
        private readonly Dictionary<string, bool> expandedContainers = new();

        // GUID remapping (design doc 6.4): populated when a checkout fails
        // with missing prefabs, so the user can point each one at its
        // re-imported replacement and retry.
        private List<string> pendingMissingGuids;
        private readonly Dictionary<string, UnityEngine.Object> remapSelections = new();
        private Func<CheckoutResult> pendingRetryCheckout;

        // Compare mode (design doc 5.2): toggle between two commits without
        // an auto-commit per flip. compareReturnCommitId is the safety-net
        // auto-commit taken once, right before entering compare mode.
        private bool compareModeActive;
        private string compareCommitAId;
        private string compareCommitBId;
        private bool compareShowingB;
        private string compareReturnCommitId;

        [MenuItem("Window/AvatarVCS")]
        public static void Open() => GetWindow<AvatarVcsWindow>("AvatarVCS");

        public static void OpenFor(GameObject avatarRoot)
        {
            var window = GetWindow<AvatarVcsWindow>("AvatarVCS");
            window.avatarRoot = avatarRoot;
            window.avatarGuid = null;
        }

        private void OnDisable()
        {
            // Closing the window mid-compare would otherwise strand the
            // scene showing whichever side was last toggled to.
            if (compareModeActive && avatarRoot != null)
                ExitCompare(keepCurrent: false);
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

            DrawRemapSection();
            DrawCompareBar();
            if (compareModeActive) return;

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
                // Leaving an avatar mid-compare would strand its scene
                // showing whichever side was last toggled to; restore it
                // before switching away.
                if (compareModeActive)
                    ExitCompare(keepCurrent: false);

                avatarRoot = newRoot;
                avatarGuid = null;
                selectedCommitId = null;
                selectedDiff = new List<ContainerDiff>();
                diffBaseCommitId = null;
                compareModeActive = false;
                compareCommitAId = null;
                compareCommitBId = null;
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
            if (diffBaseCommitId != null && commits.All(c => c.commitId != diffBaseCommitId))
                diffBaseCommitId = null;
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
            var uncommitted = diffBaseCommitId == null
                && selectedDiff.Any(d => d.kind != DiffKind.Unchanged)
                && selectedCommitId == CurrentHeadId();
            if (uncommitted)
                EditorGUILayout.HelpBox("Uncommitted changes in the scene (see diff below).", MessageType.Warning);
        }

        private string CurrentHeadId() => config.branches.FirstOrDefault(b => b.name == config.currentBranch)?.commitId;

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
                + "Toggling does not create commits.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(compareShowingB ? "Show A" : "Show B", GUILayout.Width(100)))
                ToggleCompare();
            if (GUILayout.Button("Keep This as Current State", GUILayout.Width(180)))
                ExitCompare(keepCurrent: true);
            if (GUILayout.Button("Restore Original", GUILayout.Width(130)))
                ExitCompare(keepCurrent: false);
            EditorGUILayout.EndHorizontal();
        }

        private string CommitMessage(string commitId) =>
            commits.FirstOrDefault(c => c.commitId == commitId)?.message ?? commitId;

        // Commit messages aren't unique (e.g. repeated "Manual commit"), so
        // selection popups append a short id suffix to stay disambiguated.
        private static string CommitLabel(CommitIndexEntry entry) =>
            $"{entry.message} ({(entry.commitId.Length > 6 ? entry.commitId.Substring(0, 6) : entry.commitId)})";

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

        private Commit CaptureLiveState()
        {
            var configRoot = ContainerManager.FindRoot(avatarRoot);
            var liveContainers = ContainerManager.GetContainers(configRoot)
                .Select(c => ContainerCapture.CaptureContainer(c, avatarRoot.transform))
                .ToList();
            return new Commit { containers = liveContainers };
        }

        private void ToggleCompare()
        {
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

        private void RunCheckout(Func<CheckoutResult> checkout)
        {
            var result = checkout();
            if (!result.IsSuccess)
            {
                pendingMissingGuids = result.MissingPrefabGuids;
                remapSelections.Clear();
                pendingRetryCheckout = checkout;
                return;
            }

            pendingMissingGuids = null;
            pendingRetryCheckout = null;

            if (result.VersionWarnings.Count > 0)
                EditorUtility.DisplayDialog("Asset Versions Changed",
                    "Checkout succeeded, but some referenced assets have changed since this commit was recorded "
                    + "(the result may look different):\n\n" + string.Join("\n", result.VersionWarnings),
                    "OK");

            Reload();
        }

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

        private void RecomputeSelectedDiff()
        {
            selectedDiff = new List<ContainerDiff>();
            if (avatarRoot == null || avatarGuid == null || selectedCommitId == null) return;

            var selectedCommit = CommitStore.LoadCommit(avatarGuid, selectedCommitId);
            if (selectedCommit == null) return;

            // Root is guaranteed to exist here: avatarGuid is only non-null
            // once a commit exists, which itself guarantees EnsureRoot ran.
            Commit other;
            if (diffBaseCommitId == null)
            {
                other = CaptureLiveState();
            }
            else
            {
                other = CommitStore.LoadCommit(avatarGuid, diffBaseCommitId);
                if (other == null) return;
            }

            selectedDiff = SnapshotDiffer.Diff(selectedCommit, other);
        }
    }
}
