using System;

namespace AvatarVcs.Editor.Model
{
    [Serializable]
    public class AssetRef
    {
        public string key;
        public string guid;
        public long localId;
        public bool sensitive;
    }
}
