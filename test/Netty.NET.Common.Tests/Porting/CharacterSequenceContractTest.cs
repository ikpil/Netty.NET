using System;
using System.Linq;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class CharacterSequenceContractTest
{
    [Fact]
    public void JavaSubstringUsesAnExclusiveEnd()
    {
        Assert.Equal("bc", "abcd"[1..3]);
        Assert.Equal("", "abcd"[2..2]);
        Assert.Equal("cd", "abcd"[2..]);
    }

    [Fact]
    public void StringSlicesUseRelativeOffsetsAndLogicalLength()
    {
        var slice = new StringCharSequence("xxabcdyy", 2, 4);
        Assert.Equal("abcd", slice.ToString());
        Assert.Equal('a', slice.CharAt(0));
        Assert.Equal("cd", slice.ToString(2));
        Assert.Equal("", slice.ToString(4));
        Assert.Equal("bc", slice.SubSequence(1, 3).ToString());
        Assert.Equal(2, slice.IndexOf('c'));
        Assert.Equal(-1, slice.IndexOf('y'));
        int found = slice.AsSpan().Slice(1).IndexOf("cd".AsSpan(), StringComparison.Ordinal);
        Assert.Equal(2, found < 0 ? -1 : 1 + found);
        Assert.Equal(-1, slice.AsSpan().Slice(1).IndexOf("a".AsSpan(), StringComparison.Ordinal));
        Assert.Throws<ArgumentOutOfRangeException>(() => slice.CharAt(4));
    }

    [Fact]
    public void AppendableExtensionsObserveLengthAfterResetAndAppend()
    {
        var sequence = new AppendableCharSequence(2).Append("Abcd");
        Assert.Equal("bcd", sequence.SubSequence(1).ToString());
        Assert.Equal("cd", sequence.ToString(2));
        Assert.Equal(2, sequence.IndexOf('c'));
        Assert.True(sequence.ContentEquals(new StringCharSequence("Abcd")));
        Assert.True(sequence.ContentEqualsIgnoreCase(new StringCharSequence("aBCD")));
        Assert.True(sequence.RegionMatches(1, new StringCharSequence("xbc"), 1, 2));
        Assert.Equal("Abcd".ToCharArray(), sequence.ToArray());
        sequence.Reset();
        sequence.Append('z');
        Assert.Equal(-1, sequence.IndexOf('b'));
        Assert.Throws<ArgumentOutOfRangeException>(() => sequence.Substring(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => sequence.SubSequence(-1, 0));
    }
}
