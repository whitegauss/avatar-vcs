using System;
using System.Collections.Generic;

namespace AvatarVcs.Editor.Model
{
    /// <summary>
    /// Records the content hash of a referenced asset at commit time, so
    /// checkout can warn when it has since changed (design doc section 6.3).
    /// Doesn't block restore -- the tool has no asset backup, so the goal is
    /// just to explain why the result may look different, not to prevent it.
    /// </summary>
    [Serializable]
    public class AssetVersionEntry
    {
        public string guid;
        public string assetName;
        public string contentHash;
        public string recordedAt;
    }
}
