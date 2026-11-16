using System;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

// Expected values were printed by the unmodified pinned AppendableCharSequence and java.util.Arrays.copyOfRange on
// JDK 21. ArgumentOutOfRangeException stands for IndexOutOfBoundsException (its subclasses raised by array/String
// copies carry a parameter name), IndexOutOfRangeException for a direct array index failure and ArgumentException
// for IllegalArgumentException.
public class AppendableCharSequenceBoundaryContractTest
{
    // capacity 8, "abcd", reset, 'z': the logical length is 1 and the buffer still holds "zbcd\0\0\0\0".
    private static AppendableCharSequence Rewritten()
    {
        var sequence = new AppendableCharSequence(8).Append("abcd");
        sequence.Reset();
        sequence.Append('z');
        return sequence;
    }

    [Fact]
    public void CharAtOnlyRejectsIndexesAboveLength()
    {
        AppendableCharSequence sequence = Rewritten();
        Assert.Equal('z', sequence.CharAt(0));
        Assert.Equal('b', sequence.CharAt(1));
        Assert.Equal('b', sequence[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => sequence.CharAt(2));
        Assert.Throws<IndexOutOfRangeException>(() => sequence.CharAt(-1));

        var full = new AppendableCharSequence(4).Append("abcd");
        Assert.Throws<IndexOutOfRangeException>(() => full.CharAt(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => full.CharAt(5));
    }

    [Fact]
    public void SubSequenceCopiesTheBufferRangeLikeArraysCopyOfRange()
    {
        Assert.Equal("zb", Rewritten().SubSequence(0, 2).ToString());
        Assert.Equal("cd\0\0\0\0", Rewritten().SubSequence(2, 8).ToString());
        Assert.Equal("zbcd\0\0\0\0\0", Rewritten().SubSequence(0, 9).ToString());
        Assert.Equal(0, Rewritten().SubSequence(-1, -1).Length());
        Assert.Equal(0, Rewritten().SubSequence(9, 9).Length());
        Assert.Equal("3 > 2", Assert.Throws<ArgumentException>(() => Rewritten().SubSequence(3, 2)).Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => Rewritten().SubSequence(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rewritten().SubSequence(9, 10));
    }

    [Fact]
    public void SubstringChecksStartAndLengthAgainstLength()
    {
        Assert.Equal("z", Rewritten().Substring(0, 1));
        var e = Assert.Throws<ArgumentOutOfRangeException>(() => Rewritten().Substring(0, 2));
        Assert.Null(e.ParamName);
        Assert.Equal("expected: start and length <= (1)", e.Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => Rewritten().Substring(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rewritten().Substring(1, 0));
    }

    [Fact]
    public void AppendRangeOnlyChecksTheEndAgainstTheSourceLength()
    {
        var e = Assert.Throws<ArgumentOutOfRangeException>(
            () => new AppendableCharSequence(2).Append("xy").Append(new StringCharSequence("abcd"), 2, 8));
        Assert.Null(e.ParamName);
        Assert.Equal("expected: csq.length() >= (8),but actual is (4)", e.Message);

        AppendableCharSequence reversed = new AppendableCharSequence(2).Append("xy").Append(new StringCharSequence("abcd"), 3, 2);
        Assert.Equal("xy", reversed.ToString());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AppendableCharSequence(2).Append("xy").Append(new AppendableCharSequence(8).Append("abcd"), 3, 2));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AppendableCharSequence(2).Append("xy").Append(new AppendableCharSequence(8).Append("abcd"), -1, -1));
    }

    [Fact]
    public void CopyOfRangePadsAndValidatesLikeTheJdk()
    {
        char[] chars = { 'a', 'b', 'c', 'd' };
        Assert.Equal("cd\0\0", new string(Arrays.CopyOfRange(chars, 2, 6)));
        Assert.Equal("\0", new string(Arrays.CopyOfRange(chars, 4, 5)));
        Assert.Equal("", new string(Arrays.CopyOfRange(chars, 4, 4)));
        Assert.Equal("3 > 1", Assert.Throws<ArgumentException>(() => Arrays.CopyOfRange(chars, 3, 1)).Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => Arrays.CopyOfRange(chars, 5, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Arrays.CopyOfRange(chars, -1, -1));
    }
}
