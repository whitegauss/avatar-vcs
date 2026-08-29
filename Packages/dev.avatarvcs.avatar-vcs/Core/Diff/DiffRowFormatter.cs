using AvatarVcs.Core.Model;

namespace AvatarVcs.Core.Diff
{
    /// <summary>
    /// The meaning behind a diff row's presentation -- Added/Removed/Changed/
    /// Neutral -- as opposed to a concrete color, which is a UI concern left
    /// to the caller.
    /// </summary>
    public enum DiffTone
    {
        Neutral,
        Added,
        Removed,
        Changed,
    }

    /// <summary>
    /// Symbol/tone/label decisions for one ContainerDiff row, split out of
    /// AvatarVcsWindow so they're testable without an EditorWindow.
    /// </summary>
    public static class DiffRowFormatter
    {
        public static string Symbol(DiffKind kind) => kind switch
        {
            DiffKind.Added => "+",
            DiffKind.Removed => "-",
            DiffKind.Changed => "~",
            _ => "=",
        };

        public static DiffTone ToneOf(DiffKind kind) => kind switch
        {
            DiffKind.Added => DiffTone.Added,
            DiffKind.Removed => DiffTone.Removed,
            DiffKind.Changed => DiffTone.Changed,
            _ => DiffTone.Neutral,
        };

        public static string RowLabel(ContainerDiff diff)
        {
            var label = $"{Symbol(diff.kind)} {diff.containerId}";
            if (diff.kind == DiffKind.Unchanged) label += " (unchanged)";
            return label;
        }
    }
}
