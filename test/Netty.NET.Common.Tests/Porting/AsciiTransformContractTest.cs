using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class AsciiTransformContractTest
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void TrimKeepsTheLastCharacterAndAcceptsAllControlInput(int representation)
    {
        foreach (var row in new[] { (" \ta \0", "a"), (" trailers ", "trailers"), ("\0\t\r\n ", "") })
        {
            ICharSequence source = Sequence(row.Item1, representation);
            Assert.Equal(row.Item2, AsciiString.trim(source).ToString());
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void TrimRetainsNonControlUnicodeAndUnchangedIdentity(int representation)
    {
        foreach (string text in new[] { "", "a\0b", "\u0085x\u00a0", "\u2003x\u2003", "\ud800x\udfff" })
        {
            ICharSequence source = Sequence(text, representation);
            Assert.Same(source, AsciiString.trim(source));
            Assert.Equal(text, AsciiString.trim(Sequence("\0 \t" + text + "\r\n", representation)).ToString());
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(3)]
    public void ByteTrimKeepsUnchangedViewsAndSharesTrimmedStorage(int offset)
    {
        byte[] storage = new byte[offset + 8];
        storage.AsSpan(offset).Fill(0xff);
        var source = new AsciiString(storage, offset, 8, false);
        Assert.Same(source, source.trim());
        Assert.Same(source, AsciiString.trim(source));
        var empty = new AsciiString(storage, offset, 0, false);
        Assert.Same(empty, empty.trim());
        storage[offset] = 0;
        storage[offset + 1] = 32;
        storage[offset + 6] = 9;
        storage[offset + 7] = 32;
        AsciiString trimmed = source.trim();
        Assert.Same(storage, trimmed.array());
        Assert.Equal(offset + 2, trimmed.arrayOffset());
        Assert.Equal(4, trimmed.length());
        storage[offset + 3] = 0xa0;
        Assert.Equal(0xa0, trimmed.AsSpan()[1]);
    }

    [Fact]
    public void NullTrimUsesTheNativeArgumentException()
    {
        var error = Assert.Throws<ArgumentNullException>(() => AsciiString.trim(null));
        Assert.Equal("c", error.ParamName);
    }

    [Fact]
    public void NativeUnsignedByteTrimKeepsAllValuesAboveSpace()
    {
        for (int code = 0; code <= byte.MaxValue; code++)
        {
            byte[] storage = { 0xee, 0, 32, (byte)code, 32, 9, 0xee };
            var source = new AsciiString(storage, 1, 5, false);
            AsciiString trimmed = source.trim();
            Assert.Equal(code <= 32 ? Array.Empty<byte>() : new[] { (byte)code }, trimmed.AsSpan().ToArray());
            Assert.Same(storage, trimmed.array());
            if (code > 32) Assert.Equal(3, trimmed.arrayOffset());
        }
    }

    [Fact]
    public void ProtocolCaseConversionIsCultureIndependentAndOwnsChangedBytes()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            byte[] storage = { 0xee, (byte)'I', 0xc9, 0xff, (byte)'z', 0xee };
            var source = new AsciiString(storage, 1, 4, false);
            AsciiString lower = source.toLowerCase(), upper = source.toUpperCase();
            Assert.Equal(new byte[] { (byte)'i', 0xc9, 0xff, (byte)'z' }, lower.AsSpan().ToArray());
            Assert.Equal(new byte[] { (byte)'I', 0xc9, 0xff, (byte)'Z' }, upper.AsSpan().ToArray());
            Assert.NotSame(storage, lower.array());
            Assert.NotSame(storage, upper.array());
            storage[1] = (byte)'Q';
            Assert.Equal((byte)'i', lower.AsSpan()[0]);
            Assert.Equal((byte)'I', upper.AsSpan()[0]);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void WordPatternsFindEveryByteInEitherEndianOrder()
    {
        Span<byte> bytes = stackalloc byte[8];
        for (int target = 0; target <= byte.MaxValue; target++)
        {
            long pattern = SWARUtil.compilePattern((byte)target);
            for (int lane = 0; lane < bytes.Length; lane++)
            {
                bytes.Fill((byte)(target ^ 255));
                bytes[lane] = (byte)target;
                long word = MemoryMarshal.Read<long>(bytes);
                long matches = SWARUtil.applyPattern(word, pattern);
                Assert.Equal(lane, SWARUtil.getIndex(matches, !BitConverter.IsLittleEndian));
                Assert.Equal(7 - lane, SWARUtil.getIndex(matches, BitConverter.IsLittleEndian));
            }
            bytes.Fill((byte)(target ^ 255));
            Assert.Equal(0, SWARUtil.applyPattern(MemoryMarshal.Read<long>(bytes), pattern));
        }
        Assert.Equal(8, SWARUtil.getIndex(0, true));
        Assert.Equal(8, SWARUtil.getIndex(0, false));
    }

    private static ICharSequence Sequence(string text, int representation) => representation switch
    {
        0 => new StringCharSequence("!!" + text + "!!", 2, text.Length),
        1 => new AppendableCharSequence(1).append(text),
        _ => new IndexedSequence(text)
    };

    private sealed class IndexedSequence(string text) : ICharSequence
    {
        public int Count => text.Length;
        public char this[int index] => text[index];
        public char charAt(int index) => this[index];
        public int length() => Count;
        public ICharSequence subSequence(int start, int end) => new IndexedSequence(text[start..end]);
        public ICharSequence subSequence(int start) => subSequence(start, Count);
        public override string ToString() => text;
        public string ToString(int start) => text[start..];
        public bool regionMatches(int a, ICharSequence b, int c, int d) => throw new NotSupportedException();
        public bool regionMatchesIgnoreCase(int a, ICharSequence b, int c, int d) => throw new NotSupportedException();
        public bool contentEquals(ICharSequence other) => throw new NotSupportedException();
        public bool contentEqualsIgnoreCase(ICharSequence other) => throw new NotSupportedException();
        public int indexOf(char ch, int start = 0) => throw new NotSupportedException();
        public int hashCode(bool ignoreCase) => throw new NotSupportedException();
        public IEnumerator<char> GetEnumerator() => throw new NotSupportedException();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
