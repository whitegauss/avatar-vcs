using System;
using System.IO;
using AvatarVcs.Core.History;
using UnityEngine;

namespace AvatarVcs.Editor.History
{
    public enum StoredFileStatus
    {
        /// <summary>No file. Normal for an avatar with no history yet.</summary>
        Missing,
        Loaded,

        /// <summary>Present but not parseable: truncated by a crash, emptied, hand-edited badly.</summary>
        Unreadable,

        /// <summary>Parseable, but written by a build that knows more fields than this one.</summary>
        TooNew,
    }

    /// <summary>
    /// Reading and writing this package's own JSON state files with the two
    /// rules that keep data across versions of the package (see StoredSchema):
    /// never write over a file written by a newer build, and never destroy a
    /// file that can't be read.
    ///
    /// The second rule is the one with teeth. Loading used to answer "corrupt"
    /// and "absent" identically, which is fine for a load -- and was quietly
    /// fatal on the next save: SaveCommit loads the index, adds one entry and
    /// writes it back, so an unreadable index.json became a one-entry index
    /// and the avatar's whole history disappeared from the UI. Reads still
    /// degrade to empty (a broken file must not brick the window), but a write
    /// that would land on top of a file we couldn't read moves it aside first.
    /// </summary>
    internal static class StoredJson
    {
        /// <summary>
        /// Reads only the version field, so a file whose other contents this
        /// build can't represent is still comparable. JsonUtility ignores keys
        /// it has no field for, which is exactly what makes this work -- and
        /// exactly why writing such a file back would drop them.
        /// </summary>
        [Serializable]
        private class SchemaProbe
        {
            // 0, not the current version: a file written before this package
            // recorded a version has no key here, and "older than anything"
            // is the honest reading of that.
            public int schemaVersion;
        }

        public static (T value, StoredFileStatus status) Load<T>(string path, int supportedSchema) where T : class
        {
            if (!File.Exists(path)) return (null, StoredFileStatus.Missing);

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[AvatarVCS] Could not read '{path}'. {e.Message}");
                return (null, StoredFileStatus.Unreadable);
            }

            try
            {
                var onDisk = JsonUtility.FromJson<SchemaProbe>(text)?.schemaVersion ?? 0;
                if (StoredSchema.IsNewerThanSupported(onDisk, supportedSchema))
                {
                    Debug.LogWarning("[AvatarVCS] " + StoredSchema.RefusalMessage(path, onDisk, supportedSchema));
                    return (null, StoredFileStatus.TooNew);
                }

                var value = JsonUtility.FromJson<T>(text);
                return value == null
                    ? (null, StoredFileStatus.Unreadable)
                    : (value, StoredFileStatus.Loaded);
            }
            catch (ArgumentException e)
            {
                Debug.LogWarning($"[AvatarVCS] Could not parse '{path}' as {typeof(T).Name}. {e.Message}");
                return (null, StoredFileStatus.Unreadable);
            }
        }

        /// <summary>
        /// Call before overwriting path. Throws when the file on disk is newer
        /// than this build understands -- aborting a commit is recoverable,
        /// truncating the file it would have been recorded in is not -- and
        /// moves an unreadable file aside so the write doesn't erase it.
        /// </summary>
        public static void EnsureWritable<T>(string path, int supportedSchema) where T : class
        {
            var (_, status) = Load<T>(path, supportedSchema);
            switch (status)
            {
                case StoredFileStatus.TooNew:
                    throw new InvalidOperationException(StoredSchema.RefusalMessage(path, OnDiskSchema(path), supportedSchema));
                case StoredFileStatus.Unreadable:
                    var kept = Quarantine(path);
                    if (kept != null)
                    {
                        Debug.LogWarning($"[AvatarVCS] '{path}' could not be read, so it has been moved to '{kept}' "
                            + "rather than being overwritten. Nothing in it is lost; it is just no longer in the way.");
                    }

                    break;
            }
        }

        private static int OnDiskSchema(string path)
        {
            try
            {
                return JsonUtility.FromJson<SchemaProbe>(File.ReadAllText(path))?.schemaVersion ?? 0;
            }
            catch (Exception e) when (e is ArgumentException or IOException)
            {
                return 0;
            }
        }

        /// <summary>
        /// Moves path aside, keeping the extension off the end so nothing
        /// tries to parse it again. Returns where it went, or null if it
        /// couldn't be moved -- in which case the caller's write still
        /// proceeds: refusing to save because a file we already can't read is
        /// also stuck would leave the user with no way forward at all.
        /// </summary>
        public static string Quarantine(string path)
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var target = attempt == 0 ? $"{path}.corrupt-{stamp}" : $"{path}.corrupt-{stamp}-{attempt}";
                if (File.Exists(target)) continue;

                try
                {
                    File.Move(path, target);
                    return target;
                }
                catch (IOException e)
                {
                    Debug.LogWarning($"[AvatarVCS] Could not move '{path}' aside. {e.Message}");
                    return null;
                }
            }

            return null;
        }
    }
}
