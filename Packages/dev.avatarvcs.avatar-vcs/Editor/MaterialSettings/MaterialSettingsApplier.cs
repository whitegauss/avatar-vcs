using System;
using System.Collections.Generic;
using System.Globalization;
using AvatarVcs.Core.Diagnostics;
using AvatarVcs.Core.MaterialSettings;
using AvatarVcs.Core.Naming;
using AvatarVcs.Editor.Diagnostics;
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
        // AssetDatabase.SaveAssets flushes the whole database, and this used
        // to run once per material slot. That was invisible while the shader
        // allowlist matched almost nothing; once lilToon's variants started
        // matching (KAN-84) a real avatar reached 46 slots, so one checkout
        // meant 46 full flushes. A caller applying many slots opens a batch
        // and pays for one.
        private static int saveBatchDepth;

        /// <summary>
        /// Defers the AssetDatabase flush until the outermost scope closes.
        /// Nestable, so an inner caller (ContainerRestore, inside a checkout)
        /// doesn't need to know whether an outer one already opened it.
        /// </summary>
        public static SaveBatchScope BeginSaveBatch() => new SaveBatchScope(++saveBatchDepth);

        public readonly struct SaveBatchScope : IDisposable
        {
            private readonly int depth;
            internal SaveBatchScope(int depth) => this.depth = depth;

            public void Dispose()
            {
                saveBatchDepth--;
                if (depth == 1) AssetDatabase.SaveAssets();
            }
        }

        private static void SaveUnlessBatched()
        {
            if (saveBatchDepth == 0) AssetDatabase.SaveAssets();
        }

        public static Material Apply(MaterialSettingsState state, GameObject avatarRoot, DiagnosticLog log = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (avatarRoot == null) throw new ArgumentNullException(nameof(avatarRoot));

            // KAN-20: per-property "couldn't apply that one" warnings collect
            // into a DiagnosticLog. A caller mid-checkout passes its own; a
            // direct caller (tests) passes none, so make one and flush it --
            // even on the throw path, so warnings logged before the throw
            // still reach the console.
            var ownsLog = log == null;
            log ??= new DiagnosticLog();
            try
            {
                return ApplyCore(state, avatarRoot, log);
            }
            finally
            {
                if (ownsLog) UnityDiagnosticSink.Flush(log);
            }
        }

        private static Material ApplyCore(MaterialSettingsState state, GameObject avatarRoot, DiagnosticLog log)
        {
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
                    // Re-checking out the same commit normally finds the
                    // duplicate already holding these values. Writing them
                    // back anyway would dirty the asset and make the flush
                    // real work every time, so only touch it when a value
                    // actually differs.
                    if (ApplyProperties(existing, state.properties, log))
                    {
                        EditorUtility.SetDirty(existing);
                        SaveUnlessBatched();
                    }

                    PointRendererAt(renderer, state.slot, state.targetPath, existing);
                    return existing;
                }
            }

            // Copy-constructing reads sourceMaterial but never writes to it.
            // Name/placement come from GeneratedAssetNaming so the deletion
            // guard in CommitStore recognises exactly what we emit (KAN-76).
            var duplicate = new Material(sourceMaterial)
            {
                name = GeneratedAssetNaming.DuplicateName(sourceMaterial.name),
            };
            ApplyProperties(duplicate, state.properties, log);

            var directory = System.IO.Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory) || directory.StartsWith("Packages/") || directory == "Packages")
            {
                // A source material inside an immutable/read-only UPM
                // package (Packages/...) can't have a sibling asset written
                // next to it -- AssetDatabase.CreateAsset would fail there.
                directory = GeneratedAssetNaming.GeneratedFolder;
                if (!AssetDatabase.IsValidFolder(directory))
                    AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(GeneratedAssetNaming.GeneratedFolder));
            }
            var assetPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{directory}/{duplicate.name}{GeneratedAssetNaming.MaterialExtension}");
            AssetDatabase.CreateAsset(duplicate, assetPath);
            SaveUnlessBatched();
            // CreateAsset can trigger a reimport that leaves the pre-save
            // reference stale; reload so callers and the renderer get the
            // same canonical instance that later AssetDatabase lookups see.
            duplicate = AssetDatabase.LoadAssetAtPath<Material>(assetPath);

            state.generatedGuid = AssetDatabase.AssetPathToGUID(assetPath);
            PointRendererAt(renderer, state.slot, state.targetPath, duplicate);

            return duplicate;
        }

        /// <summary>
        /// Writes each recorded property onto material, skipping any that
        /// already holds the recorded value. Returns whether anything was
        /// actually written, so the caller can avoid dirtying (and flushing)
        /// an asset that is already correct -- the normal case when the same
        /// commit is checked out twice.
        /// </summary>
        private static bool ApplyProperties(Material material, List<MaterialPropertyValue> properties, DiagnosticLog log)
        {
            var changed = false;

            // A null material here means the just-created/reused duplicate
            // failed to load (line ~101). Now that the per-property loop also
            // swallows NullReferenceException, an unchecked null would make
            // material.HasProperty NRE, silently skip every property, and let
            // the caller point the renderer slot at a null material. Fail
            // loudly instead -- CheckoutOperation catches InvalidOperationException
            // and warn-continues, so this still doesn't abort a checkout.
            if (material == null)
                throw new InvalidOperationException("Generated material could not be loaded; cannot apply properties.");

            foreach (var property in properties)
            {
                if (!material.HasProperty(property.name))
                {
                    log.Warn($"[AvatarVCS] Duplicate material has no property '{property.name}'; skipped.");
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
                            var color = ParseColor(property.value);
                            if (material.GetColor(property.name) != color)
                            {
                                material.SetColor(property.name, color);
                                changed = true;
                            }
                            break;
                        case "float":
                            var number = float.Parse(property.value, CultureInfo.InvariantCulture);
                            // Exact compare on purpose: both sides come from
                            // the same round-trip ("R" format), so an equal
                            // value really is bit-identical.
                            if (material.GetFloat(property.name) != number)
                            {
                                material.SetFloat(property.name, number);
                                changed = true;
                            }
                            break;
                        default:
                            log.Warn($"[AvatarVCS] Unsupported material property type '{property.type}' for '{property.name}' was skipped.");
                            break;
                    }
                }
                // NullReferenceException: a missing "value" key leaves
                // property.value null (JsonUtility), and ParseColor(null)
                // dereferences it in value.Split(',') before any Parse call
                // gets to throw ArgumentNullException.
                catch (Exception e) when (e is FormatException or OverflowException or IndexOutOfRangeException or ArgumentNullException or NullReferenceException)
                {
                    log.Warn($"[AvatarVCS] Could not parse material property '{property.name}' (type '{property.type}', value '{property.value}'): {e.Message}; skipped.");
                }
            }

            return changed;
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
