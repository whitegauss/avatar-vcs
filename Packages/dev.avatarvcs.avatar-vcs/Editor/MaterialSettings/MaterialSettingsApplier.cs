using System;
using System.Globalization;
using AvatarVcs.Editor.Model;
using AvatarVcs.Editor.Reflection;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.MaterialSettings
{
    /// <summary>
    /// Duplicates the source material (never mutated), applies the recorded
    /// properties to the duplicate, saves it alongside the source, and points
    /// the renderer's slot at the duplicate. Design doc 1.4.3.
    /// </summary>
    public static class MaterialSettingsApplier
    {
        public static Material Apply(MaterialSettingsState state, GameObject avatarRoot)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (avatarRoot == null) throw new ArgumentNullException(nameof(avatarRoot));

            if (!ShaderPropertyMap.IsSupported(state.shader))
                throw new NotSupportedException($"Shader '{state.shader}' is not supported (MVP: lilToon only).");

            var target = ReferenceResolver.ResolvePath(state.targetPath, avatarRoot.transform);
            if (target == null)
                throw new InvalidOperationException($"Path '{state.targetPath}' could not be resolved.");

            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
                throw new InvalidOperationException($"'{state.targetPath}' has no Renderer.");

            var sourcePath = AssetDatabase.GUIDToAssetPath(state.sourceMaterialGuid);
            if (string.IsNullOrEmpty(sourcePath))
                throw new InvalidOperationException($"Source material GUID '{state.sourceMaterialGuid}' could not be resolved.");

            var sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            if (sourceMaterial == null)
                throw new InvalidOperationException($"Asset at '{sourcePath}' is not a Material.");

            // Copy-constructing reads sourceMaterial but never writes to it.
            var duplicate = new Material(sourceMaterial) { name = sourceMaterial.name + "_avatarvcs" };

            foreach (var property in state.properties)
            {
                if (!duplicate.HasProperty(property.name))
                {
                    Debug.LogWarning($"[AvatarVCS] Duplicate material has no property '{property.name}'; skipped.");
                    continue;
                }

                switch (property.type)
                {
                    case "color":
                        duplicate.SetColor(property.name, ParseColor(property.value));
                        break;
                    case "float":
                        duplicate.SetFloat(property.name, float.Parse(property.value, CultureInfo.InvariantCulture));
                        break;
                    default:
                        Debug.LogWarning($"[AvatarVCS] Unsupported material property type '{property.type}' for '{property.name}' was skipped.");
                        break;
                }
            }

            var directory = System.IO.Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
            var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{directory}/{duplicate.name}.mat");
            AssetDatabase.CreateAsset(duplicate, assetPath);

            var materials = renderer.sharedMaterials;
            if (state.slot < 0 || state.slot >= materials.Length)
                throw new InvalidOperationException($"Material slot {state.slot} out of range on '{state.targetPath}'.");

            materials[state.slot] = duplicate;
            Undo.RecordObject(renderer, "AvatarVCS Apply Material Settings");
            renderer.sharedMaterials = materials;

            return duplicate;
        }

        private static Color ParseColor(string value)
        {
            var parts = Array.ConvertAll(value.Split(','), s => float.Parse(s, CultureInfo.InvariantCulture));
            return new Color(parts[0], parts[1], parts[2], parts[3]);
        }
    }
}
