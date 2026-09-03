using System;
using AvatarVcs.Core.History;
using NUnit.Framework;

namespace AvatarVcs.Tests.Core
{
    /// <summary>
    /// The path-traversal defense boundary: only a 32-char lowercase hex
    /// string (Guid.NewGuid().ToString("N")) may reach path interpolation.
    /// </summary>
    [Category("Core")]
    public class CommitIdentifierTests
    {
        [Test]
        public void IsValidShape_AcceptsA32CharLowercaseHexString()
        {
            Assert.IsTrue(CommitIdentifier.IsValidShape("0123456789abcdef0123456789abcdef"));
            Assert.IsTrue(CommitIdentifier.IsValidShape(Guid.NewGuid().ToString("N")));
        }

        [Test]
        public void IsValidShape_RejectsWrongLengthNullTraversalUppercaseAndNonHex()
        {
            Assert.IsFalse(CommitIdentifier.IsValidShape(null));
            Assert.IsFalse(CommitIdentifier.IsValidShape(""));
            Assert.IsFalse(CommitIdentifier.IsValidShape("abc"), "too short");
            Assert.IsFalse(CommitIdentifier.IsValidShape("0123456789abcdef0123456789abcdef0"), "too long");
            Assert.IsFalse(CommitIdentifier.IsValidShape("0123456789ABCDEF0123456789ABCDEF"), "uppercase");
            Assert.IsFalse(CommitIdentifier.IsValidShape("../../../etc/passwd_padding_aaaa"), "path traversal");
            Assert.IsFalse(CommitIdentifier.IsValidShape("0123456789abcdef0123456789abcde/"), "slash");
            Assert.IsFalse(CommitIdentifier.IsValidShape("0123456789abcdef0123456789abcdeg"), "non-hex letter");
        }

        [Test]
        public void EnsureValid_ThrowsArgumentExceptionNamingTheParameter()
        {
            var ex = Assert.Throws<ArgumentException>(() => CommitIdentifier.EnsureValid("../bad", "commitId"));
            Assert.AreEqual("commitId", ex.ParamName);
        }

        [Test]
        public void EnsureValid_DoesNotThrowForAValidShape()
        {
            Assert.DoesNotThrow(() => CommitIdentifier.EnsureValid(Guid.NewGuid().ToString("N"), "avatarGuid"));
        }
    }
}
