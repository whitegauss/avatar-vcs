using System;
using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Core.History;
using AvatarVcs.Core.Model;
using AvatarVcs.Core.Presentation;
using NUnit.Framework;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// KAN-21/22: AvatarVcsPresenter drives every stateful decision the
    /// AvatarVCS window used to make inline. These cover it against fakes --
    /// no scene, no AssetDatabase, no dialogs. (KAN-22 relocates this to
    /// Tests/Core; it already has no Unity dependency.)
    /// </summary>
    [Category("Core")]
    public class AvatarVcsPresenterTests
    {
        private const string Guid = "avatar1";

        private FakeHistoryStore store;
        private FakeAvatarGateway gateway;
        private FakeUserPrompt prompt;
        private AvatarVcsPresenter presenter;

        [SetUp]
        public void SetUp()
        {
            store = new FakeHistoryStore();
            gateway = new FakeAvatarGateway();
            prompt = new FakeUserPrompt();
            presenter = new AvatarVcsPresenter(store, gateway, prompt);
        }

        // ---- fakes ----

        private sealed class FakeHistoryStore : IHistoryStore
        {
            public BranchConfig Config = new();
            public readonly List<CommitIndexEntry> Index = new();
            public readonly Dictionary<string, Commit> Commits = new();
            public readonly List<string> Deleted = new();
            public Func<IEnumerable<string>, List<string>> DeleteCommitsBehavior;
            public Exception DeleteCommitThrows;

            public void AddCommit(string id, string message, string timestamp, string branch = "main")
            {
                Index.Add(new CommitIndexEntry { commitId = id, message = message, timestamp = timestamp, branch = branch });
                Commits[id] = new Commit { commitId = id, message = message, branch = branch };
            }

            public BranchConfig LoadConfig(string avatarGuid) => Config;
            public CommitIndex LoadIndex(string avatarGuid) => new() { entries = Index.ToList() };
            public Commit LoadCommit(string avatarGuid, string commitId) =>
                commitId != null && Commits.TryGetValue(commitId, out var c) ? c : null;

            public void DeleteCommit(string avatarGuid, string commitId)
            {
                if (DeleteCommitThrows != null) throw DeleteCommitThrows;
                Deleted.Add(commitId);
                Index.RemoveAll(e => e.commitId == commitId);
                Commits.Remove(commitId);
            }

            public readonly List<string> Saved = new();

            public void SaveCommit(string avatarGuid, Commit commit)
            {
                Saved.Add(commit.commitId);
                Commits[commit.commitId] = commit;
            }

            public List<string> DeleteCommits(string avatarGuid, IEnumerable<string> commitIds)
            {
                var ids = commitIds.ToList();
                var blocked = DeleteCommitsBehavior?.Invoke(ids) ?? new List<string>();
                foreach (var id in ids.Where(i => !blocked.Contains(i)))
                {
                    Deleted.Add(id);
                    Index.RemoveAll(e => e.commitId == id);
                    Commits.Remove(id);
                }
                return blocked;
            }
        }

        private sealed class FakeAvatarGateway : IAvatarGateway
        {
            public string Guid = AvatarVcsPresenterTests.Guid;
            public Commit LiveState = new();
            public CheckoutResult NextResult = CheckoutResult.Success(null);
            public Exception CommitThrows;
            public Exception CreateBranchThrows;

            public readonly List<string> Committed = new();
            public readonly List<string> CreatedBranches = new();
            public readonly List<string> SwitchedBranches = new();
            public readonly List<string> Restored = new();
            public readonly List<(string branch, bool auto, string parent)> CompareCheckouts = new();
            public readonly List<(string from, string to)> Remaps = new();

            public string FindAvatarGuid() => Guid;
            public int CaptureLiveStateCalls;
            public Commit CaptureLiveState()
            {
                CaptureLiveStateCalls++;
                return LiveState;
            }

            public Commit CommitCurrentState(string message)
            {
                if (CommitThrows != null) throw CommitThrows;
                Committed.Add(message);
                return new Commit { message = message };
            }

            public void CreateBranch(string name)
            {
                if (CreateBranchThrows != null) throw CreateBranchThrows;
                CreatedBranches.Add(name);
            }

            public CheckoutResult SwitchBranch(string name)
            {
                SwitchedBranches.Add(name);
                return NextResult;
            }

            public CheckoutResult RestoreToCommit(string commitId)
            {
                Restored.Add(commitId);
                return NextResult;
            }

            public CheckoutResult CheckoutForCompare(Commit commit, bool takeAutoCommit, string sourceBranch, string autoCommitParentId)
            {
                CompareCheckouts.Add((sourceBranch, takeAutoCommit, autoCommitParentId));
                return NextResult;
            }

            public void RegisterGuidRemap(string fromGuid, string toGuid) => Remaps.Add((fromGuid, toGuid));
        }

        private sealed class FakeUserPrompt : IUserPrompt
        {
            public readonly Queue<bool> ConfirmAnswers = new();
            public readonly List<(string title, string body)> Confirms = new();
            public readonly List<(string title, string body)> Alerts = new();

            public bool Confirm(string title, string body, string ok, string cancel)
            {
                Confirms.Add((title, body));
                return ConfirmAnswers.Count > 0 ? ConfirmAnswers.Dequeue() : true;
            }

            public void Alert(string title, string body) => Alerts.Add((title, body));
        }

        private void SetBranchHead(string commitId) =>
            store.Config.branches.Add(new BranchEntry { name = "main", commitId = commitId });

        // ---- notes (KAN-94) ----

        // A note is the one part of a commit meant to be written after the
        // fact. The commit message names it in the list; the note is where
        // the detail goes.
        [Test]
        public void ANoteCanBeSavedOntoTheSelectedCommit_AndReadBack()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);

            Assert.AreEqual("", presenter.SelectedCommitNote(), "a commit with no note reads as empty, not null");

            Assert.IsTrue(presenter.SaveNoteOnSelectedCommit("outfit A + hair B\nshoulder toggle off"));

            Assert.AreEqual("outfit A + hair B\nshoulder toggle off", presenter.SelectedCommitNote());
            CollectionAssert.Contains(store.Saved, "c1");
        }

        [Test]
        public void SavingANote_LeavesTheRestOfTheCommitAlone()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            store.Commits["c1"].containers.Add(new ContainerSnapshot { containerId = "hair" });
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);

            presenter.SaveNoteOnSelectedCommit("just a note");

            var reloaded = store.LoadCommit(Guid, "c1");
            Assert.AreEqual("first", reloaded.message, "the message is not the note");
            Assert.AreEqual(1, reloaded.containers.Count, "recorded state is immutable; only the note is written");
        }

        [Test]
        public void AnEmptyNote_IsStoredAsAbsentRatherThanBlank()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);
            presenter.SaveNoteOnSelectedCommit("something");

            presenter.SaveNoteOnSelectedCommit("   ");

            Assert.IsNull(store.LoadCommit(Guid, "c1").note,
                "whitespace is not a note; keeping it would put a blank line in every commit file");
            Assert.AreEqual("", presenter.SelectedCommitNote());
        }

        [Test]
        public void SavingANote_WithNothingSelected_DoesNothing()
        {
            Assert.IsFalse(presenter.SaveNoteOnSelectedCommit("note"));
            CollectionAssert.IsEmpty(store.Saved);
        }

        // ---- Reload ----

        [Test]
        public void Reload_SelectsBranchHeadWhenItExists()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            store.AddCommit("c2", "second", "2026-01-02T00:00:00Z");
            SetBranchHead("c1");

            presenter.SetAvatarGuid(Guid);

            Assert.AreEqual("c1", presenter.SelectedCommitId);
        }

        [Test]
        public void Reload_SelectsNewestWhenBranchHeadMissingFromIndex()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            store.AddCommit("c2", "second", "2026-01-02T00:00:00Z");
            SetBranchHead("ghost");

            presenter.SetAvatarGuid(Guid);

            Assert.AreEqual("c2", presenter.SelectedCommitId, "newest by timestamp");
        }

        [Test]
        public void Reload_SelectsNullWhenHistoryEmpty()
        {
            presenter.SetAvatarGuid(Guid);
            Assert.IsNull(presenter.SelectedCommitId);
            Assert.IsEmpty(presenter.Commits);
        }

        [Test]
        public void Reload_ClearsDiffBaseThatNoLongerExists()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            store.AddCommit("c2", "second", "2026-01-02T00:00:00Z");
            presenter.SetAvatarGuid(Guid);
            presenter.SelectDiffBaseByIndex(1); // "c2" (index 0 is Current Scene)
            Assert.AreEqual("c2", presenter.DiffBaseCommitId);

            store.Index.RemoveAll(e => e.commitId == "c2");
            store.Commits.Remove("c2");
            presenter.Reload();

            Assert.IsNull(presenter.DiffBaseCommitId);
        }

        [Test]
        public void Reload_DropsStaleBulkDeleteSelection()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            store.AddCommit("c2", "second", "2026-01-02T00:00:00Z");
            presenter.SetAvatarGuid(Guid);
            presenter.SetBulkDeleteSelected("c1", true);
            presenter.SetBulkDeleteSelected("c2", true);

            store.Index.RemoveAll(e => e.commitId == "c2");
            store.Commits.Remove("c2");
            presenter.Reload();

            CollectionAssert.AreEquivalent(new[] { "c1" }, presenter.SelectedForBulkDelete);
        }

        // ---- DiffBaseOptions ----

        [Test]
        public void DiffBaseOptions_FirstIsCurrentSceneWithNullId_ThenCommits()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            store.AddCommit("c2", "second", "2026-01-02T00:00:00Z");
            presenter.SetAvatarGuid(Guid);

            var options = presenter.DiffBaseOptions();

            Assert.AreEqual("Current Scene", options[0].label);
            Assert.IsNull(options[0].id);
            Assert.AreEqual("c2", options[1].id, "newest first");
            Assert.AreEqual("c1", options[2].id);

            presenter.SelectDiffBaseByIndex(2);
            Assert.AreEqual("c1", presenter.DiffBaseCommitId);
            presenter.SelectDiffBaseByIndex(0);
            Assert.IsNull(presenter.DiffBaseCommitId);
        }

        // ---- CommitCurrent ----

        [Test]
        public void CommitCurrent_EmptyMessage_BecomesManualCommit()
        {
            presenter.SetAvatarGuid(Guid);
            presenter.CommitCurrent("");
            Assert.AreEqual(new[] { "Manual commit" }, gateway.Committed.ToArray());
        }

        [Test]
        public void CommitCurrent_Failure_AlertsAndDoesNotReload()
        {
            presenter.SetAvatarGuid(Guid);
            gateway.CommitThrows = new InvalidOperationException("nested containers");

            presenter.CommitCurrent("x");

            Assert.AreEqual("Commit Failed", prompt.Alerts.Single().title);
        }

        // ---- CanCreateBranch ----

        [Test]
        public void CanCreateBranch_RejectsInvalidAndDuplicateNames()
        {
            store.Config.branches.Add(new BranchEntry { name = "main", commitId = "c1" });
            store.Config.branches.Add(new BranchEntry { name = "feature", commitId = "c1" });
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            presenter.SetAvatarGuid(Guid);

            Assert.IsFalse(presenter.CanCreateBranch(""), "empty");
            Assert.IsFalse(presenter.CanCreateBranch("bad/name"), "forbidden char");
            Assert.IsFalse(presenter.CanCreateBranch("feature"), "duplicate");
            Assert.IsTrue(presenter.CanCreateBranch("feature-2"));
        }

        // ---- SwitchBranch / checkout plumbing ----

        [Test]
        public void SwitchBranch_ConfirmDeclined_DoesNotSwitch()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);
            prompt.ConfirmAnswers.Enqueue(false);

            presenter.SwitchBranch("other");

            Assert.IsEmpty(gateway.SwitchedBranches);
        }

        [Test]
        public void Checkout_MissingPrefabs_EntersRemapPendingAndRetainsRetry()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            store.AddCommit("c2", "second", "2026-01-02T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);
            presenter.SelectCommit("c2");
            gateway.NextResult = CheckoutResult.MissingPrefabs(new List<string> { "guidA", "guidB" });

            presenter.CheckoutSelected();

            CollectionAssert.AreEquivalent(new[] { "guidA", "guidB" }, presenter.PendingMissingGuids);
            Assert.IsFalse(presenter.CanApplyRemap(), "no replacements assigned yet");

            presenter.SetRemapSelection("guidA", "newA");
            Assert.IsFalse(presenter.CanApplyRemap(), "still one unassigned");
            presenter.SetRemapSelection("guidB", "newB");
            Assert.IsTrue(presenter.CanApplyRemap());

            gateway.NextResult = CheckoutResult.Success(null);
            presenter.ApplyRemapAndRetry();

            CollectionAssert.AreEquivalent(
                new[] { ("guidA", "newA"), ("guidB", "newB") }, gateway.Remaps);
            Assert.AreEqual(2, gateway.Restored.Count, "original checkout re-ran after remap");
            Assert.IsNull(presenter.PendingMissingGuids);
        }

        [Test]
        public void RunCheckout_VersionWarnings_ShowsAlert()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            store.AddCommit("c2", "second", "2026-01-02T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);
            presenter.SelectCommit("c2");
            gateway.NextResult = CheckoutResult.Success(null, new List<string> { "Body/mat.mat changed" });

            presenter.CheckoutSelected();

            Assert.AreEqual(WindowMessages.AssetVersionsChangedTitle, prompt.Alerts.Single().title);
        }

        // ---- DeleteSelected ----

        [Test]
        public void DeleteSelected_ReportsBlockedIdsAndKeepsThemSelected()
        {
            store.AddCommit("c1", "keeper", "2026-01-01T00:00:00Z");
            store.AddCommit("c2", "goner", "2026-01-02T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);
            presenter.SetBulkDeleteSelected("c1", true);
            presenter.SetBulkDeleteSelected("c2", true);
            store.DeleteCommitsBehavior = ids => ids.Where(i => i == "c1").ToList(); // c1 blocked (head)

            presenter.DeleteSelected();

            Assert.IsTrue(store.Deleted.Contains("c2"));
            Assert.IsFalse(store.Deleted.Contains("c1"));
            CollectionAssert.Contains(presenter.SelectedForBulkDelete, "c1", "blocked id stays selected");
            CollectionAssert.DoesNotContain(presenter.SelectedForBulkDelete, "c2");
            Assert.AreEqual(WindowMessages.SomeNotDeletedTitle, prompt.Alerts.Single().title);
            StringAssert.Contains("keeper", prompt.Alerts.Single().body);
        }

        [Test]
        public void DeleteCommit_HeadCommit_AlertsWithoutDeleting()
        {
            store.AddCommit("c1", "head", "2026-01-01T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);

            presenter.DeleteCommit("c1");

            Assert.IsEmpty(store.Deleted);
            Assert.AreEqual(WindowMessages.CantDeleteHeadTitle, prompt.Alerts.Single().title);
        }

        // ---- compare ----

        private void TwoCommitsHeadedAtC1()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            store.AddCommit("c2", "second", "2026-01-02T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);
            presenter.CompareCommitAId = "c1";
            presenter.CompareCommitBId = "c2";
        }

        [Test]
        public void StartCompare_WithUncommittedChanges_TakesSafetyNetAndReturnsToAutoCommit()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            store.AddCommit("c2", "second", "2026-01-02T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);
            presenter.CompareCommitAId = "c2"; // A still loads
            presenter.CompareCommitBId = "c1";
            store.Commits.Remove("c1"); // head no longer loads -> HasUncommittedChanges == true
            gateway.NextResult = CheckoutResult.Success("auto-123");

            presenter.StartCompare();

            Assert.IsTrue(presenter.CompareModeActive);
            Assert.IsTrue(gateway.CompareCheckouts.Single().auto, "safety-net checkout used");
            Assert.AreEqual("auto-123", presenter.CompareReturnCommitId);
        }

        [Test]
        public void StartCompare_NoUncommittedChanges_SkipsSafetyNetAndReturnsToOriginalHead()
        {
            TwoCommitsHeadedAtC1();
            // c1 loads as an empty commit and live state is empty -> no diff.
            gateway.NextResult = CheckoutResult.Success("auto-should-not-be-used");

            presenter.StartCompare();

            Assert.IsTrue(presenter.CompareModeActive);
            Assert.IsFalse(gateway.CompareCheckouts.Single().auto, "no safety-net checkout");
            Assert.AreEqual("c1", presenter.CompareReturnCommitId, "returns to the real original head");
        }

        [Test]
        public void ToggleCompare_WithUncommittedChanges_CancelledPromptDoesNotToggle()
        {
            TwoCommitsHeadedAtC1();
            gateway.NextResult = CheckoutResult.Success(null);
            presenter.StartCompare(); // enters compare, showing A
            gateway.CompareCheckouts.Clear();
            store.Commits.Remove("c1"); // now HasUncommittedChanges for the shown side
            prompt.ConfirmAnswers.Enqueue(false); // decline "Discard Scene Edits?"

            presenter.ToggleCompare();

            Assert.IsFalse(presenter.CompareShowingB, "still on A");
            Assert.IsEmpty(gateway.CompareCheckouts, "no checkout ran");
        }

        [Test]
        public void ExitCompare_KeepCurrentTargetsShownSide_OtherwiseTargetsReturnCommit()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            store.AddCommit("c2", "second", "2026-01-02T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);
            presenter.CompareCommitAId = "c2";
            presenter.CompareCommitBId = "c1";
            store.Commits.Remove("c1");                 // force the safety-net path
            gateway.NextResult = CheckoutResult.Success("auto-x");
            presenter.StartCompare();                   // showing A = "c2", compareReturnCommitId = "auto-x"

            presenter.ExitCompare(keepCurrent: true);
            Assert.AreEqual("c2", gateway.Restored.Last(), "keepCurrent restores the shown side (A)");

            gateway.Restored.Clear();
            presenter.CompareCommitAId = "c2";
            presenter.CompareCommitBId = "c1";
            presenter.StartCompare();
            presenter.ExitCompare(keepCurrent: false);
            Assert.AreEqual("auto-x", gateway.Restored.Last(), "otherwise restores compareReturnCommitId");
        }

        // KAN-93: a diff against the live scene captures the whole avatar,
        // and the window asks for one after any scene edit. With the panel
        // shut nobody is reading the result, so nothing should be captured.
        [Test]
        public void WithTheDiffPanelShut_ReloadCapturesNothing()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            SetBranchHead("c1");

            presenter.SetAvatarGuid(Guid);

            Assert.AreEqual(0, gateway.CaptureLiveStateCalls,
                "the scene must not be captured for a diff nobody opened");
            CollectionAssert.IsEmpty(presenter.SelectedDiff);
        }

        [Test]
        public void OpeningTheDiffPanel_CapturesAndComputesTheDiff()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);
            // Something for the diff to actually find, so this can't pass on
            // a regression that captures and then computes nothing.
            gateway.LiveState = new Commit { containers = { new ContainerSnapshot { containerId = "added" } } };

            presenter.DiffEnabled = true;

            Assert.AreEqual(1, gateway.CaptureLiveStateCalls);
            Assert.IsTrue(presenter.SelectedDiff.Any(d => d.containerId == "added" && d.kind == DiffKind.Added),
                "capturing is not the point; the diff has to come out of it");
        }

        [Test]
        public void ShuttingTheDiffPanel_DropsTheDiffAndStopsCapturing()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);
            presenter.DiffEnabled = true;
            var capturesWhileOpen = gateway.CaptureLiveStateCalls;

            presenter.DiffEnabled = false;
            presenter.RecomputeSelectedDiff();

            Assert.AreEqual(capturesWhileOpen, gateway.CaptureLiveStateCalls,
                "an explicit recompute must still do nothing while the panel is shut");
            CollectionAssert.IsEmpty(presenter.SelectedDiff);
        }

        // The trade-off, stated as a test rather than left to be discovered:
        // the passive "you have uncommitted changes" banner reads the same
        // diff, so it only appears once the panel is open. The safety net at
        // checkout time is HasUncommittedChanges, which computes on demand and
        // is unaffected.
        [Test]
        public void TheUncommittedBanner_NeedsTheDiffPanelOpen_ButTheCheckoutGuardDoesNot()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);
            gateway.LiveState = new Commit { containers = { new ContainerSnapshot { containerId = "added" } } };

            Assert.IsFalse(presenter.ShowUncommittedWarning(), "nothing computed, so nothing to show");

            Assert.IsTrue(presenter.HasUncommittedChanges("c1"),
                "the checkout guard computes on demand and still sees the change");

            presenter.DiffEnabled = true;
            Assert.IsTrue(presenter.ShowUncommittedWarning());
        }

        // KAN-77: HasUncommittedChanges has no try/catch and gates SwitchBranch
        // and CheckoutSelected, so an NRE inside SnapshotDiffer.Diff didn't just
        // break the diff view -- it locked the user out of both. A commit whose
        // lists arrived as explicit JSON null (hand-edit / botched merge) used
        // to do exactly that.
        [Test]
        public void SwitchBranch_StillWorks_WhenTheHeadCommitHasExplicitlyNullLists()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            SetBranchHead("c1");
            // A container survives into the diff (so DescribePrefabs and the
            // inner-property walk both run) but every list under it is null.
            store.Commits["c1"].containers = new List<ContainerSnapshot>
            {
                new()
                {
                    containerId = "hair",
                    prefabGuids = null,
                    components = null,
                    blendShapes = null,
                    materials = null,
                    objectStates = null,
                    materialSettings = null,
                },
            };
            store.Commits["c1"].avatarReferences = null;
            store.Commits["c1"].materialSettings = null;
            presenter.SetAvatarGuid(Guid);

            Assert.DoesNotThrow(() => presenter.SwitchBranch("dev"));
            Assert.AreEqual("dev", gateway.SwitchedBranches.Single());
        }
    }
}
