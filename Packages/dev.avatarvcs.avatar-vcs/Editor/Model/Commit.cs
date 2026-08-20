using System;
using System.Collections.Generic;

namespace AvatarVcs.Editor.Model
{
    /// <summary>
    /// A full snapshot of an avatar's tracked state at one point in time.
    /// Design doc section 2.1.
    /// </summary>
    [Serializable]
    public class Commit
    {
        public int schemaVersion = 2;
        public string commitId;
        public string parentCommitId;
        public string branch;
        public string message;
        public string timestamp;
        public string avatarGuid;
        public string avatarName;
        public List<ContainerSnapshot> containers = new();
        public List<AvatarReferenceState> avatarReferences = new();
        public List<MaterialSettingsState> materialSettings = new();
    }
}
