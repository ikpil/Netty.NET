using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class AsciiStringDelimiterContractTest
{
    [Theory]
    [InlineData(int.MaxValue, false)]
    [InlineData(int.MaxValue - 1, false)]
    [InlineData(int.MaxValue, true)]
    [InlineData(int.MaxValue - 1, true)]
    public void BeyondViewStartCannotOverflowBackingOffset(int start, bool dispatch)
    {
        var value = new AsciiString(new byte[] { 1, 2, (byte)'a', (byte)'b', 3 }, 2, 2, false);
        int actual = dispatch ? AsciiString.indexOf(value, 'a', start) : value.indexOf('a', start);
        Assert.Equal(-1, actual);
    }

    [Fact]
    public void EveryLatin1CharacterAndLogicalStartAgreesWithIndependentScan()
    {
        byte[] backing = new byte[260];
        char[] characters = new char[256];
        for (int i = 0; i < 256; i++)
        {
            backing[i + 2] = (byte)i;
            characters[i] = (char)i;
        }
        var bytes = new AsciiString(backing, 2, 256, false);
        var text = new StringCharSequence("!!" + new string(characters) + "!!", 2, 256);
        int[] starts = { int.MinValue, -1, 0, 1, 127, 255, 256, 257, int.MaxValue - 1, int.MaxValue };
        for (int target = 0; target < 256; target++)
        {
            foreach (int start in starts)
            {
                int expected = -1;
                for (int i = Math.Max(0, start); i < 256; i++)
                {
                    if (characters[i] == target) { expected = i; break; }
                }
                Assert.Equal(expected, bytes.indexOf((char)target, start));
                Assert.Equal(expected, AsciiString.indexOf(bytes, (char)target, start));
                Assert.Equal(expected, text.indexOf((char)target, start));
                Assert.Equal(expected, AsciiString.indexOf(text, (char)target, start));
            }
        }
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(0)]
    [InlineData(int.MaxValue - 1)]
    [InlineData(int.MaxValue)]
    public void EmptyNonzeroOffsetViewNeverSearchesBacking(int start)
    {
        var value = new AsciiString(new byte[] { 1, 2, (byte)'a' }, 2, 0, false);
        Assert.Equal(-1, value.indexOf('a', start));
        Assert.Equal(-1, AsciiString.indexOf(value, 'a', start));
    }

    [Theory]
    [InlineData(0x100)]
    [InlineData(0xffff)]
    public void NonLatin1CharacterIsNeverTruncatedToAByte(int character)
    {
        var value = new AsciiString(new byte[] { 0, 255 });
        Assert.Equal(-1, value.indexOf((char)character, int.MinValue));
        Assert.Equal(-1, AsciiString.indexOf(value, (char)character, 0));
    }

    [Fact]
    public void NativeSearchReadsCurrentBytesAndStaysInsideTheLogicalView()
    {
        byte[] backing = Encoding.ASCII.GetBytes("x!ab!x");
        var value = new AsciiString(backing, 2, 2, false);
        Assert.Equal("ab", value.ToString());
        Assert.Equal(-1, value.indexOf('x', 0));
        backing[3] = (byte)'x';
        Assert.Equal(1, value.indexOf('x', 0));
        Assert.Equal(1, value.AsSpan().IndexOf((byte)'x'));
        value.arrayChanged();
        Assert.Equal("ax", value.ToString());
    }

    [Theory]
    [InlineData("", new string[] { "" })]
    [InlineData("a", new string[] { "a" })]
    [InlineData(",", new string[] { "", "" })]
    [InlineData(",,", new string[] { "", "", "" })]
    [InlineData("a,", new string[] { "a", "" })]
    [InlineData(",a,", new string[] { "", "a", "" })]
    [InlineData("a,,b,,", new string[] { "a", "", "b", "", "" })]
    [InlineData("é,\0,ÿ", new string[] { "é", "\0", "ÿ" })]
    public void NativeByteSplittingPreservesEveryEmptyField(string text, string[] expected)
    {
        var value = new AsciiString(Encoding.Latin1.GetBytes("!!" + text + "!!"), 2, text.Length, false);
        var fields = new List<string>();
        foreach (Range range in value.AsSpan().Split((byte)','))
            fields.Add(Encoding.Latin1.GetString(value.AsSpan()[range]));
        Assert.Equal(expected, fields.ToArray());
    }

    [Fact]
    public void EveryByteSeparatorProducesLogicalRangesWithoutSignedByteConversion()
    {
        for (int separator = 0; separator < 256; separator++)
        {
            byte[] logical = { 255, 0, (byte)separator, 1, (byte)separator };
            byte[] backing = new byte[logical.Length + 4];
            logical.CopyTo(backing, 2);
            var value = new AsciiString(backing, 2, logical.Length, false);
            var expected = new List<(int Start, int Length)>();
            int start = 0;
            for (int i = 0; i < logical.Length; i++)
            {
                if (logical[i] == separator)
                {
                    expected.Add((start, i - start));
                    start = i + 1;
                }
            }
            expected.Add((start, logical.Length - start));
            var actual = new List<(int Start, int Length)>();
            foreach (Range range in value.AsSpan().Split((byte)separator))
                actual.Add(range.GetOffsetAndLength(value.Count));
            Assert.Equal(expected.ToArray(), actual.ToArray());
        }
    }

    [Fact]
    public void SplitRangesLetTheCallerChooseSharedMemoryOrDetachedCopies()
    {
        byte[] backing = Encoding.ASCII.GetBytes("!!ab,cd!!");
        var value = new AsciiString(backing, 2, 5, false);
        ReadOnlyMemory<byte> memory = value.AsMemory();
        var fields = new List<ReadOnlyMemory<byte>>();
        foreach (Range range in memory.Span.Split((byte)','))
        {
            var (start, length) = range.GetOffsetAndLength(memory.Length);
            fields.Add(memory.Slice(start, length));
        }
        Assert.Equal(2, fields.Count);
        Assert.True(MemoryMarshal.TryGetArray(fields[1], out ArraySegment<byte> segment));
        Assert.Same(backing, segment.Array);
        Assert.Equal(5, segment.Offset);
        byte[] detached = fields[1].ToArray();
        backing[5] = (byte)'x';
        Assert.Equal("xd", Encoding.ASCII.GetString(fields[1].Span));
        Assert.Equal("cd", Encoding.ASCII.GetString(detached));
        Assert.Equal("ab", Encoding.ASCII.GetString(fields[0].Span));
    }

    [Fact]
    public void StringViewsExposeLogicalUtf16AndRetainTheirImmutableBacking()
    {
        string logical = "é😀,\0Ā";
        string backing = "!!" + logical + "!!";
        var value = new StringCharSequence(backing, 2, logical.Length);
        Assert.True(value.AsSpan().SequenceEqual(logical.AsSpan()));
        Assert.True(MemoryMarshal.TryGetString(value.AsMemory(), out string owner, out int start, out int length));
        Assert.Same(backing, owner);
        Assert.Equal(2, start);
        Assert.Equal(logical.Length, length);
        var fields = new List<string>();
        foreach (Range range in value.AsSpan().Split(',')) fields.Add(value.AsSpan()[range].ToString());
        Assert.Equal(new[] { "é😀", "\0Ā" }, fields.ToArray());
        for (int i = 0; i < logical.Length; i++)
            Assert.Equal(logical.IndexOf(logical[i], i), value.indexOf(logical[i], i));
        Assert.Equal(-1, value.indexOf('!', 0));
        Assert.Equal(-1, value.indexOf('é', int.MaxValue));
        Assert.Equal(0, value.indexOf('é', int.MinValue));
        Assert.Equal(0x100, value.AsSpan()[^1]);
        Assert.Equal(-1, new AsciiString(value).indexOf('Ā', 0));
    }

    [Fact]
    public void MixedSequenceDelimiterConsumersKeepTheirExistingResults()
    {
        ICharSequence[] values =
        {
            new AsciiString("!!name:value!!").subSequence(2, 12, false),
            new StringCharSequence("!!name:value!!", 2, 10),
            new AppendableCharSequence(10).append("name:value")
        };
        foreach (ICharSequence value in values)
        {
            Assert.Equal(4, AsciiString.indexOf(value, ':', 0));
            Assert.Equal("value", value.SubstringAfter(':').ToString());
            Assert.Null(value.SubstringAfter(','));
        }
    }
}
