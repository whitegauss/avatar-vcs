using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AvatarVcs.Core.MaterialSettings
{
    /// <summary>
    /// Decides which shaders AvatarVCS is willing to duplicate-and-modify
    /// (design doc 1.4.3), and enumerates the Color/Float properties a given
    /// one of those shaders actually declares.
    ///
    /// GetProperties used to return a hand-curated per-shader property list;
    /// that inevitably missed properties a user actually wanted to change
    /// (see issue #44: a material edit could silently fail to be captured/
    /// restored just because its property wasn't on the list). Reading the
    /// shader's own declared properties instead covers everything the
    /// shader exposes, including ones added by a future shader version,
    /// with no list to keep in sync.
    /// </summary>
    public static class ShaderPropertyMap
    {
        // Still an explicit allowlist, not "every shader": duplicating a
        // material is only appropriate for shaders this tool has decided
        // are safe/sensible to do that for (design doc 1.4.3's MVP scope).
        private static readonly HashSet<string> SupportedShaders = new()
        {
            "lilToon",
            // Poiyomi's registered shader name is dot-prefixed (hides it
            // from the shader dropdown search); this is the actual
            // material.shader.name.
            ".poiyomi/Poiyomi",
            // UniVRM's legacy (VRM 0.x) MToon shader.
            "VRM/MToon",
            // UniVRM's MToon 1.0 shader (VRM 1.0 / VRMC_materials_mtoon).
            "VRM10/MToon10",
        };

        public static bool IsSupported(string shaderName) => SupportedShaders.Contains(shaderName);

        /// <summary>
        /// Every Color/Float/Range property shader declares. Texture
        /// properties are deliberately excluded -- they're asset references
        /// needing GUID handling, not a simple value type, and out of scope
        /// here (MaterialPropertyValue only carries "color"/"float").
        /// </summary>
        public static IReadOnlyList<(string name, string type)> GetProperties(Shader shader)
        {
            if (shader == null) return Array.Empty<(string, string)>();

            var count = shader.GetPropertyCount();
            var result = new List<(string, string)>(count);
            for (var i = 0; i < count; i++)
            {
                var type = shader.GetPropertyType(i) switch
                {
                    ShaderPropertyType.Color => "color",
                    ShaderPropertyType.Float => "float",
                    ShaderPropertyType.Range => "float",
                    _ => null,
                };
                if (type == null) continue;

                result.Add((shader.GetPropertyName(i), type));
            }

            return result;
        }
    }
}
