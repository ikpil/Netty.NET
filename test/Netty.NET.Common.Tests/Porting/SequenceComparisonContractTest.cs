using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class SequenceComparisonContractTest
{
    public static IEnumerable<object[]> RepresentationPairs()
    {
        foreach (string culture in new[] { "", "en-US", "tr-TR" })
        for (int left = 0; left < 3; left++)
        for (int right = 0; right < 3; right++)
            yield return new object[] { culture, left, right };
    }

    private static ICharSequence Sequence(string text, int representation) => representation switch
    {
        0 => new StringCharSequence("!!" + text + "!!", 2, text.Length),
        1 => new AppendableCharSequence(1).Append(text),
        _ => new OpaqueSequence(text)
    };

    [Theory]
    [MemberData(nameof(RepresentationPairs))]
    public void UnicodeComparisonsFollowNativeOrdinalRulesForEveryRepresentation(string culture, int leftType, int rightType)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            (string Left, string Right, bool IgnoreCase)[] pairs =
            {
                ("I", "i", true), ("İ", "i", false), ("ı", "I", false),
                ("Σ", "ς", true), ("é", "É", true), ("ß", "ss", false),
                ("é", "e\u0301", false), ("\U00010400", "\U00010428", true),
                ("\ud800", "\ud800", true), ("\ud800", "\ud801", false),
                ("a\0B", "A\0b", true), ("", "", true)
            };
            foreach (var pair in pairs)
            {
                ICharSequence left = Sequence(pair.Left, leftType), right = Sequence(pair.Right, rightType);
                Assert.Equal(string.Equals(pair.Left, pair.Right, StringComparison.Ordinal), left.ContentEquals(right));
                Assert.Equal(pair.IgnoreCase, left.ContentEqualsIgnoreCase(right));
                Assert.Equal(pair.IgnoreCase, right.ContentEqualsIgnoreCase(left));
                if (pair.Left.Length == pair.Right.Length)
                {
                    Assert.Equal(pair.IgnoreCase, left.RegionMatchesIgnoreCase(0, right, 0, left.Count));
                    Assert.Equal(pair.IgnoreCase, AsciiString.RegionMatches(left, true, 0, right, 0, left.Count));
                }
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(-1, 0, 1, false)]
    [InlineData(0, -1, 1, false)]
    [InlineData(0, 0, 4, false)]
    [InlineData(3, 3, 0, true)]
    [InlineData(4, 0, 0, false)]
    [InlineData(0, 0, -1, true)]
    [InlineData(4, 4, -1, true)]
    [InlineData(0, 0, int.MinValue, true)]
    [InlineData(int.MaxValue, int.MaxValue, int.MinValue, true)]
    [InlineData(int.MaxValue, 0, 1, false)]
    [InlineData(0, 0, int.MaxValue, false)]
    public void RegionBoundsRetainTheBridgeRulesWithoutOffsetOverflow(int leftStart, int rightStart, int length, bool expected)
    {
        for (int leftType = 0; leftType < 3; leftType++)
        for (int rightType = 0; rightType < 3; rightType++)
        {
            ICharSequence left = Sequence("abc", leftType), right = Sequence("abc", rightType);
            Assert.Equal(expected, left.RegionMatches(leftStart, right, rightStart, length));
            Assert.Equal(expected, left.RegionMatchesIgnoreCase(leftStart, right, rightStart, length));
            Assert.Equal(expected, AsciiString.RegionMatches(left, true, leftStart, right, rightStart, length));
        }
    }

    [Fact]
    public void AsciiProtocolComparisonFoldsOnlyAZForEveryBytePair()
    {
        var bytes = new AsciiString[256];
        var text = new StringCharSequence[256];
        for (int i = 0; i < 256; i++)
        {
            bytes[i] = new AsciiString(new byte[] { (byte)i });
            text[i] = new StringCharSequence(new string((char)i, 1));
        }

        static int Fold(int value) => value >= 'A' && value <= 'Z' ? value + ('a' - 'A') : value;
        for (int left = 0; left < 256; left++)
        for (int right = 0; right < 256; right++)
        {
            bool expected = Fold(left) == Fold(right);
            Assert.Equal(expected, bytes[left].ContentEqualsIgnoreCase(bytes[right]));
            Assert.Equal(expected, bytes[left].ContentEqualsIgnoreCase(text[right]));
            Assert.Equal(expected, AsciiString.ContentEqualsIgnoreCase(text[left], text[right]));
            Assert.Equal(expected, AsciiString.RegionMatchesAscii(text[left], true, 0, text[right], 0, 1));
        }
        Assert.False(AsciiString.RegionMatchesAscii(new StringCharSequence("I"), true, 0, new StringCharSequence("İ"), 0, 1));
        Assert.False(new AsciiString("I").ContentEqualsIgnoreCase(new StringCharSequence("İ")));
        Assert.False(AsciiString.ContainsIgnoreCase(new StringCharSequence("xÉy"), new StringCharSequence("é")));
        Assert.True(AsciiString.ContainsIgnoreCase(new StringCharSequence("xHEADERy"), new StringCharSequence("header")));
    }

    [Fact]
    public void RegionsRespectSliceBoundariesIncludingSplitSurrogatePairs()
    {
        for (int leftType = 0; leftType < 3; leftType++)
        for (int rightType = 0; rightType < 3; rightType++)
        {
            ICharSequence left = Sequence("x\U00010400y", leftType), right = Sequence("!\U00010428?", rightType);
            Assert.True(left.RegionMatchesIgnoreCase(1, right, 1, 2));
            Assert.False(left.RegionMatches(1, right, 1, 2));
            Assert.True(left.RegionMatchesIgnoreCase(1, right, 1, 1));
            Assert.False(left.RegionMatchesIgnoreCase(2, right, 2, 1));
        }
    }

    [Fact]
    public void NativeHashesUseTheLogicalViewAndAgreeWithTheirEqualityPolicy()
    {
        foreach (string value in new[] { "", "mixedI", "Σς", "\U00010400", "a\ud800\0" })
        {
            var text = new StringCharSequence("xx" + value + "yy", 2, value.Length);
            var same = new StringCharSequence(value);
            var builder = new AppendableCharSequence(1).Append(value + "stale");
            builder.SetLength(value.Length);
            Assert.True(text.Equals(same));
            Assert.Equal(text.GetHashCode(), same.GetHashCode());
            Assert.Equal(StringComparer.Ordinal.GetHashCode(value), text.HashCode(false));
            Assert.Equal(StringComparer.OrdinalIgnoreCase.GetHashCode(value), text.HashCode(true));
            Assert.Equal(text.HashCode(false), builder.HashCode(false));
            Assert.Equal(text.HashCode(true), builder.HashCode(true));
        }
    }

    [Fact]
    public void AppendableBorrowedSpanContainsOnlyCurrentLogicalCharacters()
    {
        var builder = new AppendableCharSequence(16).Append("abcd");
        Assert.Equal("abcd", builder.AsSpan().ToString());
        builder.SetLength(2);
        Assert.Equal("ab", builder.AsSpan().ToString());
        builder.Reset();
        Assert.True(builder.AsSpan().IsEmpty);
        builder.Append("z");
        Assert.Equal("z", builder.AsSpan().ToString());
        Assert.False(builder.ContentEquals(new StringCharSequence("Z")));
        Assert.True(builder.ContentEqualsIgnoreCase(new StringCharSequence("Z")));
    }

    [Fact]
    public void NullContentAndRegionArgumentsHaveExplicitContracts()
    {
        ICharSequence text = Sequence("abc", 0), builder = Sequence("abc", 1);
        Assert.False(text.ContentEquals(null));
        Assert.False(builder.ContentEqualsIgnoreCase(null));
        Assert.False(AsciiString.RegionMatches(null, true, 0, text, 0, 1));
        Assert.False(AsciiString.RegionMatches(text, true, 0, null, 0, 1));
        Assert.Throws<ArgumentNullException>(() => text.RegionMatches(0, null, 0, 1));
        Assert.Throws<ArgumentNullException>(() => builder.RegionMatchesIgnoreCase(0, null, 0, 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContentEqualityIsCaseSensitiveAcrossRepresentations(bool appendable)
    {
        ICharSequence left = appendable ? new AppendableCharSequence(1).Append("Abcd") : new StringCharSequence("Abcd");
        Assert.False(left.ContentEquals(new StringCharSequence("aBCD")));
        Assert.False(left.ContentEquals(new AsciiString("aBCD")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IgnoreCaseEqualityDoesNotDependOnTurkishCulture(bool appendable)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            ICharSequence left = appendable ? new AppendableCharSequence(1).Append("I") : new StringCharSequence("I");
            Assert.True(left.ContentEqualsIgnoreCase(new StringCharSequence("i")));
            Assert.True(left.RegionMatchesIgnoreCase(0, new StringCharSequence("i"), 0, 1));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IgnoreCaseEqualityMatchesFinalSigma(bool appendable)
    {
        ICharSequence left = appendable ? new AppendableCharSequence(1).Append("Σ") : new StringCharSequence("Σ");
        Assert.True(left.ContentEqualsIgnoreCase(new StringCharSequence("ς")));
    }

    [Fact]
    public void ObjectEqualityIsSymmetricAndKeepsDifferentSequenceTypesAsDistinctKeys()
    {
        object text = new StringCharSequence("same");
        object bytes = new AsciiString("same");
        object builder = new AppendableCharSequence(1).Append("same");
        Assert.False(text.Equals(bytes));
        Assert.False(bytes.Equals(text));
        Assert.False(text.Equals(builder));
        Assert.False(builder.Equals(text));
        var keys = new Dictionary<object, int> { [text] = 1, [bytes] = 2, [builder] = 3 };
        Assert.Equal(3, keys.Count);
        Assert.True(((StringCharSequence)text).ContentEquals((ICharSequence)bytes));
        Assert.True(((StringCharSequence)text).ContentEquals((ICharSequence)builder));
    }

    // Deliberately exposes only indexed UTF-16 content. Comparison must not materialize strings.
    private sealed class OpaqueSequence : ICharSequence
    {
        private readonly string text;
        public OpaqueSequence(string text) => this.text = text;
        public int Count => text.Length;
        public char this[int index] => text[index];
        public char CharAt(int index) => this[index];
        public int Length() => Count;
        public bool RegionMatches(int thisStart, ICharSequence other, int start, int length) =>
            CharUtil.RegionMatches(this, thisStart, other, start, length);
        public bool RegionMatchesIgnoreCase(int thisStart, ICharSequence other, int start, int length) =>
            CharUtil.RegionMatchesIgnoreCase(this, thisStart, other, start, length);
        public bool ContentEquals(ICharSequence other) => AsciiString.ContentEquals(this, other);
        public bool ContentEqualsIgnoreCase(ICharSequence other) =>
            other != null && Count == other.Count && CharUtil.RegionMatchesIgnoreCase(this, 0, other, 0, Count);
        public ICharSequence SubSequence(int start, int end) => throw new NotSupportedException();
        public ICharSequence SubSequence(int start) => throw new NotSupportedException();
        public int IndexOf(char ch, int start = 0) => throw new NotSupportedException();
        public int HashCode(bool ignoreCase) => throw new NotSupportedException();
        public string ToString(int start) => throw new NotSupportedException();
        public override string ToString() => throw new NotSupportedException();
        public IEnumerator<char> GetEnumerator() => throw new NotSupportedException();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
