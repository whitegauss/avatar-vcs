using System;
using AvatarVcs.Core.Reflection;
using NUnit.Framework;

namespace AvatarVcs.Tests.Core
{
    [Category("Core")]
    public class ScalarCodecTests
    {
        [Test]
        public void JoinThenParseFloats_RoundTrips()
        {
            var parsed = ScalarCodec.ParseFloats(ScalarCodec.Join(1.5f, -2f, 0f, 3.25f));
            Assert.AreEqual(new[] { 1.5f, -2f, 0f, 3.25f }, parsed);
        }

        [Test]
        public void ParseFloats_UsesInvariantCulture_NotTheMachineLocale()
        {
            // A locale that uses ',' as the decimal separator must not change
            // how "1.5,2.5" parses (',' is the element separator here).
            Assert.AreEqual(new[] { 1.5f, 2.5f }, ScalarCodec.ParseFloats("1.5,2.5"));
        }

        [Test]
        public void ParseFloats_MalformedString_Throws()
        {
            Assert.Throws<FormatException>(() => ScalarCodec.ParseFloats("1,notanumber"));
        }

        [Test]
        public void IsAcceptableArraySize_RejectsNegativeAndAnythingOverTheCap()
        {
            Assert.IsFalse(ScalarCodec.IsAcceptableArraySize(-1));
            Assert.IsTrue(ScalarCodec.IsAcceptableArraySize(0));
            Assert.IsTrue(ScalarCodec.IsAcceptableArraySize(ScalarCodec.MaxArraySize));
            Assert.IsFalse(ScalarCodec.IsAcceptableArraySize(ScalarCodec.MaxArraySize + 1));
        }
    }
}
