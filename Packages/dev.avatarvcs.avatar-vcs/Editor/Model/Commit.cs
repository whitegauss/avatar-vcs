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

        /// <summary>
        /// GUIDs of assets generated while checking this commit out (design
        /// doc section 4/1.4.3), currently just materialSettings' duplicate
        /// materials. Kept in sync with each entry's generatedGuid by
        /// CheckoutOperation; CommitStore.DeleteCommit deletes these assets
        /// when the commit itself is deleted.
        /// </summary>
        public List<string> generatedAssets = new();

        /// <summary>
        /// Content hashes of referenced assets at commit time (design doc
        /// section 6.3), so checkout can warn when a prefab/material has
        /// since been overwritten in place (same GUID, different content).
        /// </summary>
        public List<AssetVersionEntry> assetVersions = new();
    }
}
