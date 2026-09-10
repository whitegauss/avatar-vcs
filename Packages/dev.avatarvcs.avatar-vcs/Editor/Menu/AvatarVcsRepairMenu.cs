using System.Linq;
using AvatarVcs.Core.Repair;
using AvatarVcs.Editor.Repair;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.Menu
{
    /// <summary>
    /// "Repair Markers": puts back the AvatarVCS components an update turned
    /// into "missing script" (see MarkerRepair for why that happened).
    ///
    /// A command the user runs rather than something that fires on its own.
    /// It adds components to objects in an open scene and can only be as
    /// right as the evidence left in the scene file, so it shows what it
    /// intends to do first -- and it is only ever needed once, on the way
    /// into the version that ships .meta files.
    /// </summary>
    public static class AvatarVcsRepairMenu
    {
        // Under Tools/ for the same reason as the cleanup commands: the
        // "Window/AvatarVCS" leaf can't also be a submenu.
        private const string RepairMenuPath = "Tools/AvatarVCS/Repair Markers";

        [MenuItem(RepairMenuPath, false, 22)]
        private static void RepairMenuItem()
        {
            var plan = MarkerRepair.Plan();

            if (plan.blockers.Count > 0)
            {
                EditorUtility.DisplayDialog("AvatarVCS — Repair Markers",
                    "Save your scenes first.\n\n" + string.Join("\n", plan.blockers), "OK");
                return;
            }

            if (plan.actions.Count == 0)
            {
                var body = plan.unresolved.Count == 0
                    ? "Nothing to repair. Every AvatarVCS component in the open scenes resolves to its script."
                    : "No marker could be identified from the scene file.\n\n"
                      + "These objects have a missing script inside an avatar, but nothing in the file or in the "
                      + "commit history says which marker it was. Re-apply them by hand with Track Properties Here "
                      + "or Untrack Properties Here:\n\n"
                      + string.Join("\n", plan.unresolved);
                EditorUtility.DisplayDialog("AvatarVCS — Repair Markers", body, "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("AvatarVCS — Repair Markers", Describe(plan), "Repair", "Cancel"))
                return;

            var applied = MarkerRepair.Apply(plan);
            Debug.Log($"[AvatarVCS] Repaired {applied} marker" + (applied == 1 ? "" : "s") + ".\n"
                + string.Join("\n", plan.actions.Select(a => $"  {a.kind}: {a.path}")));
        }

        private static string Describe(MarkerRepairPlan plan)
        {
            var byKind = plan.actions
                .GroupBy(a => a.kind)
                .OrderBy(g => g.Key)
                .Select(g => $"{Label(g.Key)} ({g.Count()})\n" + string.Join("\n", g.Select(a => "    " + a.path)));

            var body = $"Restore {plan.actions.Count} AvatarVCS "
                + (plan.actions.Count == 1 ? "component" : "components")
                + " that lost their script?\n\n"
                + string.Join("\n\n", byKind)
                + "\n\nThe guids come from the scene file, so the avatar keeps the commit history it already had.";

            if (plan.unresolved.Count > 0)
            {
                body += "\n\nLeft alone (nothing says which marker these were — re-apply by hand with "
                    + "Track Properties Here or Untrack Properties Here):\n"
                    + string.Join("\n", plan.unresolved.Select(p => "    " + p));
            }

            return body;
        }

        private static string Label(MarkerKind kind) => kind switch
        {
            MarkerKind.Root => "AvatarVCS root (keeps the commit history)",
            MarkerKind.Container => "Container",
            MarkerKind.Tracked => "Track Properties Here",
            MarkerKind.Untracked => "Untrack Properties Here",
            _ => kind.ToString(),
        };
    }
}
