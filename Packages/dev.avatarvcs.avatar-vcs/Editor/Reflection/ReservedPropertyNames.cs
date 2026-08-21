using System.Collections.Generic;

namespace AvatarVcs.Editor.Reflection
{
    /// <summary>
    /// SerializedProperty names that identify/link a Component/GameObject
    /// rather than describe its configuration (script reference, prefab
    /// linkage, hide flags, ...). ComponentCapturer never captures these;
    /// ComponentApplier must never write them either -- a value for one of
    /// these keys can only appear in a ComponentState if it came from
    /// somewhere other than this tool's own capture (a hand-edited or
    /// corrupted commit file), and writing e.g. m_Script would silently
    /// change which script drives an existing component.
    /// </summary>
    public static class ReservedPropertyNames
    {
        public static readonly HashSet<string> Names = new()
        {
            "m_Script",
            "m_ObjectHideFlags",
            "m_CorrespondingSourceObject",
            "m_PrefabInstance",
            "m_PrefabAsset",
            "m_GameObject",
            "m_EditorClassIdentifier",
            "m_EditorHideFlags",
        };
    }
}
