using System;
using System.Collections.Generic;
using AvatarVcs.Core.Presentation;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.UI
{
    /// <summary>
    /// Main window: branch switcher, commit history, and a structured diff
    /// view against the current live state. Design doc section 5.1.
    ///
    /// KAN-21 phase 4: this class is now just drawing and dispatch. Every
    /// stateful decision -- what to select, whether to take a safety-net
    /// commit, which dialog to show -- lives in AvatarVcsPresenter (Core,
    /// unit-tested against fakes). The window keeps only view-local state
    /// (scroll positions, foldouts, text-field buffers, the avatar
    /// ObjectField) and forwards user actions to presenter.XXX().
    ///
    /// Split across partial-class files by feature area:
    /// this file (lifecycle, avatar selection, top-level OnGUI),
    /// .History.cs (commit history panel, diff panel, delete),
    /// .CommitBranch.cs (branch switcher, commit, checkout),
    /// .Compare.cs (compare-mode toggle/enter/exit),
    /// .Remap.cs (missing-prefab GUID remap UI).
    /// </summary>
    public partial class AvatarVcsWindow : EditorWindow
    {
        // View-local state only.
        private GameObject avatarRoot;

        private bool diffPossiblyStale;

        // Survives a domain reload so a recompile doesn't silently reopen the
        // panel -- and with it the per-edit capture it gates.
        [SerializeField] private bool diffExpanded;

        private string commitMessage = "";
        private bool showNewBranchField;
        private string newBranchName = "";

        private Vector2 historyScroll;
        private Vector2 diffScroll;
        private readonly Dictionary<string, bool> expandedContainers = new();

        // Object-picker values for the remap UI; converted to GUIDs and
        // pushed into the presenter, which owns the missing-guid state.
        private readonly Dictionary<string, UnityEngine.Object> remapSelections = new();

        // Compare-mode state is the presenter's, but it must survive a domain
        // reload (KAN-16) -- OnDisable deliberately does NOT restore the scene
        // during one, so the window has to come back still in compare mode.
        // These [SerializeField] mirrors are synced from the presenter in
        // OnDisable and pushed back into it in OnEnable.
        [SerializeField] private bool compareModeActive;
        [SerializeField] private string compareCommitAId;
        [SerializeField] private string compareCommitBId;
        [SerializeField] private bool compareShowingB;
        [SerializeField] private string compareReturnCommitId;

        [NonSerialized] private bool domainReloadImminent;

        [NonSerialized] private EditorAvatarGateway gateway;
        [NonSerialized] private AvatarVcsPresenter presenter;

        [MenuItem("Window/AvatarVCS")]
        public static void Open()
        {
            GetWindow<AvatarVcsWindow>("AvatarVCS");
            RequestHistorySweep();
        }

        public static void OpenFor(GameObject avatarRoot)
        {
            var window = GetWindow<AvatarVcsWindow>("AvatarVCS");
            RequestHistorySweep();
            window.avatarRoot = avatarRoot;
            window.EnsurePresenter();
            window.gateway.AvatarRoot = avatarRoot;
            window.presenter.SetAvatarGuid(window.gateway.FindAvatarGuid());
        }

        /// <summary>
        /// Deliberately hung off the two "the user asked for this window"
        /// entry points, not OnEnable: OnEnable also runs when a domain
        /// reload re-creates an already-open window, so the sweep -- which
        /// can read every scene and prefab in the project -- would fire after
        /// a plain script recompile with the user never having opened
        /// anything. Deferred a frame so a first-time scan can't stall the
        /// window's first repaint.
        /// </summary>
        private static void RequestHistorySweep() =>
            EditorApplication.delayCall += AvatarHistoryAutoCleanup.RunIfDue;

        private void EnsurePresenter()
        {
            if (presenter != null) return;
            gateway = new EditorAvatarGateway { AvatarRoot = avatarRoot };
            presenter = new AvatarVcsPresenter(new EditorHistoryStore(), gateway, new EditorUserPrompt());
            presenter.RestoreCompareState(compareModeActive, compareCommitAId, compareCommitBId, compareShowingB, compareReturnCommitId);
        }

        private void SyncCompareStateToFields()
        {
            compareModeActive = presenter.CompareModeActive;
            compareCommitAId = presenter.CompareCommitAId;
            compareCommitBId = presenter.CompareCommitBId;
            compareShowingB = presenter.CompareShowingB;
            compareReturnCommitId = presenter.CompareReturnCommitId;
        }

        private void OnEnable()
        {
            EnsurePresenter();
            presenter.DiffEnabled = diffExpanded;

            // Auto-refresh the "uncommitted changes" diff instead of relying
            // on the user to remember to hit Refresh: hierarchyChanged covers
            // structural edits, postprocessModifications covers in-place value
            // edits. Both just flag dirty + request a repaint.
            EditorApplication.hierarchyChanged += OnSceneMaybeChanged;
            Undo.postprocessModifications += OnPostprocessModifications;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnSceneMaybeChanged;
            Undo.postprocessModifications -= OnPostprocessModifications;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;

            if (presenter != null) SyncCompareStateToFields();

            // A domain reload (recompile / enter play mode / quit) also runs
            // OnDisable, but a checkout here is unsafe mid-teardown: it
            // mutates the scene and can pop a modal dialog. The serialized
            // compare fields survive the reload, so OnEnable comes back still
            // in compare mode with the scene untouched. Only restore when
            // this is a genuine window close.
            if (domainReloadImminent) return;

            if (presenter != null && presenter.CompareModeActive && avatarRoot != null)
                presenter.ExitCompare(keepCurrent: false);
        }

        private void OnBeforeAssemblyReload() => domainReloadImminent = true;

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
            EnsurePresenter();
            DrawAvatarSelector();

            if (avatarRoot == null)
            {
                EditorGUILayout.HelpBox("Assign the avatar root GameObject above.", MessageType.Info);
                return;
            }

            gateway.AvatarRoot = avatarRoot;
            var guid = gateway.FindAvatarGuid();
            if (guid != presenter.AvatarGuid)
                presenter.SetAvatarGuid(guid);

            if (presenter.AvatarGuid == null)
            {
                EditorGUILayout.HelpBox("No commits yet for this avatar.", MessageType.Info);
                DrawCommitBar();
                return;
            }

            DrawRemapSection();
            DrawCompareBar();
            if (presenter.CompareModeActive) return;

            if (diffPossiblyStale)
            {
                diffPossiblyStale = false;
                // Only meaningful when diffing against the live scene, and
                // only worth the capture when the panel is actually open --
                // RecomputeSelectedDiff is a no-op otherwise, but skip the
                // call outright so the intent is visible here too.
                if (presenter.DiffEnabled && presenter.DiffBaseCommitId == null)
                    presenter.RecomputeSelectedDiff();
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

            // Whether dragged into the field or picked via "Use Selected", an
            // object with no existing AvatarVCS structure of its own must not
            // silently become the tracked avatar -- resolve up to the real
            // owner if one exists, or confirm before adopting it.
            if (newRoot != null && newRoot != avatarRoot)
                newRoot = ResolveAvatarRoot(newRoot);

            if (newRoot != avatarRoot)
            {
                // Leaving an avatar mid-compare would strand its scene showing
                // whichever side was last toggled to; restore it first.
                if (presenter.CompareModeActive && avatarRoot != null)
                    presenter.ExitCompare(keepCurrent: false);

                avatarRoot = newRoot;
                gateway.AvatarRoot = newRoot;
                presenter.SetAvatarGuid(newRoot != null ? gateway.FindAvatarGuid() : null);
            }
        }

        /// <summary>
        /// Thin wrapper around ContainerManager's shared resolve-or-confirm
        /// logic: on cancel (or no existing structure that could be
        /// resolved), falls back to avatarRoot (the previous value, possibly
        /// null) so the caller's != comparison naturally becomes a no-op
        /// instead of clearing the selection.
        /// </summary>
        private GameObject ResolveAvatarRoot(GameObject selection) =>
            ContainerManager.ResolveAvatarRootWithConfirmation(
                selection, "This window will treat it as the avatar to commit/checkout for.")
            ?? avatarRoot;
    }
}
