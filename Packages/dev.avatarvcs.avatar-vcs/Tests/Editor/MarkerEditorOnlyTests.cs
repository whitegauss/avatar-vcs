using System.IO;
using System.Linq;
using AvatarVcs.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// The VRChat SDK's avatar validation is a whitelist of type names, so
    /// every component this package puts on an avatar counts as illegal, and
    /// the build tab's "Auto Fix" destroys the lot -- taking AvatarVcsRoot's
    /// avatarGuid, and with it the key to the stored history. Implementing
    /// IAvatarVcsEditorOnly is what keeps them off that list.
    ///
    /// Note what CI can and cannot see here: TestProject has no VRChat SDK, so
    /// AVATARVCS_VRCSDK3_AVATARS is never defined in these runs and the branch
    /// where IAvatarVcsEditorOnly derives from VRC.SDKBase.IEditorOnly is not
    /// compiled at all. What is checkable is that every component implements
    /// the interface, and that the asmdef still carries the versionDefine that
    /// turns the SDK branch on -- delete that line and the protection is gone
    /// with nothing else to notice.
    /// </summary>
    public class MarkerEditorOnlyTests
    {
        private const string RuntimeAsmdefPath = "Packages/dev.avatarvcs.avatar-vcs/Runtime/AvatarVcs.Runtime.asmdef";

        [Test]
        public void EveryComponentInTheRuntimeAssembly_IsMarkedEditorOnly()
        {
            var components = typeof(AvatarVcsRoot).Assembly.GetTypes()
                .Where(t => typeof(MonoBehaviour).IsAssignableFrom(t) && !t.IsAbstract)
                .ToList();

            // Spelled out rather than "whatever the assembly holds" so that a
            // new marker component makes this test fail and its author has to
            // decide about the SDK whitelist on purpose.
            CollectionAssert.AreEquivalent(
                new[]
                {
                    typeof(AvatarVcsRoot),
                    typeof(AvatarVcsContainer),
                    typeof(AvatarVcsTrackedReference),
                    typeof(AvatarVcsUntracked),
                },
                components,
                "AvatarVcs.Runtime gained or lost a component; it needs the same editor-only treatment as the rest");

            foreach (var component in components)
            {
                Assert.IsTrue(typeof(IAvatarVcsEditorOnly).IsAssignableFrom(component),
                    $"{component.Name} sits on the user's avatar, so the SDK's Auto Fix will destroy it unless it "
                    + "implements IAvatarVcsEditorOnly");
            }
        }

        [Test]
        public void RuntimeAsmdef_StillDefinesTheVrcsdkSymbol()
        {
            var path = Path.GetFullPath(RuntimeAsmdefPath);
            Assert.IsTrue(File.Exists(path), $"'{RuntimeAsmdefPath}' should exist");

            var asmdef = File.ReadAllText(path);

            StringAssert.Contains("com.vrchat.avatars", asmdef,
                "the versionDefine keyed on the SDK package is what enables the IEditorOnly branch");
            StringAssert.Contains("AVATARVCS_VRCSDK3_AVATARS", asmdef,
                "IAvatarVcsEditorOnly derives from VRC.SDKBase.IEditorOnly only under this symbol, and no CI run "
                + "compiles that branch -- this assertion is the only thing guarding it");
        }
    }
}
