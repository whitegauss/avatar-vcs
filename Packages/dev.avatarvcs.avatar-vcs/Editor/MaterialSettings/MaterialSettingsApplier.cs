using System;
using System.Collections.Generic;
using System.Globalization;
using AvatarVcs.Core.MaterialSettings;
using AvatarVcs.Editor.History;
using AvatarVcs.Core.Model;
using AvatarVcs.Editor.Reflection;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.MaterialSettings
{
    /// <summary>
    /// Duplicates the source material (never mutated), applies the recorded
    /// properties to the duplicate, saves it alongside the source, and points
    /// the renderer's slot at the duplicate. Design doc 1.4.3.
    ///
    /// Apply mutates state.generatedGuid in place when it generates or
    /// reuses a duplicate, but does not persist that back to storage itself
    /// -- callers that want the reuse to survive a domain reload or a later
    /// session (i.e. anyone driving a real checkout, not just probing this
    /// method directly) must save the owning commit afterward.
    /// CheckoutOperation.Checkout already does this.
    /// </summary>
    public static class MaterialSettingsApplier
    {
        public static Material Apply(MaterialSettingsState state, GameObject avatarRoot)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (avatarRoot == null) throw new ArgumentNullException(nameof(avatarRoot));

            if (!ShaderPropertyMap.IsSupported(state.shader))
                throw new NotSupportedException($"Shader '{state.shader}' is not supported (see ShaderPropertyMap).");

            var target = ReferenceResolver.ResolvePath(state.targetPath, avatarRoot.transform);
            if (target == null)
                throw new InvalidOperationException($"Path '{state.targetPath}' could not be resolved.");

            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
                throw new InvalidOperationException($"'{state.targetPath}' has no Renderer.");

            // Validate the slot before generating anything: failing here
            // after CreateAsset would leak an orphaned, untracked duplicate
            // (state.generatedGuid never gets saved onto a returned commit,
            // so GC would never find it).
            if (state.slot < 0 || state.slot >= renderer.sharedMaterials.Length)
                throw new InvalidOperationException($"Material slot {state.slot} out of range on '{state.targetPath}'.");

            // GuidRemapper (design doc 6.4): a re-imported source material's
            // new GUID is transparently substituted.
            var sourcePath = AssetDatabase.GUIDToAssetPath(GuidRemapper.Resolve(state.sourceMaterialGuid));
            if (string.IsNullOrEmpty(sourcePath))
                throw new InvalidOperationException($"Source material GUID '{state.sourceMaterialGuid}' could not be resolved.");

            var sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            if (sourceMaterial == null)
                throw new InvalidOperationException($"Asset at '{sourcePath}' is not a Material.");

            // Reuse a previously-generated duplicate for this exact state if
            // it's still there, instead of creating another one on every
            // checkout of the same commit. Still re-applies state.properties
            // onto it every time: a checkout is supposed to be a regenerate,
            // not a one-time stamp -- if the duplicate was hand-edited (or
            // came from a since-replaced commit that reused this guid), the
            // recorded values must win, same as containers always destroying
            // and rebuilding rather than trusting whatever's already there.
            if (!string.IsNullOrEmpty(state.generatedGuid))
            {
                var existingPath = AssetDatabase.GUIDToAssetPath(state.generatedGuid);
                var existing = string.IsNullOrEmpty(existingPath) ? null : AssetDatabase.LoadAssetAtPath<Material>(existingPath);
                if (existing != null)
                {
                    ApplyProperties(existing, state.properties);
                    AssetDatabase.SaveAssets();
                    PointRendererAt(renderer, state.slot, state.targetPath, existing);
                    return existing;
                }
            }

            // Copy-constructing reads sourceMaterial but never writes to it.
            var duplicate = new Material(sourceMaterial) { name = sourceMaterial.name + "_avatarvcs" };
            ApplyProperties(duplicate, state.properties);

            var directory = System.IO.Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory) || directory.StartsWith("Packages/") || directory == "Packages")
            {
                // A source material inside an immutable/read-only UPM
                // package (Packages/...) can't have a sibling asset written
                // next to it -- AssetDatabase.CreateAsset would fail there.
                directory = "Assets/AvatarVCS_Generated";
                if (!AssetDatabase.IsValidFolder(directory))
                    AssetDatabase.CreateFolder("Assets", "AvatarVCS_Generated");
            }
            var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{directory}/{duplicate.name}.mat");
            AssetDatabase.CreateAsset(duplicate, assetPath);
            AssetDatabase.SaveAssets();
            // CreateAsset can trigger a reimport that leaves the pre-save
            // reference stale; reload so callers and the renderer get the
            // same canonical instance that later AssetDatabase lookups see.
            duplicate = AssetDatabase.LoadAssetAtPath<Material>(assetPath);

            state.generatedGuid = AssetDatabase.AssetPathToGUID(assetPath);
            PointRendererAt(renderer, state.slot, state.targetPath, duplicate);

            return duplicate;
        }

        private static void ApplyProperties(Material material, List<MaterialPropertyValue> properties)
        {
            foreach (var property in properties)
            {
                if (!material.HasProperty(property.name))
                {
                    Debug.LogWarning($"[AvatarVCS] Duplicate material has no property '{property.name}'; skipped.");
                    continue;
                }

                // property.value ultimately comes from commit JSON on disk,
                // which can be malformed independent of any deliberate
                // tampering (crash mid-write, bad merge); a parse failure on
                // one property must not abort applying the rest of this
                // material, let alone whatever destructive checkout is
                // already underway around this call.
                try
                {
                    switch (property.type)
                    {
                        case "color":
                            material.SetColor(property.name, ParseColor(property.value));
                            break;
                        case "float":
                            material.SetFloat(property.name, float.Parse(property.value, CultureInfo.InvariantCulture));
                            break;
                        default:
                            Debug.LogWarning($"[AvatarVCS] Unsupported material property type '{property.type}' for '{property.name}' was skipped.");
                            break;
                    }
                }
                catch (Exception e) when (e is FormatException or OverflowException or IndexOutOfRangeException or ArgumentNullException)
                {
                    Debug.LogWarning($"[AvatarVCS] Could not parse material property '{property.name}' (type '{property.type}', value '{property.value}'): {e.Message}; skipped.");
                }
            }
        }

        private static void PointRendererAt(Renderer renderer, int slot, string targetPath, Material material)
        {
            var materials = renderer.sharedMaterials;
            if (slot < 0 || slot >= materials.Length)
                throw new InvalidOperationException($"Material slot {slot} out of range on '{targetPath}'.");

            materials[slot] = material;
            Undo.RecordObject(renderer, "AvatarVCS Apply Material Settings");
            renderer.sharedMaterials = materials;
        }

        private static Color ParseColor(string value)
        {
            var parts = Array.ConvertAll(value.Split(','), s => float.Parse(s, CultureInfo.InvariantCulture));
            return new Color(parts[0], parts[1], parts[2], parts[3]);
        }
    }
}
