using AvatarVcs.Core.Diagnostics;
using UnityEngine;

namespace AvatarVcs.Editor.Diagnostics
{
    /// <summary>
    /// The one place a <see cref="DiagnosticLog"/> collected during an Editor
    /// operation gets written to the Unity console. Entries are emitted
    /// verbatim: every message already carries its "[AvatarVCS] " prefix and
    /// exact wording from when it was a direct Debug.LogWarning call, and
    /// KAN-20's contract is that that text does not change by a character, so
    /// this routes by severity and adds nothing.
    /// </summary>
    public static class UnityDiagnosticSink
    {
        public static void Flush(DiagnosticLog log)
        {
            if (log == null) return;

            foreach (var entry in log.Entries)
            {
                if (entry.Severity == DiagnosticSeverity.Error)
                    Debug.LogError(entry.Message);
                else
                    Debug.LogWarning(entry.Message);
            }
        }
    }
}
