using System.Collections.Generic;

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
        public bool IsSuccess => Kind == CheckoutResultKind.Success;

        private CheckoutResult(CheckoutResultKind kind, string autoCommitId, List<string> missingPrefabGuids, List<string> versionWarnings)
        {
            Kind = kind;
            AutoCommitId = autoCommitId;
            MissingPrefabGuids = missingPrefabGuids;
            VersionWarnings = versionWarnings ?? new List<string>();
        }

        public static CheckoutResult Success(string autoCommitId, List<string> versionWarnings = null) =>
            new(CheckoutResultKind.Success, autoCommitId, null, versionWarnings);
        public static CheckoutResult MissingPrefabs(List<string> missingGuids) =>
            new(CheckoutResultKind.MissingPrefabs, null, missingGuids, null);
    }
}
