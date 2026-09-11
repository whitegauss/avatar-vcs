using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

        private const string OverwriteSharedPref = "AvatarVcs.OverwriteSharedMaterials";

        /// <summary>
        /// Whether a material something outside the avatar is also wearing may
        /// be written to anyway.
        ///
        /// Off by default, and the warning that fires instead names this, so
        /// the choice is the user's and they find out it exists at the moment
        /// it matters. The safe direction is the default: an avatar quietly
        /// changing because a different avatar was checked out is the kind of
        /// surprise there is no undo for.
        /// </summary>
        public static bool OverwriteSharedMaterials
        {
            get => EditorPrefs.GetBool(OverwriteSharedPref, false);
            set => EditorPrefs.SetBool(OverwriteSharedPref, value);
        }

        /// <summary>
        /// The name of something outside avatarRoot that wears this material,
        /// or null when nothing does.
        ///
        /// Renderers inside prefab *assets* are skipped: the avatar in the
        /// scene is usually a prefab instance, so its own asset would
        /// otherwise report every one of its materials as shared. That leaves
        /// a material used only by a prefab (or a closed scene) undetected --
        /// the same open-scenes-only limit the rest of the tool works under.
        /// </summary>
        private static string SharedOutside(Material material, GameObject avatarRoot)
        {
            foreach (var renderer in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (renderer == null || EditorUtility.IsPersistent(renderer.gameObject)) continue;
                if (renderer.transform.IsChildOf(avatarRoot.transform)) continue;
                if (!renderer.sharedMaterials.Any(m => m == material)) continue;

                return renderer.gameObject.name;
            }

            return null;
        }

        /// <summary>
        /// A material inside a package is read-only: AssetDatabase can't write
        /// there, and it isn't the user's file to change either.
        /// </summary>
        private static bool IsWritable(string assetPath) =>
            !string.IsNullOrEmpty(assetPath)
            && assetPath.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal);

        public static Material Apply(MaterialSettingsState state, GameObject avatarRoot, DiagnosticLog log = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (avatarRoot == null) throw new ArgumentNullException(nameof(avatarRoot));

            // KAN-20: per-property "couldn't apply that one" warnings collect
            // into a DiagnosticLog. A caller mid-checkout passes its own; a
            // direct caller (tests) passes none, so make one and flush it --
            // even on the throw path, so warnings logged before the throw
            // still reach the console.
            using var diagnostics = DiagnosticScope.OwnOrBorrow(ref log);

            return ApplyCore(state, avatarRoot, log);
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

            // Before anything is generated: when the recorded values are
            // already what the source material holds, the source *is* the
            // recorded state and duplicating it buys nothing. The copy would
            // not even be byte-identical -- writing every recorded property
            // out explicitly materialises defaults the source never
            // serialised -- so nothing downstream could tell the two apart
            // and collapse them again.
            //
            // This is where the volume came from. materialSettings records
            // every slot whose shader is supported, changed or not, so a real
            // lilToon avatar recorded 31-46 slots and a checkout wrote a
            // duplicate for every one of them, per commit, none of which the
            // user had ever edited.
            var probe = new DiagnosticLog();
            if (!WouldChange(sourceMaterial, state.properties, probe))
            {
                // Nothing to write -- but a property that could not be applied
                // at all is still worth saying, and the real apply that would
                // have said it never runs on this path.
                log.AddRange(probe);

                // Drop any duplicate a previous checkout made for this entry:
                // the commit no longer claims it, so the sweeper can collect
                // it (CheckoutOperation rewrites generatedAssets from these).
                state.generatedGuid = null;
                PointRendererAt(renderer, state.slot, state.targetPath, sourceMaterial);
                return sourceMaterial;
            }

            // The recorded settings go onto the user's own material. A
            // separate copy per commit is what buried a real project under 552
            // generated materials; the settings are the thing being version-
            // controlled, and every other kind of tracked state (BlendShape
            // weights, component fields, active flags) is likewise written
            // straight onto the object it belongs to.
            //
            // Two cases still can't write there, and both fall through to the
            // one duplicate below: a material inside a read-only package, and
            // one that something outside this avatar is also wearing --
            // changing that would reach into work this checkout was never
            // asked to touch.
            var sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);
            var sharedWith = SharedOutside(sourceMaterial, avatarRoot);
            var writable = IsWritable(sourcePath);

            if (writable && (sharedWith == null || OverwriteSharedMaterials))
            {
                if (sharedWith != null)
                {
                    log.Warn($"[AvatarVCS] '{sourcePath}' is also used by '{sharedWith}', outside this avatar, "
                        + "and was overwritten anyway because Tools > AvatarVCS > Overwrite Shared Materials is on.");
                }

                if (ApplyProperties(sourceMaterial, state.properties, log))
                {
                    EditorUtility.SetDirty(sourceMaterial);
                    SaveUnlessBatched();
                }

                // Any duplicate an older checkout made for this entry is no
                // longer claimed by the commit, so the sweeper can collect it
                // (CheckoutOperation rewrites generatedAssets from these).
                state.generatedGuid = null;
                PointRendererAt(renderer, state.slot, state.targetPath, sourceMaterial);
                return sourceMaterial;
            }

            if (sharedWith != null)
            {
                log.Warn($"[AvatarVCS] '{sourcePath}' is also used by '{sharedWith}', outside this avatar, so this "
                    + "avatar gets its own copy of it instead of that material being changed. "
                    + "Turn on Tools > AvatarVCS > Overwrite Shared Materials to change the material itself.");
            }

            // Exactly one duplicate per source material, at a fixed path,
            // reused for the life of the project -- not one per commit. Only
            // one commit's state is ever live in the scene, so the duplicate
            // is a working copy of whatever is currently checked out, and
            // re-applying the recorded values onto it is the same
            // destroy-and-regenerate contract containers have.
            var duplicateName = GeneratedAssetNaming.DuplicateName(sourceMaterial.name);

            var directory = System.IO.Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
            if (!writable || string.IsNullOrEmpty(directory))
            {
                // A source material inside an immutable/read-only UPM
                // package (Packages/...) can't have a sibling asset written
                // next to it -- AssetDatabase.CreateAsset would fail there.
                directory = GeneratedAssetNaming.GeneratedFolder;
                if (!AssetDatabase.IsValidFolder(directory))
                    AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(GeneratedAssetNaming.GeneratedFolder));
            }

            var canonicalPath = $"{directory}/{duplicateName}{GeneratedAssetNaming.MaterialExtension}";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(canonicalPath);

            // Somebody else's asset sitting on the name we want. Very
            // unlikely, and still not ours to overwrite.
            if (existing != null && GeneratedMaterialSource.ResolveSourceGuid(canonicalPath) != sourceGuid)
            {
                canonicalPath = AssetDatabase.GenerateUniqueAssetPath(canonicalPath);
                existing = null;
            }

            if (existing != null)
            {
                // Only write when a value actually differs: re-checking out
                // the same commit normally finds the duplicate already
                // correct, and dirtying it would make every checkout flush
                // the asset database for nothing.
                if (ApplyProperties(existing, state.properties, log))
                {
                    EditorUtility.SetDirty(existing);
                    SaveUnlessBatched();
                }

                state.generatedGuid = AssetDatabase.AssetPathToGUID(canonicalPath);
                PointRendererAt(renderer, state.slot, state.targetPath, existing);
                return existing;
            }

            // Copy-constructing reads sourceMaterial but never writes to it.
            // Name/placement come from GeneratedAssetNaming so the deletion
            // guard in CommitStore recognises exactly what we emit (KAN-76).
            var duplicate = new Material(sourceMaterial) { name = duplicateName };
            ApplyProperties(duplicate, state.properties, log);

            AssetDatabase.CreateAsset(duplicate, canonicalPath);
            SaveUnlessBatched();

            // The link back to what this was made from, written into the
            // duplicate's own .meta so capture can resolve it later even
            // after a rename or a move (GeneratedMaterialSource). Before the
            // reload below, not after: it re-imports the asset, which is
            // exactly what makes an already-held reference stale.
            GeneratedMaterialSource.Record(canonicalPath, sourceGuid);

            // CreateAsset can trigger a reimport that leaves the pre-save
            // reference stale; reload so callers and the renderer get the
            // same canonical instance that later AssetDatabase lookups see.
            duplicate = AssetDatabase.LoadAssetAtPath<Material>(canonicalPath);

            state.generatedGuid = AssetDatabase.AssetPathToGUID(canonicalPath);
            PointRendererAt(renderer, state.slot, state.targetPath, duplicate);

            return duplicate;
        }

        /// <summary>
        /// Whether applying these properties to material would actually
        /// change it.
        ///
        /// Runs the real ApplyProperties against a throwaway in-memory copy
        /// rather than reimplementing the comparison. A second implementation
        /// drifting from this one is exactly how "it renders identically but
        /// we duplicated it anyway" comes back.
        /// </summary>
        /// <param name="probeLog">
        /// Collects the warnings the dry run produces. The caller decides what
        /// to do with them: fold them in when this returns false (nothing else
        /// will report them), drop them when it returns true (the real apply
        /// is about to say the same thing).
        /// </param>
        private static bool WouldChange(Material material, List<MaterialPropertyValue> properties, DiagnosticLog probeLog)
        {
            var probe = new Material(material);
            try
            {
                return ApplyProperties(probe, properties, probeLog);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
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

            // Property names whose texture couldn't be resolved. Their
            // tiling/offset must be skipped too: the slot keeps the texture
            // the duplicate inherited, and stamping the recorded scale/offset
            // onto a different texture is worse than leaving both alone.
            var unresolvedTextures = new HashSet<string>();

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
                    log.Warn($"[AvatarVCS] Material has no property '{property.name}'; skipped.");
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
                        case "texture":
                            if (!TryApplyTexture(material, property, log, out var textureChanged))
                                unresolvedTextures.Add(property.name);
                            else if (textureChanged) changed = true;
                            break;
                        case ShaderPropertyMap.TextureScaleOffsetType:
                            if (unresolvedTextures.Contains(property.name)) break;
                            if (ApplyTextureScaleOffset(material, property)) changed = true;
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

        /// <summary>
        /// Restores one texture slot by GUID, the same way a material slot is
        /// restored. An empty recorded value means "nothing was assigned",
        /// which is restored as null so the shader falls back to its own
        /// default -- that is what lets a checkout clear a texture the source
        /// material has since gained.
        /// </summary>
        /// <summary>
        /// Restores one texture slot by GUID, the same way a material slot is
        /// restored. An empty recorded value means "nothing was assigned",
        /// which is restored as null so the shader falls back to its own
        /// default -- that is what lets a checkout clear a texture the source
        /// material has since gained.
        ///
        /// Returns false when the recorded GUID could not be resolved, which
        /// is not the same as "nothing changed": the caller must then also
        /// skip this property's tiling/offset.
        /// </summary>
        private static bool TryApplyTexture(Material material, MaterialPropertyValue property, DiagnosticLog log,
            out bool changed)
        {
            changed = false;
            Texture texture = null;

            if (!string.IsNullOrEmpty(property.value))
            {
                // GuidRemapper for the same reason sourceMaterialGuid uses it:
                // a re-imported texture gets a new guid (design doc 6.4).
                var path = AssetDatabase.GUIDToAssetPath(GuidRemapper.Resolve(property.value));
                texture = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture>(path);

                if (texture == null)
                {
                    // Deleted or replaced since the commit. Leaving whatever
                    // the duplicate inherited beats blanking the slot, and
                    // this is exactly the "may look different" case worth
                    // telling the user about.
                    log.Warn($"[AvatarVCS] Texture for '{property.name}' (GUID '{property.value}') could not be "
                        + "resolved; that slot's texture and tiling are left as-is.");
                    return false;
                }
            }

            if (material.GetTexture(property.name) == texture) return true;

            material.SetTexture(property.name, texture);
            changed = true;
            return true;
        }

        private static bool ApplyTextureScaleOffset(Material material, MaterialPropertyValue property)
        {
            var parts = property.value.Split(',');
            if (parts.Length != 4) throw new FormatException($"expected 4 components, got {parts.Length}");

            var scale = new Vector2(
                float.Parse(parts[0], CultureInfo.InvariantCulture),
                float.Parse(parts[1], CultureInfo.InvariantCulture));
            var offset = new Vector2(
                float.Parse(parts[2], CultureInfo.InvariantCulture),
                float.Parse(parts[3], CultureInfo.InvariantCulture));

            if (material.GetTextureScale(property.name) == scale
                && material.GetTextureOffset(property.name) == offset)
                return false;

            material.SetTextureScale(property.name, scale);
            material.SetTextureOffset(property.name, offset);
            return true;
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
