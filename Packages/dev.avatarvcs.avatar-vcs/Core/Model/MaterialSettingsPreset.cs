using System.Collections.Generic;

namespace AvatarVcs.Core.Model
{
    /// <summary>
    /// Shader settings lifted off one material, portable to a different
    /// material on a different avatar in a different project.
    ///
    /// Deliberately carries neither targetPath, slot, nor sourceMaterialGuid,
    /// unlike MaterialSettingsState. Those bind a recording to one place in
    /// one avatar's hierarchy, which is exactly what has to be dropped for
    /// "send your friend your settings" to work -- the destination is
    /// whichever slot they have selected when they import.
    ///
    /// Dropping sourceMaterialGuid also means the file doesn't record which
    /// asset the settings came from.
    ///
    /// Textures are referenced by GUID and never embedded, so a preset is
    /// values only: it cannot stand in for an asset the recipient doesn't
    /// already own. That is the same property the commit format has, and it
    /// is what makes sharing one safer than sending the .mat itself.
    /// </summary>
    public class MaterialSettingsPreset
    {
        /// <summary>
        /// The shader these values were read from, e.g.
        /// "Hidden/lilToonOutline". Recorded so an import onto a material on
        /// a different shader can say so rather than quietly writing a
        /// handful of coincidentally-named properties.
        /// </summary>
        public string shader;

        /// <summary>
        /// A human note travelling with the preset -- what it is, what it
        /// expects. The file is meant to be read by whoever receives it.
        /// </summary>
        public string description;

        public List<MaterialPropertyValue> properties = new();
    }
}
