using System.IO;

namespace AvatarVcs.Editor.History
{
    /// <summary>
    /// Writing a file so that a crash or power loss leaves either the old
    /// contents or the new ones, never a half-written mixture.
    ///
    /// Extracted from CommitStore (KAN-18) so GuidRemapper gets the same
    /// guarantee. Both write small JSON files that the tool cannot recover
    /// from if truncated: a torn commit breaks that avatar's history, and a
    /// torn guid-remapping file breaks prefab resolution for every commit
    /// that relies on it.
    /// </summary>
    public static class AtomicFile
    {
        public static void WriteAllText(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var tempPath = $"{path}.tmp";

            // The flush to disk is the point. Without it the rename below can
            // land before the bytes do, and a power loss in that window
            // leaves a file that exists, is the right size, and is full of
            // zeroes. StreamWriter(string) defaults to UTF-8 with no BOM,
            // matching File.WriteAllText; BaseStream is the FileStream.
            using (var writer = new StreamWriter(tempPath, append: false))
            {
                writer.Write(content);
                writer.Flush();
                ((FileStream)writer.BaseStream).Flush(flushToDisk: true);
            }

            if (File.Exists(path))
                File.Replace(tempPath, path, null);
            else
                File.Move(tempPath, path);
        }
    }
}
