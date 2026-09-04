using AvatarVcs.Core.MaterialSettings;
using NUnit.Framework;

namespace AvatarVcs.Tests.Core
{
    [Category("Core")]
    public class ShaderPropertyMapTests
    {
        // Names taken from an actual jp.lilxyzw.liltoon install, which
        // registers 64 shaders. The allowlist used to hold the single exact
        // string "lilToon", so every one of the other 63 was rejected -- and a
        // real avatar's materials are almost always variants. The two marked
        // below are the ones from the user report that started this: a whole
        // avatar recorded no shader settings at all, silently.
        [TestCase("lilToon")]
        [TestCase("Hidden/lilToonOutline")]        // user report: Body_base.mat, Face.mat
        [TestCase("Hidden/lilToonTransparent")]    // user report: Face_transparent.mat
        [TestCase("Hidden/lilToonCutout")]
        [TestCase("Hidden/lilToonCutoutOutline")]
        [TestCase("Hidden/lilToonOnePassTransparent")]
        [TestCase("Hidden/lilToonTwoPassTransparentOutline")]
        [TestCase("Hidden/lilToonLiteCutoutOutline")]
        [TestCase("Hidden/lilToonTessellationTransparent")]
        [TestCase("Hidden/lilToonFurCutout")]
        [TestCase("Hidden/lilToonGem")]
        [TestCase("Hidden/lilToonRefractionBlur")]
        [TestCase("Hidden/lilToonMultiOutline")]
        [TestCase("_lil/lilToonMulti")]
        [TestCase("_lil/[Optional] lilToonOverlay")]
        [TestCase("_lil/[Optional] lilToonOutlineOnlyCutout")]
        [TestCase("_lil/[Optional] lilToonFakeShadow")]
        public void IsSupported_TrueForEveryLilToonVariant(string shaderName)
        {
            Assert.IsTrue(ShaderPropertyMap.IsSupported(shaderName));
        }

        [TestCase(".poiyomi/Poiyomi")]
        [TestCase(".poiyomi/Poiyomi Toon")]
        [TestCase(".poiyomi/Poiyomi Pro")]
        [TestCase("Poiyomi/Poiyomi Toon")]
        // Poiyomi renames a locked-in shader into a hashed path; the family
        // name still sits in one of the segments.
        [TestCase("Hidden/Locked/Poiyomi Toon/9f8e7d6c")]
        [TestCase("VRM/MToon")]
        [TestCase("VRM10/MToon10")]
        public void IsSupported_TrueForPoiyomiAndMToonVariants(string shaderName)
        {
            Assert.IsTrue(ShaderPropertyMap.IsSupported(shaderName));
        }

        // lilToon's internal pass/baker shaders. They ship in the same package
        // but no material should reference one, and they carry a different
        // property set -- the family match must not reach them.
        [TestCase("Hidden/ltspass_opaque")]
        [TestCase("Hidden/ltspass_tess_cutout")]
        [TestCase("Hidden/ltspass_lite_transparent")]
        [TestCase("Hidden/ltsother_baker")]
        [TestCase("Hidden/ltsother_bakeramp")]
        public void IsSupported_FalseForLilToonInternalPassShaders(string shaderName)
        {
            Assert.IsFalse(ShaderPropertyMap.IsSupported(shaderName));
        }

        [TestCase("Standard")]
        [TestCase("")]
        [TestCase(null)]
        [TestCase("Universal Render Pipeline/Lit")]
        [TestCase("Unlit/Texture")]
        [TestCase("VRChat/Mobile/Toon Lit")]
        // Case-sensitive on purpose: material.shader.name reproduces the
        // registered name exactly, so a case mismatch means a different shader.
        [TestCase("hidden/liltoonoutline")]
        [TestCase("LILTOON")]
        public void IsSupported_FalseForAnythingElse(string shaderName)
        {
            Assert.IsFalse(ShaderPropertyMap.IsSupported(shaderName));
        }
    }
}
