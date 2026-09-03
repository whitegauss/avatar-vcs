using System.Collections.Generic;
using AvatarVcs.Core.History;
using AvatarVcs.Core.Model;
using NUnit.Framework;

namespace AvatarVcs.Tests.Core
{
    [Category("Core")]
    public class GuidRemapResolverTests
    {
        [Test]
        public void Resolve_FollowsAChainToItsEnd()
        {
            var index = new Dictionary<string, string> { ["A"] = "B", ["B"] = "C" };
            var r = GuidRemapResolver.Resolve(index, "A");
            Assert.AreEqual("C", r.Guid);
            Assert.IsFalse(r.CycleDetected);
        }

        [Test]
        public void Resolve_UnregisteredGuid_ReturnsItselfUnchanged()
        {
            var r = GuidRemapResolver.Resolve(new Dictionary<string, string> { ["A"] = "B" }, "Z");
            Assert.AreEqual("Z", r.Guid);
            Assert.IsFalse(r.CycleDetected);
        }

        [Test]
        public void Resolve_DirectCycle_IsDetected()
        {
            var index = new Dictionary<string, string> { ["A"] = "B", ["B"] = "A" };
            var r = GuidRemapResolver.Resolve(index, "A");
            Assert.IsTrue(r.CycleDetected);
        }

        [Test]
        public void Resolve_SelfMapping_IsACycle()
        {
            var r = GuidRemapResolver.Resolve(new Dictionary<string, string> { ["A"] = "A" }, "A");
            Assert.IsTrue(r.CycleDetected);
        }

        [Test]
        public void Resolve_NullOrEmptyGuid_IsReturnedAsIs()
        {
            Assert.IsNull(GuidRemapResolver.Resolve(new Dictionary<string, string>(), null).Guid);
            Assert.AreEqual("", GuidRemapResolver.Resolve(new Dictionary<string, string>(), "").Guid);
        }

        [Test]
        public void BuildIndex_SkipsNullAndBlankEntries_AndKeepsFirstOnDuplicateSource()
        {
            var config = new GuidRemapConfig();
            config.mappings.Add(null);
            config.mappings.Add(new GuidRemapEntry { oldGuid = "", newGuid = "x" });
            config.mappings.Add(new GuidRemapEntry { oldGuid = "A", newGuid = "" });
            config.mappings.Add(new GuidRemapEntry { oldGuid = "A", newGuid = "B" });
            config.mappings.Add(new GuidRemapEntry { oldGuid = "A", newGuid = "SHOULD-BE-IGNORED" });

            var index = GuidRemapResolver.BuildIndex(config);

            Assert.AreEqual(1, index.Count);
            Assert.AreEqual("B", index["A"], "first well-formed mapping for a source wins");
        }
    }
}
