using System.Linq;
using AvatarVcs.Editor.MaterialSettings;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.Menu
{
    /// <summary>
    /// "Clean Up Generated Materials": deletes the duplicate materials left
    /// behind by checkouts that no commit names and no open scene wears.
    ///
    /// A command the user runs, like the history cleanup next to it: it
    /// deletes assets, and working out what is safe to delete means reading
    /// every commit of every avatar.
    /// </summary>
    public static class AvatarVcsGeneratedMaterialsMenu
    {
        private const string SweepMenuPath = "Tools/AvatarVCS/Clean Up Generated Materials";
        private const string OverwriteSharedMenuPath = "Tools/AvatarVCS/Overwrite Shared Materials";

        /// <summary>
        /// A checkout writes the recorded settings onto the material itself.
        /// When something outside the avatar is wearing that same material it
        /// copies instead, so checking out one avatar can't silently restyle
        /// another -- and says so in the console. This is the way to say
        /// "no, change the material": for a project where the materials
        /// belong to one avatar anyway, the copy is just a file in the way.
        /// </summary>
        [MenuItem(OverwriteSharedMenuPath, false, 24)]
        private static void ToggleOverwriteShared() =>
            MaterialSettingsApplier.OverwriteSharedMaterials = !MaterialSettingsApplier.OverwriteSharedMaterials;

        [MenuItem(OverwriteSharedMenuPath, true)]
        private static bool ValidateToggleOverwriteShared()
        {
            global::UnityEditor.Menu.SetChecked(OverwriteSharedMenuPath, MaterialSettingsApplier.OverwriteSharedMaterials);
            return true;
        }

        [MenuItem(SweepMenuPath, false, 23)]
        private static void SweepMenuItem()
        {
            GeneratedMaterialSweepPlan plan;
            try
            {
                EditorUtility.DisplayProgressBar("AvatarVCS", "Reading commits...", 0.5f);
                plan = GeneratedMaterialSweeper.Plan();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (plan.Blocked)
            {
                EditorUtility.DisplayDialog("AvatarVCS — Clean Up Generated Materials",
                    "Nothing was deleted.\n\n"
                    + $"{plan.unreadableCommits.Count} commit(s) could not be read, so which materials are still "
                    + "needed can't be answered. Deleting on a partial answer could take a material an avatar is "
                    + "wearing.\n\n"
                    + string.Join("\n", plan.unreadableCommits.Take(5)),
                    "OK");
                return;
            }

            var kept = plan.keptInUse + plan.keptReferenced;
            if (plan.deletable.Count == 0)
            {
                EditorUtility.DisplayDialog("AvatarVCS — Clean Up Generated Materials",
                    "Nothing to clean up.\n\n"
                    + $"{kept} generated material(s) found, and every one of them is either worn by an avatar in an "
                    + "open scene or named by a commit.",
                    "OK");
                return;
            }

            var body =
                $"Delete {plan.deletable.Count} generated material(s) ({EditorUtility.FormatBytes(plan.deletableBytes)})?\n\n"
                + "These are duplicates a checkout created. No commit names them and no renderer in an open scene "
                + "is using them.\n\n"
                + $"Keeping {plan.keptInUse} in use in an open scene and {plan.keptReferenced} named by a commit.\n\n"
                + string.Join("\n", plan.deletable.Take(10))
                + (plan.deletable.Count > 10 ? $"\n... and {plan.deletable.Count - 10} more" : string.Empty)
                + "\n\nOnly open scenes can be checked. A material used solely by a closed scene looks unused here.";

            if (!EditorUtility.DisplayDialog("AvatarVCS — Clean Up Generated Materials", body, "Delete", "Cancel"))
                return;

            var deleted = GeneratedMaterialSweeper.Apply(plan);
            Debug.Log($"[AvatarVCS] Deleted {deleted} generated material(s) "
                + $"({EditorUtility.FormatBytes(plan.deletableBytes)}).");
        }
    }
}
