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
            public int LoadCommitCalls;

            public Commit LoadCommit(string avatarGuid, string commitId)
            {
                LoadCommitCalls++;
                return commitId != null && Commits.TryGetValue(commitId, out var c) ? c : null;
            }

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

        // KAN-95: the note panel calls SelectedCommitNote from OnGUI, and
        // reading a note means parsing the whole commit -- megabytes on a
        // real avatar. Shipped in 0.7.0 as "every keystroke re-reads the
        // commit from disk". Assert the absence of the work, not just the
        // right answer.
        [Test]
        public void SelectedCommitNote_RepeatedCalls_HitTheStoreOnce()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);
            store.Commits["c1"].note = "a note";

            var before = store.LoadCommitCalls;
            for (var i = 0; i < 50; i++) presenter.SelectedCommitNote();

            Assert.AreEqual(before + 1, store.LoadCommitCalls,
                "OnGUI calls this every frame; it must not read the commit each time");
        }

        [Test]
        public void SelectedCommitNote_AfterTheSelectionMoves_ReadsTheNewCommit()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            store.AddCommit("c2", "second", "2026-01-02T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);
            store.Commits["c1"].note = "note one";
            store.Commits["c2"].note = "note two";

            Assert.AreEqual("note one", presenter.SelectedCommitNote());

            presenter.SelectCommit("c2");

            Assert.AreEqual("note two", presenter.SelectedCommitNote(),
                "the cache must follow the selection, not outlive it");
        }

        [Test]
        public void SelectedCommitNote_AfterSaving_ReflectsWhatWasSaved()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);
            presenter.SelectedCommitNote();

            presenter.SaveNoteOnSelectedCommit("written after the cache was warm");

            Assert.AreEqual("written after the cache was warm", presenter.SelectedCommitNote());
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

        // CanCreateBranch is the button's enabled state; CreateBranch is what
        // the button does. Only the first was covered, so the call could have
        // gone to the wrong gateway method, or skipped the re-read that the
        // branch popup draws from, without a test moving.
        [Test]
        public void CreateBranch_PassesTheNameToTheGateway_AndRereadsHistory()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);
            // Stands in for what creating a branch writes: something the
            // presenter can only be holding if it read history again.
            store.AddCommit("c2", "on the new branch", "2026-01-02T00:00:00Z", "dev");

            Assert.IsTrue(presenter.CreateBranch("dev"), "true lets the view clear its input");

            Assert.AreEqual(new[] { "dev" }, gateway.CreatedBranches.ToArray());
            Assert.IsTrue(presenter.Commits.Any(c => c.commitId == "c2"), "history is re-read");
            Assert.IsEmpty(prompt.Alerts);
        }

        // The name passed the pre-flight but BranchManager still refused it --
        // another window created it since this config was read, or it is
        // malformed by a rule the UI doesn't mirror. That arrives as an
        // exception mid-draw and has to come out as a dialog, not a console
        // stack trace.
        [TestCase(typeof(ArgumentException))]
        [TestCase(typeof(InvalidOperationException))]
        public void CreateBranch_Rejected_AlertsAndKeepsTheInput(Type thrown)
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);
            gateway.CreateBranchThrows = (Exception)Activator.CreateInstance(thrown, "branch 'dev' already exists");
            store.AddCommit("c2", "not reloaded", "2026-01-02T00:00:00Z", "dev");

            Assert.IsFalse(presenter.CreateBranch("dev"), "false keeps the typed name in the field");

            Assert.AreEqual("Create Branch Failed", prompt.Alerts.Single().title);
            Assert.AreEqual("branch 'dev' already exists", prompt.Alerts.Single().body);
            Assert.IsFalse(presenter.Commits.Any(c => c.commitId == "c2"), "nothing happened, so nothing to reload");
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

        // Cancel is the only way out of the remap prompt. If the retry
        // survived it, the next checkout the user ran would silently re-run
        // the one they backed out of.
        [Test]
        public void CancelRemap_DropsThePromptAndTheRetry()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            store.AddCommit("c2", "second", "2026-01-02T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);
            presenter.SelectCommit("c2");
            gateway.NextResult = CheckoutResult.MissingPrefabs(new List<string> { "guidA" });
            presenter.CheckoutSelected();
            presenter.SetRemapSelection("guidA", "newA");

            presenter.CancelRemap();

            Assert.IsNull(presenter.PendingMissingGuids);
            Assert.IsFalse(presenter.CanApplyRemap());

            presenter.ApplyRemapAndRetry();
            Assert.IsEmpty(gateway.Remaps, "the cancelled replacement must not be registered later");
            Assert.AreEqual(1, gateway.Restored.Count, "and the cancelled checkout must not re-run");
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

        // A checkout can throw rather than come back unsuccessful: the commit
        // file was deleted or hand-broken, a container is nested, the branch
        // moved underneath. RunCheckout is the only thing between that and an
        // unhandled exception out of OnGUI, which leaves the window throwing
        // on every repaint instead of saying what went wrong once.
        [Test]
        public void RunCheckout_WhenTheCheckoutThrows_AlertsAndChangesNothing()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            store.AddCommit("c2", "second", "2026-01-02T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);
            presenter.CompareCommitAId = "c2";
            presenter.CompareCommitBId = "c1";
            store.Commits.Remove("c2"); // the commit A points at no longer loads

            Assert.DoesNotThrow(() => presenter.StartCompare());

            Assert.AreEqual(WindowMessages.CheckoutFailedTitle, prompt.Alerts.Single().title);
            StringAssert.Contains("c2", prompt.Alerts.Single().body, "the dialog names the commit that failed");
            Assert.IsFalse(presenter.CompareModeActive, "a failed entry must not leave compare mode half-on");
            Assert.IsNull(presenter.PendingMissingGuids, "a throw is not a missing-prefab prompt");
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

        [Test]
        public void DeleteCommit_NonHead_ConfirmsThenDeletesAndMovesTheSelection()
        {
            store.AddCommit("c1", "head", "2026-01-01T00:00:00Z");
            store.AddCommit("c2", "goner", "2026-01-02T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);
            presenter.SelectCommit("c2");

            presenter.DeleteCommit("c2");

            Assert.AreEqual(WindowMessages.DeleteCommitTitle, prompt.Confirms.Single().title);
            Assert.AreEqual(new[] { "c2" }, store.Deleted.ToArray());
            Assert.IsFalse(presenter.Commits.Any(c => c.commitId == "c2"), "the list is re-read");
            Assert.AreEqual("c1", presenter.SelectedCommitId, "the deleted commit can't stay selected");
        }

        [Test]
        public void DeleteCommit_ConfirmDeclined_DeletesNothing()
        {
            store.AddCommit("c1", "head", "2026-01-01T00:00:00Z");
            store.AddCommit("c2", "goner", "2026-01-02T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);
            prompt.ConfirmAnswers.Enqueue(false);

            presenter.DeleteCommit("c2");

            Assert.IsEmpty(store.Deleted);
        }

        // The head check above only knows about the *current* branch. A commit
        // that heads some other branch gets past it and is refused down in the
        // store, and that refusal has to reach the user with the way out
        // appended -- the exception message alone says no, not what to do.
        [Test]
        public void DeleteCommit_RefusedByTheStore_AlertsWithTheWayOut()
        {
            store.AddCommit("c1", "head", "2026-01-01T00:00:00Z");
            store.AddCommit("c2", "head of dev", "2026-01-02T00:00:00Z", "dev");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);
            store.DeleteCommitThrows = new InvalidOperationException("Commit is the head of branch 'dev'.");

            presenter.DeleteCommit("c2");

            Assert.AreEqual(WindowMessages.DeleteFailedTitle, prompt.Alerts.Single().title);
            Assert.AreEqual("Commit is the head of branch 'dev'." + WindowMessages.DeleteBlockedSuffix,
                prompt.Alerts.Single().body);
            Assert.IsEmpty(store.Deleted);
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

        // The Start Compare button. Comparing a commit against itself is a
        // checkout dressed up as a comparison, and starting again while
        // already comparing would overwrite the return commit -- the only
        // record of where the user came from.
        [Test]
        public void CanStartCompare_NeedsTwoDistinctCommits_AndNoCompareAlreadyRunning()
        {
            TwoCommitsHeadedAtC1();

            presenter.CompareCommitAId = null;
            Assert.IsFalse(presenter.CanStartCompare(), "no A picked");

            presenter.CompareCommitAId = "c1";
            presenter.CompareCommitBId = null;
            Assert.IsFalse(presenter.CanStartCompare(), "no B picked");

            presenter.CompareCommitBId = "c1";
            Assert.IsFalse(presenter.CanStartCompare(), "A and B are the same commit");

            presenter.CompareCommitBId = "c2";
            Assert.IsTrue(presenter.CanStartCompare());

            presenter.StartCompare();
            Assert.IsFalse(presenter.CanStartCompare(), "already comparing");
        }

        // KAN-16: compare mode has to outlive a domain reload -- any script
        // recompile while the user is mid-comparison -- and the presenter is
        // rebuilt empty when that happens. The window pushes its
        // [SerializeField] mirrors back in. If that push doesn't take, the
        // scene still shows commit A while the presenter thinks compare mode
        // is off, so Toggle and Exit both no-op and the user is stuck in a
        // checked-out state with no button that leaves it.
        [Test]
        public void RestoreCompareState_PutsCompareModeBack_AndExitStillLeaves()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            store.AddCommit("c2", "second", "2026-01-02T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);

            presenter.RestoreCompareState(active: true, aId: "c1", bId: "c2", showingB: true, returnId: "auto-x");

            Assert.IsTrue(presenter.CompareModeActive);
            Assert.AreEqual("c1", presenter.CompareCommitAId);
            Assert.AreEqual("c2", presenter.CompareCommitBId);
            Assert.IsTrue(presenter.CompareShowingB, "the reload happened while B was on screen");
            Assert.AreEqual("auto-x", presenter.CompareReturnCommitId);

            presenter.ToggleCompare();
            Assert.IsFalse(presenter.CompareShowingB, "toggling from the restored state goes back to A");

            presenter.ExitCompare(keepCurrent: false);
            Assert.IsFalse(presenter.CompareModeActive);
            Assert.AreEqual("auto-x", gateway.Restored.Single(),
                "exit returns to what was recorded before the reload, not to nothing");
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

        // ---- view-state predicates ----

        // The Checkout button. The head is already in the scene, and with
        // nothing selected there is nothing to check out; both would restore
        // over the user's work for no gain.
        [Test]
        public void CanCheckoutSelected_IsFalseForTheHeadAndForNothingSelected()
        {
            store.AddCommit("c1", "head", "2026-01-01T00:00:00Z");
            store.AddCommit("c2", "older", "2026-01-02T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);

            Assert.IsFalse(presenter.CanCheckoutSelected(), "the head is already what the scene shows");

            presenter.SelectCommit("c2");
            Assert.IsTrue(presenter.CanCheckoutSelected());

            presenter.SelectCommit(null);
            Assert.IsFalse(presenter.CanCheckoutSelected(), "nothing selected");

            presenter.CheckoutSelected();
            Assert.IsEmpty(gateway.Restored, "the guard holds inside CheckoutSelected, not only on the button");
        }

        // The banner claims "uncommitted changes", which is only what the diff
        // means when it runs the live scene against the commit the scene is
        // supposed to match. The other two combinations produce rows that are
        // real differences but not uncommitted work, and calling them that
        // would push people into committing to get rid of a warning about
        // nothing.
        [Test]
        public void TheUncommittedBanner_StaysOff_WhenTheDiffIsntSceneAgainstHead()
        {
            store.AddCommit("c1", "head", "2026-01-01T00:00:00Z");
            store.AddCommit("c2", "older", "2026-01-02T00:00:00Z");
            SetBranchHead("c1");
            presenter.SetAvatarGuid(Guid);
            gateway.LiveState = new Commit { containers = { new ContainerSnapshot { containerId = "added" } } };
            presenter.DiffEnabled = true;

            Assert.IsTrue(presenter.ShowUncommittedWarning(), "scene against head, with a difference");

            presenter.SelectCommit("c2");
            Assert.IsTrue(presenter.SelectedDiff.Any(d => d.kind != DiffKind.Unchanged), "there are rows either way");
            Assert.IsFalse(presenter.ShowUncommittedWarning(),
                "an older commit differing from the scene is history, not uncommitted work");

            presenter.SelectCommit("c1");
            store.Commits["c2"].containers.Add(new ContainerSnapshot { containerId = "hair" });
            presenter.SelectDiffBaseByIndex(1); // against commit c2 instead of the live scene
            Assert.IsTrue(presenter.SelectedDiff.Any(d => d.kind != DiffKind.Unchanged), "there are rows either way");
            Assert.IsFalse(presenter.ShowUncommittedWarning(),
                "commit-to-commit rows say nothing about the scene");
        }

        [Test]
        public void ClearBulkDelete_UnticksEverything()
        {
            store.AddCommit("c1", "first", "2026-01-01T00:00:00Z");
            store.AddCommit("c2", "second", "2026-01-02T00:00:00Z");
            presenter.SetAvatarGuid(Guid);
            presenter.SetBulkDeleteSelected("c1", true);
            presenter.SetBulkDeleteSelected("c2", true);

            presenter.ClearBulkDelete();

            CollectionAssert.IsEmpty(presenter.SelectedForBulkDelete);

            presenter.DeleteSelected();
            Assert.IsEmpty(prompt.Confirms, "nothing ticked, so nothing to confirm");
            Assert.IsEmpty(store.Deleted);
        }

        // Commit messages aren't unique -- two "wip" rows in a popup are
        // indistinguishable without the id, which is what the suffix is for.
        [Test]
        public void CommitLabel_AppendsAShortCommitId()
        {
            Assert.AreEqual("wip (abcdef)", AvatarVcsPresenter.CommitLabel(
                new CommitIndexEntry { commitId = "abcdef0123456789", message = "wip" }));

            Assert.AreEqual("wip (abc)", AvatarVcsPresenter.CommitLabel(
                new CommitIndexEntry { commitId = "abc", message = "wip" }),
                "an id shorter than the cut is used whole rather than read past its end");
        }
    }
}
