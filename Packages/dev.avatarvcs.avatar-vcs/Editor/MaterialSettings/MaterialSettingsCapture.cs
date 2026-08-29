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
                    _ => null,
                };
                if (value == null) continue;

                state.properties.Add(new MaterialPropertyValue { name = name, type = type, value = value });
            }

            return state;
        }

        private static string ColorToString(Color c) =>
            string.Join(",", new[] { c.r, c.g, c.b, c.a }.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
    }
}
