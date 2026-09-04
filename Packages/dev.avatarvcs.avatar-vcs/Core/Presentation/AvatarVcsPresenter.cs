using System;
using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Core.Diff;
using AvatarVcs.Core.History;
using AvatarVcs.Core.Model;

namespace AvatarVcs.Core.Presentation
{
    /// <summary>
    /// The state and transitions behind AvatarVcsWindow (KAN-21 phase 4).
    /// Everything that reads history, decides what to select, runs a
    /// checkout, or drives a dialog lives here, behind IHistoryStore /
    /// IAvatarGateway / IUserPrompt, so it can be unit-tested with fakes and
    /// no scene. The window keeps only drawing, dispatch, and view-only
    /// widgets (scroll position, foldout state, the avatar ObjectField).
    ///
    /// Behaviour is a faithful move of the pre-refactor window code; the
    /// dialog wording lives in WindowMessages.
    /// </summary>
    public sealed class AvatarVcsPresenter
    {
        private readonly IHistoryStore store;
        private readonly IAvatarGateway gateway;
        private readonly IUserPrompt prompt;

        public AvatarVcsPresenter(IHistoryStore store, IAvatarGateway gateway, IUserPrompt prompt)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            this.prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        }

        private string avatarGuid;
        private BranchConfig config = new();
        private List<CommitIndexEntry> commits = new();
        private string selectedCommitId;
        private List<ContainerDiff> selectedDiff = new();
        private bool diffEnabled;

        // See SelectedCommitNote: reading a note means parsing the whole
        // commit, so it is not something to do from OnGUI.
        private string cachedNoteCommitId;
        private string cachedNote = string.Empty;
        private string diffBaseCommitId;
        private readonly HashSet<string> selectedForBulkDelete = new();

        private List<string> pendingMissingGuids;
        private Func<CheckoutResult> pendingRetry;
        private readonly Dictionary<string, string> remapSelections = new();

        private bool compareModeActive;
        private string compareCommitAId;
        private string compareCommitBId;
        private bool compareShowingB;
        private string compareReturnCommitId;

        // ---- read-only view state ----
        public string AvatarGuid => avatarGuid;
        public BranchConfig Config => config;
        public IReadOnlyList<CommitIndexEntry> Commits => commits;
        public string SelectedCommitId => selectedCommitId;
        public IReadOnlyList<ContainerDiff> SelectedDiff => selectedDiff;

        /// <summary>
        /// Whether the diff panel is open. While it is shut, no diff is
        /// computed at all.
        ///
        /// Diffing against the live scene means capturing the whole avatar --
        /// every container, every tracked component, and since shader
        /// settings started being recorded, tens of thousands of material
        /// property reads. The window asks for that again after any scene
        /// edit, so with the panel open the cost lands on every slider drag,
        /// not just on checkout. Nobody reads a diff they haven't opened.
        /// </summary>
        public bool DiffEnabled
        {
            get => diffEnabled;
            set
            {
                if (diffEnabled == value) return;
                diffEnabled = value;
                if (diffEnabled) RecomputeSelectedDiff();
                else selectedDiff = new List<ContainerDiff>();
            }
        }
        public string DiffBaseCommitId => diffBaseCommitId;
        public IReadOnlyCollection<string> SelectedForBulkDelete => selectedForBulkDelete;
        public IReadOnlyList<string> PendingMissingGuids => pendingMissingGuids;
        public bool CompareModeActive => compareModeActive;
        public bool CompareShowingB => compareShowingB;
        public string CompareReturnCommitId => compareReturnCommitId;
        public string CurrentHeadId => BranchConfigOps.HeadOf(config, config.currentBranch);

        // The A/B pickers write these directly (view widgets); the rest of
        // compare state is presenter-driven.
        public string CompareCommitAId
        {
            get => compareCommitAId;
            set => compareCommitAId = value;
        }

        public string CompareCommitBId
        {
            get => compareCommitBId;
            set => compareCommitBId = value;
        }

        // ---- avatar selection ----

        public void SetAvatarGuid(string guid)
        {
            if (guid == avatarGuid) return;
            avatarGuid = guid;
            selectedCommitId = null;
            selectedDiff = new List<ContainerDiff>();
            diffBaseCommitId = null;
            compareModeActive = false;
            compareCommitAId = null;
            compareCommitBId = null;
            selectedForBulkDelete.Clear();
            pendingMissingGuids = null;
            pendingRetry = null;
            remapSelections.Clear();
            Reload();
        }

        // ---- history ----

