using System;
using System.Collections.Generic;
using System.Linq;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.Model;
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
            IEnumerable<MaterialSettingsState> materialSettings = null)
        {
            if (avatarRoot == null) throw new ArgumentNullException(nameof(avatarRoot));

            // EnsureRoot (not FindRoot): every commit needs a stable avatarGuid,
            // even the very first one before any container has been created.
            var configRoot = ContainerManager.EnsureRoot(avatarRoot);
            var avatarGuid = configRoot.GetComponent<AvatarVcsRoot>().AvatarGuid;

            var containers = ContainerManager.GetContainers(configRoot)
                .Select(container => ContainerCapture.CaptureContainer(container, avatarRoot.transform))
                .ToList();
            var avatarReferencesList = avatarReferences?.ToList() ?? new List<AvatarReferenceState>();
            var materialSettingsList = materialSettings?.ToList() ?? new List<MaterialSettingsState>();

            // Design doc 6.3: record every referenced asset's content hash
            // so a later checkout can warn if it's since changed in place.
            var referencedGuids = containers.SelectMany(c => c.prefabGuids)
                .Concat(materialSettingsList.Select(m => m.sourceMaterialGuid))
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
