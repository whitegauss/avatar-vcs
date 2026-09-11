using System.IO;
using AvatarVcs.Core.History;
using AvatarVcs.Core.Naming;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.MaterialSettings
{
    /// <summary>
    /// Which material a generated duplicate was made from.
    ///
    /// This exists because capture reads whatever material the renderer is
    /// pointing at -- and after a checkout that is the duplicate, not the
    /// user's material. Recording the duplicate as the source made the next
    /// checkout duplicate the duplicate, and a real avatar reached 552
    /// generated materials with names six levels deep
    /// ("SmartPhone_avatarvcs 3_avatarvcs_avatarvcs_avatarvcs 2_avatarvcs").
    ///
    /// The link is written into the duplicate's own .meta (AssetImporter
    /// userData), which survives moves, renames and re-imports. Duplicates
    /// generated before that existed have no link, so the name is used as a
    /// fallback -- GeneratedAssetNaming.StripSuffixes gets back to the
    /// original name, which for the sibling-placement case is enough.
    /// </summary>
    public static class GeneratedMaterialSource
    {
        private const string UserDataPrefix = "avatarvcs.source=";

        /// <summary>
        /// Remembers, on the generated asset itself, which material it was
        /// duplicated from. Left alone if the importer refuses -- a missing
        /// link degrades to the name-based fallback, it doesn't break anything.
        /// </summary>
        public static void Record(string generatedAssetPath, string sourceGuid)
        {
            if (string.IsNullOrEmpty(generatedAssetPath) || !CommitIdentifier.IsValidShape(sourceGuid)) return;

            var importer = AssetImporter.GetAtPath(generatedAssetPath);
            if (importer == null) return;

            // Somebody else's userData is not ours to throw away. In practice
            // a material importer's is empty, but appending keeps the
            // promise either way.
            var existing = importer.userData ?? string.Empty;
            if (existing.Contains(UserDataPrefix)) return;

            importer.userData = existing.Length == 0
                ? UserDataPrefix + sourceGuid
                : existing + "\n" + UserDataPrefix + sourceGuid;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// The guid of the material this one was generated from, or null when
        /// it wasn't generated (or nothing can be traced).
        /// </summary>
        public static string ResolveSourceGuid(string generatedAssetPath)
        {
            if (!GeneratedAssetNaming.LooksGenerated(generatedAssetPath)) return null;

            var recorded = RecordedSourceGuid(generatedAssetPath);
            if (recorded != null) return recorded;

            // Fallback for duplicates made before the link was written: the
            // original sat next to it under the un-suffixed name.
            var directory = Path.GetDirectoryName(generatedAssetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory)) return null;

            var original = GeneratedAssetNaming.StripSuffixes(Path.GetFileNameWithoutExtension(generatedAssetPath));
            var candidate = $"{directory}/{original}{GeneratedAssetNaming.MaterialExtension}";
            if (candidate == generatedAssetPath || !File.Exists(candidate)) return null;

            var guid = AssetDatabase.AssetPathToGUID(candidate);
            return string.IsNullOrEmpty(guid) ? null : guid;
        }

        /// <summary>
        /// material itself when it isn't one of ours, otherwise the material
        /// it was generated from. Never null for a non-null argument: an
        /// untraceable duplicate is better recorded as itself than dropped.
        /// </summary>
        public static Material Resolve(Material material)
        {
            if (material == null) return null;

            var path = AssetDatabase.GetAssetPath(material);
            var sourceGuid = ResolveSourceGuid(path);
            if (sourceGuid == null) return material;

            var sourcePath = AssetDatabase.GUIDToAssetPath(sourceGuid);
            if (string.IsNullOrEmpty(sourcePath)) return material;

            return AssetDatabase.LoadAssetAtPath<Material>(sourcePath) ?? material;
        }

        private static string RecordedSourceGuid(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath);
            var userData = importer == null ? null : importer.userData;
            if (string.IsNullOrEmpty(userData)) return null;

            foreach (var line in userData.Split('\n'))
            {
                if (!line.StartsWith(UserDataPrefix, System.StringComparison.Ordinal)) continue;

                var guid = line.Substring(UserDataPrefix.Length).Trim();
                return CommitIdentifier.IsValidShape(guid) ? guid : null;
            }

            return null;
        }
    }
}
