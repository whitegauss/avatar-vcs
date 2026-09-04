using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AvatarVcs.Core.MaterialSettings;
using AvatarVcs.Core.Model;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.MaterialSettings
{
    /// <summary>
    /// Standalone export/import of one material's shader settings, the
    /// material-side counterpart to BlendShapePresetIO.
    ///
    /// The use case is "tell a friend your settings". Today that means
    /// sending them the .mat -- which redistributes the asset it came from --
    /// or a screenshot of the Inspector. A values-only file is both safer and
    /// actually usable.
    /// </summary>
    public static class MaterialSettingsPresetIO
    {
        /// <summary>
        /// Only the properties that differ from the shader's own defaults.
        ///
        /// The commit format records every declared property, which for
        /// lilToon is ~491 per material -- correct there, because a commit
        /// has to restore an exact state. A preset is the opposite: it is
        /// read by a person deciding whether to apply it, and 491 lines of
        /// mostly-defaults hides the handful that were actually set. Anything
        /// left out is, by definition, already what the recipient's material
        /// would do untouched.
        /// </summary>
        public static MaterialSettingsPreset Capture(Material material)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (material.shader == null) throw new ArgumentException("material has no shader.", nameof(material));

            var shader = material.shader;
            var preset = new MaterialSettingsPreset { shader = shader.name };

            foreach (var (name, type) in ShaderPropertyMap.GetProperties(shader))
            {
                var index = shader.FindPropertyIndex(name);
                if (index < 0) continue;

                var value = ReadIfChangedFromDefault(material, shader, index, name, type);
                if (value != null) preset.properties.Add(value);
            }

            return preset;
        }

        private static MaterialPropertyValue ReadIfChangedFromDefault(
            Material material, Shader shader, int index, string name, string type)
        {
            switch (type)
            {
                case "color":
                {
                    var current = material.GetColor(name);
                    var def = shader.GetPropertyDefaultVectorValue(index);
                    if (Approximately(current, new Color(def.x, def.y, def.z, def.w))) return null;
                    return new MaterialPropertyValue { name = name, type = type, value = ColorToString(current) };
                }
                case "float":
                {
                    var current = material.GetFloat(name);
                    if (Mathf.Approximately(current, shader.GetPropertyDefaultFloatValue(index))) return null;
                    return new MaterialPropertyValue
                    {
                        name = name,
                        type = type,
                        value = current.ToString("R", CultureInfo.InvariantCulture),
                    };
                }
                case "texture":
                {
                    // A shader's default texture is a built-in ("white",
                    // "bump", ...) with no GUID, so "assigned" is the same
                    // question as "differs from default" here.
                    var texture = material.GetTexture(name);
                    if (texture == null) return null;

                    var path = AssetDatabase.GetAssetPath(texture);
                    if (string.IsNullOrEmpty(path)) return null;

                    var guid = AssetDatabase.AssetPathToGUID(path);
                    if (string.IsNullOrEmpty(guid)) return null;

                    return new MaterialPropertyValue { name = name, type = type, value = guid };
                }
                default:
                    return null;
            }
        }

        /// <summary>
        /// Writes the preset onto material, skipping any property the target
        /// shader doesn't declare -- importing onto a similar-but-different
        /// material is the point, so a missing property is a skip, not an
        /// error. Returns the names that couldn't be applied.
        ///
        /// Mutates the material it is given, under Undo.
        ///
        /// Unlike checkout -- which duplicates rather than touch a source
        /// asset, because it runs automatically and the user didn't pick the
        /// material -- an import is an explicit action on an asset the user
        /// selected. Editing it is what they asked for. Duplicating instead
        /// would leave them with a material their renderers don't use.
        /// </summary>
        public static List<string> Apply(MaterialSettingsPreset preset, Material material)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            if (material == null) throw new ArgumentNullException(nameof(material));

            var skipped = new List<string>();
            Undo.RecordObject(material, "Import Material Settings");

            foreach (var property in preset.properties ?? new List<MaterialPropertyValue>())
            {
                if (property == null || string.IsNullOrEmpty(property.name))
                {
                    skipped.Add("(missing name)");
                    continue;
                }

                if (!material.HasProperty(property.name))
                {
                    skipped.Add(property.name);
                    continue;
                }

                if (!TryWrite(material, property)) skipped.Add(property.name);
            }

            EditorUtility.SetDirty(material);
            return skipped;
        }

        private static bool TryWrite(Material material, MaterialPropertyValue property)
        {
            try
            {
                switch (property.type)
                {
                    case "color":
                        material.SetColor(property.name, ParseColor(property.value));
                        return true;
                    case "float":
                        material.SetFloat(property.name, float.Parse(property.value, CultureInfo.InvariantCulture));
                        return true;
                    case "texture":
                    {
                        var path = AssetDatabase.GUIDToAssetPath(property.value);
                        var texture = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture>(path);
                        // The recipient not having the texture is the normal
                        // case for a shared preset, not a corrupt file. Leave
                        // the slot as it is and report it.
                        if (texture == null) return false;

                        material.SetTexture(property.name, texture);
                        return true;
                    }
                    default:
                        return false;
                }
            }
            // Values come from a file someone was sent; a bad one skips its
            // property rather than aborting the rest of the import.
            catch (Exception e) when (e is FormatException or OverflowException
                or IndexOutOfRangeException or ArgumentNullException or NullReferenceException)
            {
                return false;
            }
        }

        private static bool Approximately(Color a, Color b) =>
            Mathf.Approximately(a.r, b.r) && Mathf.Approximately(a.g, b.g)
            && Mathf.Approximately(a.b, b.b) && Mathf.Approximately(a.a, b.a);

        private static string ColorToString(Color c) =>
            string.Join(",", new[] { c.r, c.g, c.b, c.a }.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));

        private static Color ParseColor(string value)
        {
            var parts = value.Split(',');
            if (parts.Length != 4) throw new FormatException($"expected 4 components, got {parts.Length}");
            return new Color(
                float.Parse(parts[0], CultureInfo.InvariantCulture),
                float.Parse(parts[1], CultureInfo.InvariantCulture),
                float.Parse(parts[2], CultureInfo.InvariantCulture),
                float.Parse(parts[3], CultureInfo.InvariantCulture));
        }
    }
}
