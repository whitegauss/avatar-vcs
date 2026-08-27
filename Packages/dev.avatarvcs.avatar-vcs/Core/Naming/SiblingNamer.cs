using System.Collections.Generic;

namespace AvatarVcs.Core.Naming
{
    /// <summary>
    /// Disambiguates a name against a set of names already in use, appending
    /// "_1", "_2", ... until it's unique. Split out of
    /// ContainerManager.MakeUniqueSiblingName so the disambiguation itself
    /// can be tested without a scene; see
    /// ContainerManager.AdoptLoosePrefabInstancesAsContainers for why the
    /// caller must build existingNames at a specific point in its own
    /// sequence (after the child being wrapped is reparented out, before its
    /// new wrapper is reparented in) rather than just any time.
    /// </summary>
    public static class SiblingNamer
    {
        public static string MakeUnique(ISet<string> existingNames, string baseName)
        {
            if (!existingNames.Contains(baseName)) return baseName;

            var i = 1;
            string candidate;
            do
            {
                candidate = $"{baseName}_{i}";
                i++;
            } while (existingNames.Contains(candidate));

            return candidate;
        }
    }
}