        public void Reload()
        {
            if (avatarGuid == null)
            {
                config = new BranchConfig();
                commits = new List<CommitIndexEntry>();
                selectedCommitId = null;
                selectedDiff = new List<ContainerDiff>();
                return;
            }

            cachedNoteCommitId = null;

            config = store.LoadConfig(avatarGuid);
            commits = store.LoadIndex(avatarGuid).entries
                .OrderByDescending(e => e.timestamp)
                .ToList();

            var head = BranchConfigOps.HeadOf(config, config.currentBranch);
            selectedCommitId = commits.Any(c => c.commitId == head) ? head : commits.FirstOrDefault()?.commitId;
            if (diffBaseCommitId != null && commits.All(c => c.commitId != diffBaseCommitId))
                diffBaseCommitId = null;
            selectedForBulkDelete.RemoveWhere(id => commits.All(c => c.commitId != id));
            RecomputeSelectedDiff();
        }

        /// <summary>
        /// The selected commit's note, or "" when there is none.
        ///
        /// Cached per selected commit. This used to load on every call, on
        /// the reasoning that "the note is small" -- but reading it means
        /// parsing the whole commit, which on a real avatar is megabytes, and
        /// the note panel calls this from OnGUI. That made every keystroke in
        /// the window a multi-megabyte disk read and parse.
        ///
        /// Refreshed when the selection moves, when a note is saved, and on
        /// Reload; nothing else in the editor writes a note.
        /// </summary>
        public string SelectedCommitNote()
        {
            if (avatarGuid == null || selectedCommitId == null) return string.Empty;

            if (selectedCommitId != cachedNoteCommitId)
            {
                cachedNote = store.LoadCommit(avatarGuid, selectedCommitId)?.note ?? string.Empty;
                cachedNoteCommitId = selectedCommitId;
            }

            return cachedNote;
        }

        /// <summary>
        /// Saves a note onto the selected commit, leaving everything else it
        /// recorded untouched. Returns false when there is nothing to save it
        /// onto -- no avatar, no selection, or the commit no longer loads.
        ///
        /// The commit's recorded state is immutable; the note is the one part
        /// meant to be written after the fact, which is why this reloads and
        /// rewrites rather than taking a Commit from the caller.
        /// </summary>
        public bool SaveNoteOnSelectedCommit(string note)
        {
            if (avatarGuid == null || selectedCommitId == null) return false;

            var commit = store.LoadCommit(avatarGuid, selectedCommitId);
            if (commit == null) return false;

            commit.note = string.IsNullOrWhiteSpace(note) ? null : note;
            store.SaveCommit(avatarGuid, commit);

            cachedNoteCommitId = selectedCommitId;
            cachedNote = commit.note ?? string.Empty;
            return true;
        }

        public void RecomputeSelectedDiff()
        {
            selectedDiff = new List<ContainerDiff>();
            if (!diffEnabled) return;
            if (avatarGuid == null || selectedCommitId == null) return;

            var selectedCommit = store.LoadCommit(avatarGuid, selectedCommitId);
            if (selectedCommit == null) return;

            Commit other;
            if (diffBaseCommitId == null)
            {
                other = gateway.CaptureLiveState();
            }
            else
            {
                other = store.LoadCommit(avatarGuid, diffBaseCommitId);
                if (other == null) return;
            }

            selectedDiff = SnapshotDiffer.Diff(selectedCommit, other);
        }

        public void SelectCommit(string commitId)
        {
            if (commitId == selectedCommitId) return;
            selectedCommitId = commitId;
            RecomputeSelectedDiff();
        }

        // Commit messages aren't unique, so selection popups append a short id.
        public static string CommitLabel(CommitIndexEntry entry) =>
            $"{entry.message} ({(entry.commitId.Length > 6 ? entry.commitId.Substring(0, 6) : entry.commitId)})";

        public string CommitMessageOf(string commitId) =>
            commits.FirstOrDefault(c => c.commitId == commitId)?.message ?? commitId;

        /// <summary>
        /// Options for the "Diff against" popup: index 0 is always
        /// ("Current Scene", null), then one per commit newest-first.
        /// </summary>
        public IReadOnlyList<(string label, string id)> DiffBaseOptions()
        {
            var options = new List<(string label, string id)> { ("Current Scene", null) };
            options.AddRange(commits.Select(c => (CommitLabel(c), c.commitId)));
            return options;
        }

        public void SelectDiffBaseByIndex(int index)
        {
            var options = DiffBaseOptions();
            if (index < 0 || index >= options.Count) index = 0;
            var id = options[index].id;
            if (id == diffBaseCommitId) return;
            diffBaseCommitId = id;
            RecomputeSelectedDiff();
        }

        // ---- bulk delete selection ----

        public void SetBulkDeleteSelected(string commitId, bool selected)
        {
            if (selected) selectedForBulkDelete.Add(commitId);
            else selectedForBulkDelete.Remove(commitId);
        }

