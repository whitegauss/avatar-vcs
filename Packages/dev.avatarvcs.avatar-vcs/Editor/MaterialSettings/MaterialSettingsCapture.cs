using System;
using System.Globalization;
using System.Linq;
using AvatarVcs.Core.MaterialSettings;
using AvatarVcs.Core.Model;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.MaterialSettings
{
    /// <summary>
    /// Reads current shader property values off a material for the properties
    /// ShaderPropertyMap tracks for its shader. Read-only, never mutates material.
    /// shaderName is taken as an explicit parameter (normally material.shader.name)
    /// rather than inferred, so callers control which map entry is used.
    /// </summary>
    public static class MaterialSettingsCapture
    {
        public static MaterialSettingsState Capture(Material material, string shaderName, string targetPath, int slot)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (string.IsNullOrEmpty(shaderName)) throw new ArgumentException("shaderName must not be empty.", nameof(shaderName));
            if (!ShaderPropertyMap.IsSupported(shaderName))
                throw new NotSupportedException($"Shader '{shaderName}' is not supported (see ShaderPropertyMap).");

            var assetPath = AssetDatabase.GetAssetPath(material);
            var guid = string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
                throw new InvalidOperationException("material must be a saved asset to be captured.");

            var state = new MaterialSettingsState
            {
                targetPath = targetPath,
                slot = slot,
                sourceMaterialGuid = guid,
                shader = shaderName,
            };

            foreach (var (name, type) in ShaderPropertyMap.GetProperties(material.shader))
            {
                var value = type switch
                {
                    "color" => ColorToString(material.GetColor(name)),
                    "float" => material.GetFloat(name).ToString("R", CultureInfo.InvariantCulture),
                    // Empty means "no texture assigned", which is a real,
                    // restorable state -- the shader falls back to its own
                    // default. Recording it (rather than skipping the
                    // property) is what lets a checkout clear a texture the
                    // source material has since gained.
                    "texture" => TextureGuid(material, name),
                    _ => null,
                };
                if (value == null) continue;

                state.properties.Add(new MaterialPropertyValue { name = name, type = type, value = value });

                // Tiling/offset only matters where a texture is actually
                // assigned, and it is part of how that texture looks, so it
                // rides along rather than being a separate opt-in.
                if (type == "texture" && value.Length > 0)
                {
                    var scale = material.GetTextureScale(name);
                    var offset = material.GetTextureOffset(name);
                    state.properties.Add(new MaterialPropertyValue
                    {
                        name = name,
                        type = ShaderPropertyMap.TextureScaleOffsetType,
                        value = string.Join(",", new[] { scale.x, scale.y, offset.x, offset.y }
                            .Select(v => v.ToString("R", CultureInfo.InvariantCulture))),
                    });
                }
            }

            return state;
        }

        /// <summary>
        /// The GUID of the texture in this slot, or "" when nothing is
        /// assigned. A runtime-only texture (not a saved asset) also reads as
        /// "" -- there is no guid to restore it from, and pretending
        /// otherwise would silently drop it on checkout.
        /// </summary>
        private static string TextureGuid(Material material, string name)
        {
            var texture = material.GetTexture(name);
            if (texture == null) return string.Empty;

            var path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path)) return string.Empty;

            return AssetDatabase.AssetPathToGUID(path) ?? string.Empty;
        }

        private static string ColorToString(Color c) =>
            string.Join(",", new[] { c.r, c.g, c.b, c.a }.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
    }
}
