using System;
using System.Collections.Generic;

namespace AvatarVcs.Core.Model
{
    [Serializable]
    public class MaterialPropertyValue
    {
        public string name;
        public string type; // "color" | "float"
        public string value;
    }

    /// <summary>
    /// Recorded shader property values for one material slot (design doc 1.4.3).
    /// Restoring this never touches sourceMaterialGuid's asset; it duplicates it
    /// and applies properties to the duplicate.
    /// </summary>
    [Serializable]
    public class MaterialSettingsState
    {
        public string targetPath;
        public int slot;
        public string sourceMaterialGuid;
        public string shader;
        public List<MaterialPropertyValue> properties = new();

        /// <summary>
        /// GUID of the previously-generated duplicate material, if any.
        /// Populated after the first Apply and persisted back onto the
        /// commit so re-checking out the same commit reuses it instead of
        /// generating a new duplicate every time (design doc 1.4.3's GC
        /// section implies generated assets are commit-scoped and stable,
        /// not regenerated per checkout).
        /// </summary>
        public string generatedGuid;
    }
}
