using System;

namespace AvatarVcs.Editor.Model
{
    [Serializable]
    public class FieldValue
    {
        public string key;
        public string value;
        public string type;
        public bool sensitive;
    }
}