        public void ClearBulkDelete() => selectedForBulkDelete.Clear();

        public void DeleteCommit(string commitId)
        {
            if (commitId == CurrentHeadId)
            {
                prompt.Alert(WindowMessages.CantDeleteHeadTitle, WindowMessages.CantDeleteHeadBody);
                return;
            }

            if (!prompt.Confirm(WindowMessages.DeleteCommitTitle, WindowMessages.DeleteCommitBody, "Delete", "Cancel"))
                return;

            try
            {
                store.DeleteCommit(avatarGuid, commitId);
            }
            catch (InvalidOperationException e)
            {
                prompt.Alert(WindowMessages.DeleteFailedTitle, e.Message + WindowMessages.DeleteBlockedSuffix);
                return;
            }

            if (selectedCommitId == commitId) selectedCommitId = null;
            Reload();
        }

        public void DeleteSelected()
        {
            var ids = selectedForBulkDelete.ToList();
            if (ids.Count == 0) return;

            if (!prompt.Confirm(WindowMessages.BulkDeleteTitle, WindowMessages.BulkDeleteBody(ids.Count), "Delete", "Cancel"))
                return;

            // Blocked ids' messages are only available before Reload replaces
            // `commits` with the post-delete state.
            var blocked = store.DeleteCommits(avatarGuid, ids);
            var blockedMessages = blocked.Select(id => WindowMessages.BlockedByHead(CommitMessageOf(id))).ToList();

            foreach (var id in ids)
            {
                if (blocked.Contains(id)) continue;
                selectedForBulkDelete.Remove(id);
                if (selectedCommitId == id) selectedCommitId = null;
            }

            Reload();

            if (blockedMessages.Count > 0)
                prompt.Alert(WindowMessages.SomeNotDeletedTitle, WindowMessages.SomeNotDeletedBody(blockedMessages));
        }

        // ---- commit / branch ----

        /// <summary>Returns true when the commit succeeded (so the view can clear its message field).</summary>
        public bool CommitCurrent(string message)
        {
            var resolved = string.IsNullOrEmpty(message) ? "Manual commit" : message;
            try
            {
                gateway.CommitCurrentState(resolved);
            }
            catch (InvalidOperationException e)
            {
                prompt.Alert("Commit Failed", e.Message);
                return false;
            }

            // The very first commit creates the root (and its guid).
            avatarGuid = gateway.FindAvatarGuid();
            Reload();
            return true;
        }

        public bool CanCreateBranch(string name) => BranchConfigOps.CanCreate(config, name);

        /// <summary>Returns true when the branch was created (so the view can clear its input).</summary>
        public bool CreateBranch(string name)
        {
            try
            {
                gateway.CreateBranch(name);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                prompt.Alert("Create Branch Failed", e.Message);
                return false;
            }

            Reload();
            return true;
        }

        public void SwitchBranch(string name)
        {
            if (!ConfirmDiscardIfUncommitted("Switch Branch", $"Switch from '{config.currentBranch}' to '{name}'?"))
                return;
            RunCheckout(() => gateway.SwitchBranch(name));
        }

        public bool CanCheckoutSelected() =>
            selectedCommitId != null && selectedCommitId != CurrentHeadId;

        public void CheckoutSelected()
        {
            if (!CanCheckoutSelected()) return;
            if (!ConfirmDiscardIfUncommitted("Checkout Commit", "Checkout the selected commit?")) return;
            RunCheckout(() => gateway.RestoreToCommit(selectedCommitId));
        }

        public bool ShowUncommittedWarning() =>
            diffBaseCommitId == null
            && selectedDiff.Any(d => d.kind != DiffKind.Unchanged)
            && selectedCommitId == CurrentHeadId;

        public bool HasUncommittedChanges(string headCommitId)
        {
            if (string.IsNullOrEmpty(headCommitId)) return true;
            var head = store.LoadCommit(avatarGuid, headCommitId);
            if (head == null) return true;
            return SnapshotDiffer.Diff(head, gateway.CaptureLiveState()).Any(d => d.kind != DiffKind.Unchanged);
        }

        private bool ConfirmDiscardIfUncommitted(string title, string action)
        {
            if (!HasUncommittedChanges(CurrentHeadId))
                return prompt.Confirm(title, action, "OK", "Cancel");

            return prompt.Confirm(title,
                action + "\n\nUncommitted changes in the scene will be discarded (undo with Ctrl+Z if needed).",
                "Discard and Continue", "Cancel");
        }

        // ---- checkout plumbing (missing-prefab remap, version warnings) ----

