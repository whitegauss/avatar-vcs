using System;
using System.Collections.Generic;
using System.IO;
using AvatarVcs.Core.Model;
using UnityEngine;

namespace AvatarVcs.Core.Presets
{
    /// <summary>
    /// JSON (de)serialization and result-message formatting for standalone
    /// material settings presets, mirroring BlendShapePresetJson: file I/O
    /// stays in the menu, MaterialSettingsPresetIO stays the capture/apply
    /// half, and this is the part that can be unit tested without Unity.
    /// </summary>
    public static class MaterialSettingsPresetJson
    {
        public static string Serialize(MaterialSettingsPreset preset) => JsonUtility.ToJson(preset, true);

        /// <summary>
        /// Never throws, for the same reason BlendShapePresetJson doesn't: the
        /// input is a file someone was sent, so malformed content is an
        /// expected case to report, not an exception to propagate. On success
        /// preset can still be null -- JsonUtility returns null for "null" or
        /// an unrelated JSON object rather than failing -- so callers check
        /// that separately.
        /// </summary>
        public static bool TryParse(string json, out MaterialSettingsPreset preset, out string error)
        {
            try
            {
                preset = JsonUtility.FromJson<MaterialSettingsPreset>(json);
                error = null;
                return true;
            }
            catch (Exception e) when (e is IOException or ArgumentException)
            {
                preset = null;
                error = e.Message;
                return false;
            }
        }

        public static string DescribeExport(int count, string shader, string path) =>
            $"[AvatarVCS] Exported {count} setting(s) from '{shader}' to '{path}'.";

        /// <summary>
        /// Names the shader mismatch explicitly when there is one: importing
        /// lilToon values onto a Standard material is the case where a
        /// half-applied result would otherwise be baffling.
        ///
        /// The skip list does not say why, because Apply skips for three
        /// different reasons and only one of them is "not on this material".
        /// A texture the recipient doesn't own is skipped with the property
        /// sitting right there on the material -- and that is the headline
        /// case for a shared preset, not a corner one.
        /// </summary>
        public static string DescribeImport(
            int appliedCount, string path, string presetShader, string targetShader, IReadOnlyList<string> skipped)
        {
            var message = $"[AvatarVCS] Imported {appliedCount} setting(s) from '{path}'.";

            if (!string.IsNullOrEmpty(presetShader) && presetShader != targetShader)
                message += $" The preset was exported from '{presetShader}' but this material uses '{targetShader}';"
                    + " only properties present on both were applied.";

            if (skipped.Count > 0)
                message += $" {skipped.Count} skipped (not on this material, or its value or texture"
                    + $" couldn't be used): {string.Join(", ", skipped)}";

            return message;
        }
    }
}
