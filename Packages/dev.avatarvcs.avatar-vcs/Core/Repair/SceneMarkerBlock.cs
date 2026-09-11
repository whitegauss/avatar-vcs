namespace AvatarVcs.Core.Repair
{
    /// <summary>
    /// One MonoBehaviour as it appears in a saved scene's YAML: which script
    /// it points at, which GameObject it sits on, and the two guid fields our
    /// markers carry.
    ///
    /// This exists because a component whose script Unity can't resolve is
    /// unreadable through the normal API -- GetComponents returns a null, and
    /// nothing on it can be queried -- while the file on disk still holds
    /// everything it was serialized with. Repairing a marker whose script guid
    /// changed under it (see MarkerRepair) means reading the file.
    /// </summary>
    public sealed class SceneMarkerBlock
    {
        /// <summary>The guid from m_Script, i.e. the .cs asset it points at.</summary>
        public string scriptGuid;

        /// <summary>
        /// The local file id of the GameObject this sits on -- the scene-local
        /// identity that survives the script going missing, and what the
        /// editor side matches live objects against.
        /// </summary>
        public long gameObjectFileId;

        /// <summary>AvatarVcsRoot's field, or null when the block has none.</summary>
        public string avatarGuid;

        /// <summary>AvatarVcsContainer's field, or null when the block has none.</summary>
        public string containerGuid;

        /// <summary>
        /// True when neither guid field is present. AvatarVcsTrackedReference
        /// and AvatarVcsUntracked both serialize to this -- they carry no
        /// fields at all -- which is why telling those two apart needs
        /// evidence from outside the file (MarkerGuidClassifier).
        /// </summary>
        public bool IsDataless => avatarGuid == null && containerGuid == null;
    }

    /// <summary>
    /// One GameObject document in a scene, and what ties it to a prefab.
    /// sourceFileId/prefabInstanceFileId are 0 for an object that is just
    /// part of the scene; for one that came from a prefab they are the id of
    /// the object inside the prefab and the id of the instance, which is the
    /// pair GlobalObjectId reports for the live object.
    /// </summary>
    public sealed class SceneGameObjectEntry
    {
        public long fileId;
        public long sourceFileId;
        public long prefabInstanceFileId;
    }
}
