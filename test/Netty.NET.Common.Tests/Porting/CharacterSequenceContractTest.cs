using System;
using System.Linq;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class CharacterSequenceContractTest
{
    [Fact]
    public void JavaSubstringUsesAnExclusiveEnd()
    {
        Assert.Equal("bc", "abcd".substring(1, 3));
        Assert.Equal("", "abcd".substring(2, 2));
        Assert.Equal("cd", "abcd".substring(2));
    }

    [Fact]
    public void StringSlicesUseRelativeOffsetsAndLogicalLength()
    {
        var slice = new StringCharSequence("xxabcdyy", 2, 4);
        Assert.Equal("abcd", slice.ToString());
        Assert.Equal('a', slice.charAt(0));
        Assert.Equal("cd", slice.ToString(2));
        Assert.Equal("", slice.ToString(4));
        Assert.Equal("bc", slice.subSequence(1, 3).ToString());
        Assert.Equal(2, slice.indexOf('c'));
        Assert.Equal(-1, slice.indexOf('y'));
        Assert.Equal(2, slice.indexOf("cd", 1));
        Assert.Equal(-1, slice.indexOf("a", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => slice.charAt(4));
    }

    [Fact]
    public void AppendableExtensionsObserveLengthAfterResetAndAppend()
    {
        var sequence = new AppendableCharSequence(2).append("Abcd");
        Assert.Equal("bcd", sequence.subSequence(1).ToString());
        Assert.Equal("cd", sequence.ToString(2));
        Assert.Equal(2, sequence.indexOf('c'));
        Assert.True(sequence.contentEquals(new StringCharSequence("Abcd")));
        Assert.True(sequence.contentEqualsIgnoreCase(new StringCharSequence("aBCD")));
        Assert.True(sequence.regionMatches(1, new StringCharSequence("xbc"), 1, 2));
        Assert.Equal("Abcd".ToCharArray(), sequence.ToArray());
        sequence.reset();
        sequence.append('z');
        Assert.Equal(-1, sequence.indexOf('b'));
        Assert.Throws<ArgumentOutOfRangeException>(() => sequence.substring(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => sequence.subSequence(-1, 0));
    }
}
