using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using AvatarVcs.Core.History;

namespace AvatarVcs.Core.Repair
{
    /// <summary>
    /// Pulls every MonoBehaviour out of a saved scene's YAML, along with the
    /// two guid fields our marker components carry.
    ///
    /// Deliberately a text scan rather than a YAML parser: Unity's scene
    /// format is a stream of small documents with a fixed shape, and this only
    /// needs four lines out of each MonoBehaviour. The same reasoning (and the
    /// same "a guid is a plain serialized string, so it appears verbatim")
    /// that AvatarHistoryInventory already relies on to find avatars in closed
    /// scenes.
    /// </summary>
    public static class SceneYamlMarkerScanner
    {
        // "--- !u!114 &443647173", possibly with a trailing " stripped".
        // 114 is MonoBehaviour; every other class id starts a document we skip.
        private static readonly Regex DocumentHeader =
            new Regex(@"^--- !u!(?<classId>\d+) &(?<fileId>-?\d+)", RegexOptions.Compiled);

        private static readonly Regex ScriptGuid =
            new Regex(@"^\s{2}m_Script: \{fileID: -?\d+, guid: (?<guid>[0-9a-f]{32}), type: \d+\}", RegexOptions.Compiled);

        private static readonly Regex GameObjectId =
            new Regex(@"^\s{2}m_GameObject: \{fileID: (?<fileId>-?\d+)\}", RegexOptions.Compiled);

        private static readonly Regex GuidField =
            new Regex(@"^\s{2}(?<name>avatarGuid|containerGuid): (?<value>[0-9a-fA-F]{32})\s*$", RegexOptions.Compiled);

        private static readonly Regex CorrespondingSource =
            new Regex(@"^\s{2}m_CorrespondingSourceObject: \{fileID: (?<fileId>-?\d+)", RegexOptions.Compiled);

        private static readonly Regex PrefabInstance =
            new Regex(@"^\s{2}m_PrefabInstance: \{fileID: (?<fileId>-?\d+)\}", RegexOptions.Compiled);

        private const string MonoBehaviourClassId = "114";
        private const string GameObjectClassId = "1";

        /// <summary>
        /// Every GameObject document, with what ties it to a prefab.
        ///
        /// A GameObject that came from a prefab is serialized as a "stripped"
        /// entry: the id the scene refers to it by is its own, but its
        /// identity is (which object of the prefab, which instance of that
        /// prefab). That pair is what GlobalObjectId reports for the live
        /// object -- its targetObjectId is the id *inside the prefab*, not the
        /// scene id -- so matching the two needs this, not just the scene id.
        /// Avatars are prefab instances, so this is the normal case.
        /// </summary>
        public static List<SceneGameObjectEntry> ReadGameObjects(string sceneYaml)
        {
            var entries = new List<SceneGameObjectEntry>();
            if (string.IsNullOrEmpty(sceneYaml)) return entries;

            SceneGameObjectEntry current = null;
            foreach (var line in sceneYaml.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');

                var header = DocumentHeader.Match(trimmed);
                if (header.Success)
                {
                    if (current != null) entries.Add(current);
                    current = header.Groups["classId"].Value == GameObjectClassId
                        ? new SceneGameObjectEntry
                        {
                            fileId = long.Parse(header.Groups["fileId"].Value, CultureInfo.InvariantCulture),
                        }
                        : null;
                    continue;
                }

                if (current == null) continue;

                var source = CorrespondingSource.Match(trimmed);
                if (source.Success)
                {
                    current.sourceFileId = long.Parse(source.Groups["fileId"].Value, CultureInfo.InvariantCulture);
                    continue;
                }

                var instance = PrefabInstance.Match(trimmed);
                if (instance.Success)
                    current.prefabInstanceFileId = long.Parse(instance.Groups["fileId"].Value, CultureInfo.InvariantCulture);
            }

            if (current != null) entries.Add(current);

            return entries;
        }

        public static List<SceneMarkerBlock> ReadMonoBehaviours(string sceneYaml)
        {
            var blocks = new List<SceneMarkerBlock>();
            if (string.IsNullOrEmpty(sceneYaml)) return blocks;

            SceneMarkerBlock current = null;
            foreach (var line in sceneYaml.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');

                var header = DocumentHeader.Match(trimmed);
                if (header.Success)
                {
                    // A block is only kept once its m_Script line has been
                    // seen: a MonoBehaviour without one isn't addressable.
                    if (current != null && current.scriptGuid != null) blocks.Add(current);
                    current = header.Groups["classId"].Value == MonoBehaviourClassId ? new SceneMarkerBlock() : null;
                    continue;
                }

                if (current == null) continue;

                var script = ScriptGuid.Match(trimmed);
                if (script.Success)
                {
                    current.scriptGuid = script.Groups["guid"].Value;
                    continue;
                }

                var owner = GameObjectId.Match(trimmed);
                if (owner.Success)
                {
                    current.gameObjectFileId = long.Parse(owner.Groups["fileId"].Value, CultureInfo.InvariantCulture);
                    continue;
                }

                var field = GuidField.Match(trimmed);
                if (!field.Success) continue;

                var value = field.Groups["value"].Value;
                // Anything not shaped like one of our guids is somebody
                // else's field that happens to share the name.
                if (!CommitIdentifier.IsValidShape(value)) continue;

                if (field.Groups["name"].Value == "avatarGuid") current.avatarGuid = value;
                else current.containerGuid = value;
            }

            if (current != null && current.scriptGuid != null) blocks.Add(current);

            return blocks;
        }
    }
}
