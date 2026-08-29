using System;
using System.Collections.Generic;

namespace AvatarVcs.Core.Model
{
    [Serializable]
    public class BranchEntry
    {
        public string name;
        public string commitId;
    }

    /// <summary>
    /// Design doc section 2.2. branches is a List rather than a Dictionary
    /// because JsonUtility (used throughout this package to avoid a
    /// Newtonsoft.Json dependency) cannot serialize dictionaries.
    /// </summary>
    [Serializable]
    public class BranchConfig
    {
        public List<BranchEntry> branches = new();
        public string currentBranch = "main";
    }
}