        private void RunCheckout(Func<CheckoutResult> op)
        {
            CheckoutResult result;
            try
            {
                result = op();
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException)
            {
                prompt.Alert(WindowMessages.CheckoutFailedTitle, e.Message);
                return;
            }

            if (!result.IsSuccess)
            {
                pendingMissingGuids = result.MissingPrefabGuids;
                remapSelections.Clear();
                pendingRetry = op;
                return;
            }

            pendingMissingGuids = null;
            pendingRetry = null;

            if (result.VersionWarnings.Count > 0)
                prompt.Alert(WindowMessages.AssetVersionsChangedTitle,
                    WindowMessages.AssetVersionsChangedBody(result.VersionWarnings));

            Reload();
        }

        public void SetRemapSelection(string missingGuid, string newGuid)
        {
            if (string.IsNullOrEmpty(newGuid)) remapSelections.Remove(missingGuid);
            else remapSelections[missingGuid] = newGuid;
        }

        public bool CanApplyRemap() =>
            pendingMissingGuids != null
            && pendingMissingGuids.Count > 0
            && pendingMissingGuids.All(g => remapSelections.TryGetValue(g, out var v) && !string.IsNullOrEmpty(v));

        public void ApplyRemapAndRetry()
        {
            if (!CanApplyRemap()) return;

            foreach (var guid in pendingMissingGuids)
                gateway.RegisterGuidRemap(guid, remapSelections[guid]);

            var retry = pendingRetry;
            pendingMissingGuids = null;
            remapSelections.Clear();
            pendingRetry = null;

            if (retry != null) RunCheckout(retry);
        }

        public void CancelRemap()
        {
            pendingMissingGuids = null;
            remapSelections.Clear();
            pendingRetry = null;
        }

        // ---- compare mode ----

        public bool CanStartCompare() =>
            !compareModeActive
            && !string.IsNullOrEmpty(compareCommitAId)
            && !string.IsNullOrEmpty(compareCommitBId)
            && compareCommitAId != compareCommitBId;

        public void StartCompare()
        {
            var sourceBranch = config.currentBranch;
            var originalHeadId = CurrentHeadId;
            var commitAId = compareCommitAId;

            RunCheckout(() =>
            {
                var commit = store.LoadCommit(avatarGuid, commitAId);
                if (commit == null)
                    throw new InvalidOperationException($"Commit '{commitAId}' could not be loaded.");

                // Only take the safety-net auto-commit when there's actually
                // uncommitted work to protect; otherwise "Restore Original"
                // would land on a redundant [auto] commit.
                CheckoutResult result;
                if (HasUncommittedChanges(originalHeadId))
                {
                    result = gateway.CheckoutForCompare(commit, takeAutoCommit: true, sourceBranch, originalHeadId);
                    if (result.IsSuccess) compareReturnCommitId = result.AutoCommitId;
                }
                else
                {
                    result = gateway.CheckoutForCompare(commit, takeAutoCommit: false, sourceBranch, originalHeadId);
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

        public void ToggleCompare()
        {
            if (!compareModeActive) return;

            var currentlyShownId = compareShowingB ? compareCommitBId : compareCommitAId;
            if (HasUncommittedChanges(currentlyShownId))
            {
                if (!prompt.Confirm("Discard Scene Edits?",
                        "The scene has changed since you entered (or last toggled) compare mode. "
                        + "Toggling now will discard those changes -- compare mode doesn't take a "
                        + "safety-net commit on toggle.",
                        "Discard and Toggle", "Cancel"))
                    return;
            }

            var targetId = compareShowingB ? compareCommitAId : compareCommitBId;

            RunCheckout(() =>
            {
                var target = store.LoadCommit(avatarGuid, targetId);
                if (target == null)
                    throw new InvalidOperationException($"Commit '{targetId}' could not be loaded.");
                var result = gateway.CheckoutForCompare(target, takeAutoCommit: false, config.currentBranch, null);
                if (result.IsSuccess)
                    compareShowingB = !compareShowingB;
                return result;
            });
        }

        public void ExitCompare(bool keepCurrent)
        {
            var targetCommitId = keepCurrent
                ? (compareShowingB ? compareCommitBId : compareCommitAId)
                : compareReturnCommitId;

            compareModeActive = false;
            RunCheckout(() => gateway.RestoreToCommit(targetCommitId));
        }

        /// <summary>
        /// Push compare state the window persisted across a domain reload
        /// ([SerializeField] mirrors) back into the presenter.
        /// </summary>
        public void RestoreCompareState(bool active, string aId, string bId, bool showingB, string returnId)
        {
            compareModeActive = active;
            compareCommitAId = aId;
            compareCommitBId = bId;
            compareShowingB = showingB;
            compareReturnCommitId = returnId;
        }
    }
}
