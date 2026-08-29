using System;
using System.Collections.Generic;
using System.IO;
using AvatarVcs.Core.Model;
using UnityEngine;

namespace AvatarVcs.Core.Presets
{
    /// <summary>
    /// JSON (de)serialization and result-message formatting for standalone
    /// BlendShape presets (issue #58), split out of AvatarVcsMenu's
    /// Export/Import commands. File I/O (SaveFilePanel/OpenFilePanel/
    /// File.ReadAllText, Debug.Log) stays in the menu; BlendShapePresetIO
    /// remains the pure capture/apply half.
    /// </summary>
    public static class BlendShapePresetJson
    {
        public static string Serialize(BlendShapePreset preset) => JsonUtility.ToJson(preset, true);

        /// <summary>
        /// Never throws: a malformed json (truncated, not actually JSON) is
        /// reported through error/false rather than propagating an
        /// exception, mirroring the FormatException/OverflowException/...
        /// catch pattern used elsewhere for values that ultimately come from
        /// a file on disk. On success, preset can still be null -- JsonUtility
        /// doesn't fail parsing e.g. "null" or an unrelated JSON object, it
        /// just returns null -- callers must check for that separately.
        /// </summary>
        public static bool TryParse(string json, out BlendShapePreset preset, out string error)
        {
            try
            {
                preset = JsonUtility.FromJson<BlendShapePreset>(json);
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

        public static string DescribeExport(int count, string path) =>
            $"[AvatarVCS] Exported {count} BlendShape(s) to '{path}'.";

        public static string DescribeImport(int appliedCount, string path, IReadOnlyList<string> skipped) =>
            $"[AvatarVCS] Imported {appliedCount} BlendShape(s) from '{path}'."
                + (skipped.Count > 0 ? $" {skipped.Count} not found on this mesh, skipped: {string.Join(", ", skipped)}" : "");
    }
}
