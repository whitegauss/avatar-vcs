using AvatarVcs.Core.Presentation;
using UnityEditor;

namespace AvatarVcs.Editor.UI
{
    /// <summary>
    /// IUserPrompt backed by EditorUtility.DisplayDialog. KAN-21 phase 4-4.
    /// </summary>
    public sealed class EditorUserPrompt : IUserPrompt
    {
        public bool Confirm(string title, string body, string ok, string cancel) =>
            EditorUtility.DisplayDialog(title, body, ok, cancel);

        public void Alert(string title, string body) =>
            EditorUtility.DisplayDialog(title, body, "OK");
    }
}
