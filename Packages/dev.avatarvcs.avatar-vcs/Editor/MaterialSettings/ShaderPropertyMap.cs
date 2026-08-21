using System;
using System.Collections.Generic;

namespace AvatarVcs.Editor.MaterialSettings
{
    /// <summary>
    /// Maps a shader name (material.shader.name, not the shader's display
    /// path) to the material properties AvatarVCS tracks for it. Covers the
    /// most common VRChat avatar shaders (design doc 1.4.3); a shader not
    /// listed here is intentionally left unmapped so callers detect and warn
    /// on it, rather than silently doing nothing. Property lists are
    /// best-effort common properties, not exhaustive -- MaterialSettingsCapture/
    /// Applier already skip a mapped property a given material doesn't
    /// declare (material.HasProperty), so an imprecise entry degrades to
    /// "captures fewer properties than ideal" rather than breaking anything.
    /// </summary>
    public static class ShaderPropertyMap
    {
        private static readonly Dictionary<string, (string name, string type)[]> Map = new()
        {
            ["lilToon"] = new[]
            {
                ("_Color", "color"),
                ("_OutlineColor", "color"),
                ("_OutlineWidth", "float"),
                ("_EmissionColor", "color"),
            },
            // Poiyomi's registered shader name is dot-prefixed (hides it from
            // the shader dropdown search); this is the actual material.shader.name.
            [".poiyomi/Poiyomi"] = new[]
            {
                ("_Color", "color"),
                ("_EmissionColor", "color"),
                ("_Cutoff", "float"),
            },
            // UniVRM's legacy (VRM 0.x) MToon shader.
            ["VRM/MToon"] = new[]
            {
                ("_Color", "color"),
                ("_ShadeColor", "color"),
                ("_EmissionColor", "color"),
                ("_OutlineColor", "color"),
                ("_OutlineWidth", "float"),
            },
            // UniVRM's MToon 1.0 shader (VRM 1.0 / VRMC_materials_mtoon),
            // property names follow glTF-style *Factor naming.
            ["VRM10/MToon10"] = new[]
            {
                ("_BaseColorFactor", "color"),
                ("_ShadeColorFactor", "color"),
                ("_EmissiveFactor", "color"),
                ("_OutlineColorFactor", "color"),
                ("_OutlineWidthFactor", "float"),
            },
        };

        public static bool IsSupported(string shaderName) => Map.ContainsKey(shaderName);

        public static IReadOnlyList<(string name, string type)> GetProperties(string shaderName) =>
            Map.TryGetValue(shaderName, out var entries) ? entries : Array.Empty<(string, string)>();
    }
}
