using AvatarVcs.Core.Naming;
using NUnit.Framework;

namespace AvatarVcs.Tests.Core
{
    /// <summary>
    /// KAN-76: this predicate gates the only AssetDatabase.DeleteAsset call in
    /// the package, and its input is a GUID out of commit JSON -- which is
    /// hand-editable and merge-corruptible. A false positive destroys a file
    /// the user authored, so the false-positive classes are pinned here.
    /// </summary>
    [Category("Core")]
    public class GeneratedAssetNamingTests
    {
        [Test]
        public void DuplicateName_AppendsTheSuffixTheGuardLooksFor()
        {
            Assert.AreEqual("Coat" + GeneratedAssetNaming.Suffix, GeneratedAssetNaming.DuplicateName("Coat"));
            Assert.IsTrue(GeneratedAssetNaming.LooksGenerated(
                $"Assets/Outfits/{GeneratedAssetNaming.DuplicateName("Coat")}{GeneratedAssetNaming.MaterialExtension}"),
                "what the producer emits must be recognised by the guard");
        }

        // What MaterialSettingsApplier actually produces.
        [TestCase("Assets/Outfits/Coat_avatarvcs.mat")]
        [TestCase("Assets/Outfits/Coat_avatarvcs 1.mat")]
        [TestCase("Assets/Outfits/Coat_avatarvcs 12.mat")]
        [TestCase("Assets/AvatarVCS_Generated/Body_avatarvcs.mat")]
        [TestCase("Assets/AvatarVCS_Generated/anything.mat")]
        public void LooksGenerated_TrueForWhatWeEmit(string path)
        {
            Assert.IsTrue(GeneratedAssetNaming.LooksGenerated(path));
        }

        // The producer only ever writes a Material. Anything else carrying the
        // suffix belongs to the user (KAN-76: the old guard ignored extensions
        // entirely and would have deleted all of these).
        [TestCase("Assets/Outfits/Hair_avatarvcs.prefab")]
        [TestCase("Assets/Rigs/Rig_avatarvcs.asset")]
        [TestCase("Assets/Anim/Body_avatarvcs.controller")]
        [TestCase("Assets/AvatarVCS_Generated/notes.txt")]
        public void LooksGenerated_FalseForNonMaterialsEvenWithTheSuffix(string path)
        {
            Assert.IsFalse(GeneratedAssetNaming.LooksGenerated(path));
        }

        // AssetDatabase.GUIDToAssetPath resolves folder GUIDs too, and
        // DeleteAsset removes a folder *recursively* -- the worst outcome a
        // corrupt generatedAssets entry could reach.
        [TestCase("Assets/Stuff_avatarvcs")]
        [TestCase("Assets/AvatarVCS_Generated")]
        [TestCase("Assets/AvatarVCS_Generated/SubFolder")]
        public void LooksGenerated_FalseForFolders(string path)
        {
            Assert.IsFalse(GeneratedAssetNaming.LooksGenerated(path));
        }

        // Pre-existing expectations from GeneratedAssetGCTests -- the suffix is
        // anchored to the end, so a user file that merely contains it is safe.
        [TestCase("Assets/Outfits/Coat_avatarvcs_backup.mat")]
        [TestCase("Assets/Outfits/clip_avatarvcs v2.mat")]
        [TestCase("Assets/Outfits/UserOwned.mat")]
        [TestCase("Assets/Outfits/Coat_AvatarVCS.mat")] // case-sensitive: we always write lowercase
        public void LooksGenerated_FalseForUserAssetsThatOnlyResembleOurs(string path)
        {
            Assert.IsFalse(GeneratedAssetNaming.LooksGenerated(path));
        }

        [Test]
        public void LooksGenerated_FalseForNullOrEmpty()
        {
            Assert.IsFalse(GeneratedAssetNaming.LooksGenerated(null));
            Assert.IsFalse(GeneratedAssetNaming.LooksGenerated(""));
        }

        [Test]
        public void LooksGenerated_NormalisesBackslashesBeforeMatchingTheGeneratedFolder()
        {
            Assert.IsTrue(GeneratedAssetNaming.LooksGenerated(@"Assets\AvatarVCS_Generated\Body_avatarvcs.mat"));
        }
    }
}
