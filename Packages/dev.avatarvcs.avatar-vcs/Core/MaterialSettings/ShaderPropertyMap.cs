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
        //
        // Matched by FAMILY, not by exact name. These shaders ship one
        // variant per rendering mode and each variant is a separate registered
        // name, so an exact-name list only ever covered the opaque default:
        // lilToon alone registers 64 names, of which "lilToon" is one, and a
        // real avatar's materials are almost always variants
        // ("Hidden/lilToonOutline", "Hidden/lilToonTransparent", ...). Every
        // variant is the same shader with the same property set, so there is
        // no reason to treat them differently -- and because an unsupported
        // shader is skipped silently, the old list made whole avatars record
        // nothing at all with no visible sign.
        private static readonly string[] SupportedFamilies =
        {
            // lilToon: "lilToon", "Hidden/lilToon*", "_lil/lilToonMulti",
            // "_lil/[Optional] lilToon*". Note this correctly excludes
            // lilToon's internal pass shaders ("Hidden/ltspass_opaque",
            // "Hidden/ltsother_baker"), which no material should reference.
            "lilToon",
            // Poiyomi: ".poiyomi/Poiyomi Toon", "Poiyomi/Poiyomi Pro", and
            // the locked-in form "Hidden/Locked/Poiyomi Toon/<hash>".
            "Poiyomi",
            // UniVRM: "VRM/MToon" (VRM 0.x) and "VRM10/MToon10" (VRM 1.0).
            "MToon",
        };

        // lilToon prefixes its optional shaders with this inside the "_lil"
        // folder, e.g. "_lil/[Optional] lilToonOverlay".
        private const string OptionalPrefix = "[Optional]";

        /// <summary>
        /// True when shaderName belongs to a supported shader family. A
        /// shader's registered name is a "/"-separated path, and the family
        /// name can sit in any segment: the leading segments are folders
        /// ("Hidden", ".poiyomi", "Hidden/Locked") that say where the shader
        /// appears in the dropdown, not what it is.
        /// </summary>
        public static bool IsSupported(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName)) return false;

            foreach (var rawSegment in shaderName.Split('/'))
            {
                var segment = rawSegment.Trim();
                if (segment.StartsWith(OptionalPrefix, StringComparison.Ordinal))
                    segment = segment.Substring(OptionalPrefix.Length).TrimStart();

                foreach (var family in SupportedFamilies)
                {
                    // StartsWith, not equality: the variant suffix is part of
                    // the same segment ("lilToonTransparentOutline").
                    if (segment.StartsWith(family, StringComparison.Ordinal)) return true;
                }
            }

            return false;
        }

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
