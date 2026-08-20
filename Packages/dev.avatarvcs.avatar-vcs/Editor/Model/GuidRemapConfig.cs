using System;
using System.Collections.Generic;

namespace AvatarVcs.Editor.Model
{
    [Serializable]
    public class GuidRemapEntry
    {
        public string oldGuid;
        public string newGuid;
    }

    /// <summary>
    /// Project-wide GUID remapping (design doc section 6.4): once a user
    /// resolves a re-imported asset's new GUID, the mapping applies to every
    /// future checkout automatically instead of asking again. Stored at
    /// ProjectSettings/AvatarVcs/guid-remapping.json -- project-scoped, not
    /// per-avatar, since a re-import affects every avatar that referenced it.
    /// </summary>
    [Serializable]
    public class GuidRemapConfig
    {
        public List<GuidRemapEntry> mappings = new();
    }
}
