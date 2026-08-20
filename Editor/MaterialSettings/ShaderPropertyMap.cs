using System;
using System.Collections.Generic;

namespace AvatarVcs.Editor.MaterialSettings
{
    /// <summary>
    /// Maps a shader name to the material properties AvatarVCS tracks for it.
    /// MVP only supports "lilToon" (design doc 1.4.3); other shaders are
    /// intentionally left unmapped so callers detect and warn on them, rather
    /// than silently doing nothing.
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
        };

        public static bool IsSupported(string shaderName) => Map.ContainsKey(shaderName);

        public static IReadOnlyList<(string name, string type)> GetProperties(string shaderName) =>
            Map.TryGetValue(shaderName, out var entries) ? entries : Array.Empty<(string, string)>();
    }
}
