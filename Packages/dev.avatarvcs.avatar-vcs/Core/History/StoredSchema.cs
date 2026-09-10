namespace AvatarVcs.Core.History
{
    /// <summary>
    /// The one rule for reading and writing this tool's own JSON files across
    /// versions of the package.
    ///
    /// JsonUtility deserializes into fields and serializes the fields back
    /// out: a key it has no field for is dropped on the way through. So an
    /// older build that reads a file a newer build wrote, changes one thing
    /// and saves it has silently deleted every field the newer build added.
    /// For a commit that costs part of a snapshot; for index.json it costs
    /// the list of every commit an avatar has.
    ///
    /// Hence: a stored file names the schema it was written with, a build
    /// refuses to overwrite one that claims to be newer than it understands,
    /// and the user is told to update the package instead. Downgrading is
    /// rare, but "install the update, open the project, look at it with the
    /// old one still on another machine" is not.
    /// </summary>
    public static class StoredSchema
    {
        public static bool IsNewerThanSupported(int onDisk, int supported) => onDisk > supported;

        /// <summary>
        /// Said on both sides of the rule -- when such a file is read, and
        /// when a write to it is refused -- so a user who sees it twice sees
        /// the same sentence and the same fix.
        /// </summary>
        public static string RefusalMessage(string path, int onDisk, int supported) =>
            $"'{path}' was written by a newer AvatarVCS (schemaVersion {onDisk}, this build understands {supported}). "
            + "It is being left alone: this build can't see everything in it, and writing it back would delete "
            + "the parts it can't see. Update the AvatarVCS package.";
    }
}
