using System;
using System.IO;
using System.Text.RegularExpressions;

namespace AvatarVcs.Core.Naming
{
    /// <summary>
    /// The single source of truth for how AvatarVCS names and places the
    /// assets it generates, and for recognising one again later.
    ///
    /// There is exactly one producer -- MaterialSettingsApplier duplicating a
    /// material -- and exactly one consumer that matters: the commit-deletion
    /// GC, which is the only code path in this package that calls
    /// AssetDatabase.DeleteAsset on a file in the user's project. Those two
    /// used to carry the convention independently with a "keep in sync"
    /// comment between them; they now share these constants so they cannot
    /// drift (KAN-76).
    /// </summary>
    public static class GeneratedAssetNaming
    {
        /// <summary>Appended to the source material's name.</summary>
        public const string Suffix = "_avatarvcs";

        /// <summary>
        /// Fallback folder for when the source material sits somewhere
        /// unwritable (a read-only UPM package), so a sibling asset can't be
        /// created next to it.
        /// </summary>
        public const string GeneratedFolder = "Assets/AvatarVCS_Generated";

        /// <summary>Every asset this package generates is a Material.</summary>
        public const string MaterialExtension = ".mat";

        public static string DuplicateName(string sourceMaterialName) => sourceMaterialName + Suffix;

        // "<source>_avatarvcs", optionally + AssetDatabase.GenerateUniqueAssetPath's
        // " 1", " 2", ... uniquifier, anchored to the end. Deliberately NOT a
        // substring match -- a user's "Coat_avatarvcs_backup.mat" must not
        // qualify as deletable.
        private static readonly Regex NamePattern =
            new(Regex.Escape(Suffix) + @"( \d+)?$", RegexOptions.Compiled);

        /// <summary>
        /// Whether assetPath looks like something this package generated, and
        /// is therefore a candidate for deletion when the commit that
        /// generated it is deleted.
        ///
        /// Deliberately strict, because the caller deletes what this returns
        /// true for and its input is a GUID out of commit JSON -- which this
        /// package treats as hand-editable and merge-corruptible everywhere
        /// else. A false negative leaves an orphaned duplicate material
        /// behind; a false positive destroys a file the user authored. So:
        ///
        /// - the extension must be .mat. The producer only ever creates
        ///   Materials, so a "Hair_avatarvcs.prefab" is somebody else's.
        /// - that also excludes folders, which AssetDatabase.GUIDToAssetPath
        ///   happily resolves and AssetDatabase.DeleteAsset removes
        ///   *recursively*.
        ///
        /// A user file named exactly "&lt;something&gt;_avatarvcs.mat" is still
        /// indistinguishable from our own output and will match. That residual
        /// overlap is inherent to a name-based scheme; the extension and
        /// folder constraints narrow it to the one shape we actually emit.
        /// </summary>
        public static bool LooksGenerated(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;

            var normalized = assetPath.Replace('\\', '/');
            if (!normalized.EndsWith(MaterialExtension, StringComparison.Ordinal)) return false;

            if (normalized.StartsWith(GeneratedFolder + "/", StringComparison.Ordinal)) return true;

            return NamePattern.IsMatch(Path.GetFileNameWithoutExtension(normalized));
        }
    }
}
