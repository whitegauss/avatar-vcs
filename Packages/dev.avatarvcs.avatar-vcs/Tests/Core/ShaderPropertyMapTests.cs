using AvatarVcs.Core.MaterialSettings;
using NUnit.Framework;

namespace AvatarVcs.Tests.Core
{
    [Category("Core")]
    public class ShaderPropertyMapTests
    {
        [TestCase("lilToon")]
        [TestCase(".poiyomi/Poiyomi")]
        [TestCase("VRM/MToon")]
        [TestCase("VRM10/MToon10")]
        public void IsSupported_TrueForEachAllowlistedShader(string shaderName)
        {
            Assert.IsTrue(ShaderPropertyMap.IsSupported(shaderName));
        }

        [TestCase("Standard")]
        [TestCase("poiyomi/Poiyomi")] // missing the leading dot of the registered name
        [TestCase("")]
        [TestCase("Universal Render Pipeline/Lit")]
        public void IsSupported_FalseForAnythingElse(string shaderName)
        {
            Assert.IsFalse(ShaderPropertyMap.IsSupported(shaderName));
        }
    }
}
