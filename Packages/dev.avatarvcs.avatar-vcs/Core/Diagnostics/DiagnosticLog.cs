using System.Collections.Generic;

namespace AvatarVcs.Core.Diagnostics
{
    public enum DiagnosticSeverity
    {
        Warning,
        Error,
    }

    /// <summary>
    /// One deferred diagnostic message. <see cref="Message"/> is the exact,
    /// ready-to-log string -- callers keep whatever prefix/wording they used
    /// with Debug.LogWarning so the sink can emit it byte-for-byte.
    /// </summary>
    public readonly struct Diagnostic
    {
        public readonly DiagnosticSeverity Severity;
        public readonly string Message;

        public Diagnostic(DiagnosticSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }
    }

    /// <summary>
    /// Collects diagnostics from a capture/apply operation so the caller can
    /// log them in one place (an Editor sink) or assert on them in a test,
    /// instead of every helper reaching for Debug.LogWarning directly. This
    /// is the same "return warnings, caller logs once" contract
    /// GameObjectStateApplier already used, generalised.
    ///
    /// Migration convention: an entry point takes <c>DiagnosticLog log =
    /// null</c>; when null it makes its own and flushes it through
    /// UnityDiagnosticSink at the end, so existing callers (and their
    /// LogAssert tests) keep working unchanged while new code can pass a log
    /// in and inspect <see cref="Entries"/>.
    /// </summary>
    public sealed class DiagnosticLog
    {
        private readonly List<Diagnostic> entries = new();

        public IReadOnlyList<Diagnostic> Entries => entries;

        public bool IsEmpty => entries.Count == 0;

        public void Warn(string message) =>
            entries.Add(new Diagnostic(DiagnosticSeverity.Warning, message));

        public void Error(string message) =>
            entries.Add(new Diagnostic(DiagnosticSeverity.Error, message));

        /// <summary>
        /// Appends every entry from another log -- used when an inner
        /// operation was given its own log and the outer one wants to fold
        /// the results back in.
        /// </summary>
        public void AddRange(DiagnosticLog other)
        {
            if (other == null) return;
            entries.AddRange(other.entries);
        }
    }
}
