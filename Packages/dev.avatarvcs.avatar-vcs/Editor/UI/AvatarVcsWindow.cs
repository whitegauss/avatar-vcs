using System;
using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Editor.AvatarReferences;
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
    ///
    /// Split across partial-class files by feature area:
    /// this file (state, lifecycle, avatar selection, top-level OnGUI),
    /// .History.cs (commit history panel, diff panel, delete),
    /// .CommitBranch.cs (branch switcher, commit, checkout),
    /// .Compare.cs (compare-mode toggle/enter/exit),
    /// .Remap.cs (missing-prefab GUID remap UI).
    /// </summary>
    public partial class AvatarVcsWindow : EditorWindow
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

        // Set by hierarchyChanged/postprocessModifications, consumed once
        // per OnGUI so a scene edit is reflected without the user having to
        // remember to hit Refresh.
        private bool diffPossiblyStale;

        private string commitMessage = "";
        private bool showNewBranchField;
        private string newBranchName = "";

        private Vector2 historyScroll;
        private Vector2 diffScroll;
        private readonly Dictionary<string, bool> expandedContainers = new();

        // History panel's checkbox-driven bulk delete. Pruned in Reload so a
        // stale id (deleted elsewhere, or from a since-switched-away avatar)
        // never lingers as "selected".
        private readonly HashSet<string> selectedForBulkDelete = new();

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

        private void OnEnable()
        {
            // Auto-refresh the "uncommitted changes" diff instead of relying
            // on the user to remember to hit Refresh: hierarchyChanged
            // covers structural edits (add/remove/reparent an object or
            // component), postprocessModifications covers in-place value
            // edits (most Inspector field changes go through Undo). Both
            // just flag dirty + request a repaint rather than recomputing
            // synchronously, since a diff recompute walks every component
            // via SerializedObject and firing that on every keystroke of a
            // drag would be wasteful.
            EditorApplication.hierarchyChanged += OnSceneMaybeChanged;
            Undo.postprocessModifications += OnPostprocessModifications;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnSceneMaybeChanged;
            Undo.postprocessModifications -= OnPostprocessModifications;

            // Closing the window mid-compare would otherwise strand the
            // scene showing whichever side was last toggled to.
            if (compareModeActive && avatarRoot != null)
                ExitCompare(keepCurrent: false);
        }

        private void OnSceneMaybeChanged()
        {
            diffPossiblyStale = true;
            Repaint();
        }

        private UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            diffPossiblyStale = true;
            Repaint();
            return modifications;
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

            if (diffPossiblyStale)
            {
                diffPossiblyStale = false;
                // Only meaningful when diffing against the live scene; a
                // commit-vs-commit diff isn't affected by a scene edit.
                if (diffBaseCommitId == null)
                    RecomputeSelectedDiff();
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

            // Whether dragged into the field or picked via "Use Selected",
            // an object with no existing AvatarVCS structure of its own
            // (e.g. a single outfit item rather than the actual avatar)
            // must not silently become the tracked avatar -- resolve up to
            // the real owner if one exists, or confirm before adopting it.
            if (newRoot != null && newRoot != avatarRoot)
                newRoot = ResolveAvatarRoot(newRoot);

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

        /// <summary>
        /// Thin wrapper around ContainerManager's shared resolve-or-confirm
        /// logic: on cancel (or no existing structure that could be
        /// resolved), falls back to avatarRoot (the previous value, possibly
        /// null) rather than null, so the caller's != comparison naturally
        /// becomes a no-op instead of clearing the selection.
        /// </summary>
        private GameObject ResolveAvatarRoot(GameObject selection) =>
            ContainerManager.ResolveAvatarRootWithConfirmation(
                selection, "This window will treat it as the avatar to commit/checkout for.")
            ?? avatarRoot;

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
            selectedForBulkDelete.RemoveWhere(id => commits.All(c => c.commitId != id));
            RecomputeSelectedDiff();
        }

        private string CurrentHeadId() => config.branches.FirstOrDefault(b => b.name == config.currentBranch)?.commitId;

        private string CommitMessage(string commitId) =>
            commits.FirstOrDefault(c => c.commitId == commitId)?.message ?? commitId;

        // Commit messages aren't unique (e.g. repeated "Manual commit"), so
        // selection popups append a short id suffix to stay disambiguated.
        private static string CommitLabel(CommitIndexEntry entry) =>
            $"{entry.message} ({(entry.commitId.Length > 6 ? entry.commitId.Substring(0, 6) : entry.commitId)})";

        private void RunCheckout(Func<CheckoutResult> checkout)
        {
            CheckoutResult result;
            try
            {
                result = checkout();
            }
            catch (InvalidOperationException e)
            {
                // The safety-net auto-commit taken before a checkout runs
                // the same container validation as a manual Commit, so a
                // pre-existing structural problem (duplicate/nested
                // containers) surfaces here too -- before anything gets
                // destroyed, not after.
                EditorUtility.DisplayDialog("Checkout Failed", e.Message, "OK");
                return;
            }

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

        // Shared by the auto-refresh "uncommitted changes" diff and compare
        // mode's HasUncommittedChanges check -- both need "what does the
        // scene look like right now, in the same shape as a stored Commit".
        private Commit CaptureLiveState()
        {
            var configRoot = ContainerManager.FindRoot(avatarRoot);
            var liveContainers = ContainerManager.GetContainers(configRoot)
                .Select(c => ContainerCapture.CaptureContainer(c, avatarRoot.transform))
                .ToList();
            var (avatarReferences, materialSettings) = AvatarReferenceCollector.CollectFromTrackedTargets(avatarRoot);
            return new Commit
            {
                containers = liveContainers,
                avatarReferences = avatarReferences,
                materialSettings = materialSettings,
            };
        }
    }
}
