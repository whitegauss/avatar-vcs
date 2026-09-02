using System.Collections.Generic;
using AvatarVcs.Core.Diagnostics;

namespace AvatarVcs.Core.History
{
    public enum CheckoutResultKind
    {
        Success,
        MissingPrefabs,
    }

    public class CheckoutResult
    {
        public CheckoutResultKind Kind { get; }
        public string AutoCommitId { get; }
        public List<string> MissingPrefabGuids { get; }
        public List<string> VersionWarnings { get; }

        /// <summary>
        /// Non-fatal problems hit while applying the commit (a slot's material
        /// settings failed, a field couldn't be decoded, ...). Already logged
        /// to the console by CheckoutOperation; surfaced here too so a caller
        /// or test can inspect them without scraping the log. Empty on a clean
        /// checkout and always non-null.
        /// </summary>
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public bool IsSuccess => Kind == CheckoutResultKind.Success;

        private CheckoutResult(CheckoutResultKind kind, string autoCommitId, List<string> missingPrefabGuids,
            List<string> versionWarnings, IReadOnlyList<Diagnostic> diagnostics)
        {
            Kind = kind;
            AutoCommitId = autoCommitId;
            MissingPrefabGuids = missingPrefabGuids;
            VersionWarnings = versionWarnings ?? new List<string>();
            Diagnostics = diagnostics ?? new List<Diagnostic>();
        }

        public static CheckoutResult Success(string autoCommitId, List<string> versionWarnings = null,
            IReadOnlyList<Diagnostic> diagnostics = null) =>
            new(CheckoutResultKind.Success, autoCommitId, null, versionWarnings, diagnostics);
        public static CheckoutResult MissingPrefabs(List<string> missingGuids) =>
            new(CheckoutResultKind.MissingPrefabs, null, missingGuids, null, null);
    }
}
