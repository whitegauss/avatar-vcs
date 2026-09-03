using System;
using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Core.Diagnostics;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.Diagnostics;
using AvatarVcs.Core.Model;
using AvatarVcs.Editor.Operations;
using AvatarVcs.Runtime;
using UnityEngine;

namespace AvatarVcs.Editor.History
{
    /// <summary>
    /// Builds a Commit from the avatar's current live state. Design doc
    /// section 3.1.
    /// </summary>
    public static class CommitBuilder
    {
        public static Commit CreateCommit(
            GameObject avatarRoot,
            string message,
            string branch,
            string parentCommitId,
            IEnumerable<AvatarReferenceState> avatarReferences = null,
            IEnumerable<MaterialSettingsState> materialSettings = null,
            DiagnosticLog log = null)
        {
            if (avatarRoot == null) throw new ArgumentNullException(nameof(avatarRoot));

            // KAN-20: a caller mid-operation passes its own DiagnosticLog for
            // the per-container capture warnings; a direct caller (tests)
            // passes none, so make one and flush it here.
            var ownsLog = log == null;
            log ??= new DiagnosticLog();
            try
            {
                return CreateCommitCore(avatarRoot, message, branch, parentCommitId, avatarReferences, materialSettings, log);
            }
            finally
            {
                if (ownsLog) UnityDiagnosticSink.Flush(log);
            }
        }

        private static Commit CreateCommitCore(
            GameObject avatarRoot,
            string message,
            string branch,
            string parentCommitId,
            IEnumerable<AvatarReferenceState> avatarReferences,
            IEnumerable<MaterialSettingsState> materialSettings,
            DiagnosticLog log)
        {
            // EnsureRoot (not FindRoot): every commit needs a stable avatarGuid,
            // even the very first one before any container has been created.
            var configRoot = ContainerManager.EnsureRoot(avatarRoot);
            var avatarGuid = configRoot.GetComponent<AvatarVcsRoot>().AvatarGuid;

            ContainerManager.AdoptLoosePrefabInstancesAsContainers(configRoot);
            ContainerManager.ValidateContainers(configRoot);

            var containers = ContainerManager.GetContainers(configRoot)
                .Select(container => ContainerCapture.CaptureContainer(container, avatarRoot.transform, log))
                .ToList();
            var avatarReferencesList = avatarReferences?.ToList() ?? new List<AvatarReferenceState>();
            var materialSettingsList = materialSettings?.ToList() ?? new List<MaterialSettingsState>();

            // Design doc 6.3: record every referenced asset's content hash
            // so a later checkout can warn if it's since changed in place.
            var referencedGuids = containers.SelectMany(c => c.prefabGuids)
                .Concat(materialSettingsList.Select(m => m.sourceMaterialGuid))
                .Concat(containers.SelectMany(c => c.materialSettings.Select(m => m.sourceMaterialGuid))) // KAN-73
                .Concat(avatarReferencesList.SelectMany(r => r.materials.Select(m => m.guid)));

            return new Commit
            {
                commitId = Guid.NewGuid().ToString("N"),
                parentCommitId = parentCommitId,
                branch = branch,
                message = message,
                timestamp = DateTime.UtcNow.ToString("o"),
                avatarGuid = avatarGuid,
                avatarName = avatarRoot.name,
                containers = containers,
                avatarReferences = avatarReferencesList,
                materialSettings = materialSettingsList,
                assetVersions = AssetVersionChecker.RecordVersions(referencedGuids),
            };
        }
    }
}
