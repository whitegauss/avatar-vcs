using System;
using System.Collections.Generic;

namespace AvatarVcs.Editor.Model
{
    public enum DiffKind
    {
        Added,
        Removed,
        Changed,
        Unchanged,
    }

    /// <summary>
    /// Container-level diff entry. Design doc section 3.3: containers are
    /// compared first; changeNotes holds the field-level detail an expanded
    /// UI row would show.
    /// </summary>
    [Serializable]
    public class ContainerDiff
    {
        public string containerId;
        public DiffKind kind;
        public string prefabNameBefore;
        public string prefabNameAfter;
        public List<string> changeNotes = new();
    }
}
