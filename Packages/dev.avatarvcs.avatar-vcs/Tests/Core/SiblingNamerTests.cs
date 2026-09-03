using System.Collections.Generic;
using AvatarVcs.Core.Naming;
using NUnit.Framework;

namespace AvatarVcs.Tests.Core
{
    [Category("Core")]
    public class SiblingNamerTests
    {
        [Test]
        public void MakeUnique_ReturnsTheBaseNameWhenItIsFree()
        {
            Assert.AreEqual("hair", SiblingNamer.MakeUnique(new HashSet<string> { "body", "armature" }, "hair"));
        }

        [Test]
        public void MakeUnique_AppendsUnderscoreOneOnFirstCollision()
        {
            Assert.AreEqual("hair_1", SiblingNamer.MakeUnique(new HashSet<string> { "hair" }, "hair"));
        }

        [Test]
        public void MakeUnique_KeepsCountingWhenTheSuffixedNameAlsoCollides()
        {
            Assert.AreEqual("hair_3",
                SiblingNamer.MakeUnique(new HashSet<string> { "hair", "hair_1", "hair_2" }, "hair"));
        }
    }
}
