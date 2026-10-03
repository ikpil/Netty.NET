using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Netty.NET.Common.Tests.Porting;

public class AsciiStringSequenceContractTest
{
    [Theory]
    [InlineData(int.MinValue, int.MaxValue)]
    [InlineData(int.MinValue, 0)]
    [InlineData(-1, int.MaxValue)]
    [InlineData(int.MaxValue, int.MinValue)]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(1, 0)]
    [InlineData(0, 3)]
    [InlineData(3, 3)]
    [InlineData(2, 3)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void InvalidSliceEndpointsHaveNativeArgumentErrorsEvenWhenChecked(int start, int end)
    {
        var value = new AsciiString(new byte[] { 1, 2, 3, 4 }, 1, 2, false);
        Assert.Throws<ArgumentOutOfRangeException>(() => value.subSequence(start, end, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => value.subSequence(start, end, true));
    }

    [Theory]
    [InlineData("", "", 0, 0)]
    [InlineData("ababa", "aba", 0, 2)]
    [InlineData("aaaa", "aa", 0, 2)]
    [InlineData("abc", "x", -1, -1)]
    [InlineData("abc", "", 0, 3)]
    [InlineData("aa\0bb\0", "\0", 2, 5)]
    [InlineData("é\0ÿé", "é", 0, 3)]
    [InlineData("é\0ÿé", "\0ÿ", 1, 1)]
    public void NativePatternSearchUsesOnlyLogicalByteAndOrdinalCharViews(string text, string pattern, int first, int last)
    {
        var bytes = new AsciiString(Encoding.Latin1.GetBytes("!!" + text + "!!"), 2, text.Length, false);
        var chars = new StringCharSequence("!!" + text + "!!", 2, text.Length);
        byte[] needle = Encoding.Latin1.GetBytes(pattern);
        Assert.Equal(first, bytes.AsSpan().IndexOf(needle));
        Assert.Equal(last, bytes.AsSpan().LastIndexOf(needle));
        Assert.Equal(first, chars.AsSpan().IndexOf(pattern.AsSpan(), StringComparison.Ordinal));
        Assert.Equal(last, chars.AsSpan().LastIndexOf(pattern.AsSpan(), StringComparison.Ordinal));
    }

    [Fact]
    public void NativeWindowsAgreeWithIndependentCandidateScansForEveryByte()
    {
        for (int character = 0; character < 256; character++)
        {
            byte[] source = { 1, (byte)character, 0, (byte)character, 0, 2 };
            byte[] backing = new byte[source.Length + 4];
            source.CopyTo(backing, 2);
            var value = new AsciiString(backing, 2, source.Length, false);
            byte[] pattern = { (byte)character, 0 };
            for (int start = 0; start <= source.Length; start++)
            {
                int first = -1, last = -1;
                for (int candidate = 0; candidate <= source.Length - pattern.Length; candidate++)
                {
                    if (source[candidate] != pattern[0] || source[candidate + 1] != pattern[1]) continue;
                    if (candidate >= start && first < 0) first = candidate;
                    if (candidate <= start) last = candidate;
                }
                int relative = value.AsSpan().Slice(start).IndexOf(pattern);
                Assert.Equal(first, relative < 0 ? -1 : start + relative);
                int end = Math.Min(start + pattern.Length, source.Length);
                Assert.Equal(last, value.AsSpan().Slice(0, end).LastIndexOf(pattern));
            }
        }
    }

    [Fact]
    public void NativeEncodingAndOrdinalChoiceRemainExplicit()
    {
        var value = new AsciiString(new byte[] { 0xe9, 0, 0xff });
        Assert.Equal(0, value.AsSpan().IndexOf(new byte[] { 0xe9 }));
        Assert.Equal(-1, value.AsSpan().IndexOf("é"u8));
        var text = new StringCharSequence("!!Ā😀I!!", 2, 4);
        Assert.Equal(0, text.AsSpan().IndexOf("Ā".AsSpan(), StringComparison.Ordinal));
        Assert.Equal(1, text.AsSpan().IndexOf("😀".AsSpan(), StringComparison.Ordinal));
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal(-1, text.AsSpan().IndexOf("ı".AsSpan(), StringComparison.Ordinal));
            Assert.Equal(3, text.AsSpan().IndexOf("I".AsSpan(), StringComparison.Ordinal));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void PartialSlicesDistinguishSharedBytesAndDetachedStorage()
    {
        byte[] backing = Encoding.ASCII.GetBytes("!!abcdef!!");
        var parent = new AsciiString(backing, 2, 6, false);
        var borrowed = parent.subSequence(1, 4, false);
        var detached = parent.subSequence(1, 4, true);
        Assert.Equal("bcd", borrowed.ToString());
        Assert.Equal("bcd", detached.ToString());
        Assert.True(MemoryMarshal.TryGetArray(borrowed.AsMemory(), out ArraySegment<byte> shared));
        Assert.Same(backing, shared.Array);
        Assert.Equal(3, shared.Offset);
        Assert.NotSame(backing, detached.array());
        Assert.Equal(0, detached.arrayOffset());
        backing[3] = (byte)'x';
        Assert.Equal(0, borrowed.AsSpan().IndexOf("xcd"u8));
        borrowed.arrayChanged();
        parent.arrayChanged();
        Assert.Equal("xcd", borrowed.ToString());
        Assert.Equal("axcdef", parent.ToString());
        Assert.Equal("bcd", detached.ToString());
    }

    [Fact]
    public void NestedSlicesAndNativeRangesStayRelativeToTheirOwnLength()
    {
        var parent = new AsciiString(Encoding.ASCII.GetBytes("!!abcdef!!"), 2, 6, false);
        var child = parent.subSequence(1, 5, false).subSequence(1, 3, false);
        Assert.Equal("cd", child.ToString());
        Assert.Equal(4, child.arrayOffset());
        Assert.Equal(-1, child.AsSpan().IndexOf("bc"u8));
        Assert.Throws<ArgumentOutOfRangeException>(() => child.subSequence(0, 3, false));
        Assert.Equal("e", Encoding.ASCII.GetString(parent.AsMemory().Slice(1, 4).Span[^1..]));
        var text = new StringCharSequence("!!Ā😀abc!!", 2, 6);
        var textChild = (StringCharSequence)text.subSequence(1, 3);
        Assert.Equal("😀", textChild.ToString());
        Assert.Equal(0, textChild.AsSpan().IndexOf("😀".AsSpan(), StringComparison.Ordinal));
        Assert.Throws<ArgumentOutOfRangeException>(() => textChild.subSequence(0, 3));
    }

    [Fact]
    public void FullAndEmptyBridgeIdentityDoesNotImplyAnIndependentCopy()
    {
        byte[] backing = { 1, 2, 3, 4 };
        var parent = new AsciiString(backing, 1, 2, false);
        Assert.Same(parent, parent.subSequence(0, parent.Count, true));
        Assert.Same(parent, parent.subSequence(0, parent.Count, false));
        Assert.Same(AsciiString.EMPTY_STRING, parent.subSequence(1, 1, true));
        Assert.Same(AsciiString.EMPTY_STRING, parent.subSequence(1, 1, false));
        byte[] independent = parent.AsMemory().ToArray();
        backing[1] = 9;
        Assert.Equal(new byte[] { 2, 3 }, independent);
        Assert.Equal(new byte[] { 9, 3 }, parent.AsSpan().ToArray());
        var empty = new AsciiString(backing, 2, 0, false);
        Assert.Same(empty, empty.subSequence(0, 0, true));
    }
}
