using System.Collections.Generic;
using AvatarVcs.Core.Model;
using AvatarVcs.Core.Presets;
using NUnit.Framework;

namespace AvatarVcs.Tests.Core
{
    [Category("Core")]
    public class BlendShapePresetJsonTests
    {
        [Test]
        public void SerializeThenParse_RoundTrips()
        {
            var preset = new BlendShapePreset { meshName = "Face" };
            preset.blendShapes.Add(new BlendShapeRef { name = "Smile", weight = 42f });

            Assert.IsTrue(BlendShapePresetJson.TryParse(BlendShapePresetJson.Serialize(preset), out var parsed, out var error));
            Assert.IsNull(error);
            Assert.AreEqual("Face", parsed.meshName);
            Assert.AreEqual("Smile", parsed.blendShapes[0].name);
            Assert.AreEqual(42f, parsed.blendShapes[0].weight, 0.0001f);
        }

        [Test]
        public void TryParse_GarbageString_ReturnsFalseWithAnErrorAndDoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                var ok = BlendShapePresetJson.TryParse("{ not json", out var preset, out var error);
                Assert.IsFalse(ok);
                Assert.IsNull(preset);
                Assert.IsNotNull(error);
            });
        }

        [Test]
        public void TryParse_LiteralNull_SucceedsButYieldsANullPresetForTheCallerToCheck()
        {
            var ok = BlendShapePresetJson.TryParse("null", out var preset, out var error);
            Assert.IsTrue(ok);
            Assert.IsNull(error);
            Assert.IsNull(preset);
        }

        [Test]
        public void DescribeImport_MentionsSkippedShapesOnlyWhenThereAreAny()
        {
            Assert.AreEqual("[AvatarVCS] Imported 3 BlendShape(s) from 'p.json'.",
                BlendShapePresetJson.DescribeImport(3, "p.json", new List<string>()));

            var withSkips = BlendShapePresetJson.DescribeImport(2, "p.json", new List<string> { "A", "B" });
            StringAssert.Contains("2 not found on this mesh, skipped: A, B", withSkips);
        }
    }
}
