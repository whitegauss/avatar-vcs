using System;
using System.Collections.Generic;

namespace AvatarVcs.Core.Model
{
    /// <summary>
    /// A full snapshot of an avatar's tracked state at one point in time.
    /// Design doc section 2.1.
    /// </summary>
    [Serializable]
    public class Commit
    {
        /// <summary>
        /// Highest commit schemaVersion this build understands.
        /// CommitStore.LoadCommit refuses (warn + null) anything higher, so a
        /// commit written by a future build isn't silently restored with
        /// fields this build can't see. Bump this ONLY for a change an older
        /// build cannot safely fall back from -- a new field whose absence an
        /// old reader already handles (e.g. BlendShapeRef.path defaulting to
        /// the target itself) is additive and does NOT bump it.
        /// </summary>
        public const int CurrentSchemaVersion = 2;

        public int schemaVersion = CurrentSchemaVersion;
        public string commitId;
        public string parentCommitId;
        public string branch;
        public string message;

        /// <summary>
        /// A free-form note, editable after the fact, separate from the
        /// one-line commit message. The message names the commit in a list;
        /// this is where "the outfit here is X plus Y, and the hair needs the
        /// shoulder toggle off" goes.
        ///
        /// Additive: absent in older commits, where JsonUtility leaves it
        /// null, and an older build simply ignores it. schemaVersion does not
        /// move (see CurrentSchemaVersion's rule).
        /// </summary>
        public string note;

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
