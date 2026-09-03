using AvatarVcs.Core.Diff;
using AvatarVcs.Core.Model;
using NUnit.Framework;

namespace AvatarVcs.Tests.Core
{
    [Category("Core")]
    public class DiffRowFormatterTests
    {
        [TestCase(DiffKind.Added, "+", DiffTone.Added)]
        [TestCase(DiffKind.Removed, "-", DiffTone.Removed)]
        [TestCase(DiffKind.Changed, "~", DiffTone.Changed)]
        [TestCase(DiffKind.Unchanged, "=", DiffTone.Neutral)]
        public void SymbolAndTone_MatchTheKind(DiffKind kind, string symbol, DiffTone tone)
        {
            Assert.AreEqual(symbol, DiffRowFormatter.Symbol(kind));
            Assert.AreEqual(tone, DiffRowFormatter.ToneOf(kind));
        }

        [Test]
        public void RowLabel_PrefixesTheSymbol_AndTagsUnchanged()
        {
            Assert.AreEqual("+ hair",
                DiffRowFormatter.RowLabel(new ContainerDiff { kind = DiffKind.Added, containerId = "hair" }));
            Assert.AreEqual("= hair (unchanged)",
                DiffRowFormatter.RowLabel(new ContainerDiff { kind = DiffKind.Unchanged, containerId = "hair" }));
        }
    }
}
