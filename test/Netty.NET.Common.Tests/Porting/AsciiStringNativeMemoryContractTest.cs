using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

// ByteBuffer adaptation is expressed as bounded ReadOnlyMemory<byte>, without
// introducing Java position/limit/CharBuffer types into the CLR public API.
public class AsciiStringNativeMemoryContractTest
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MemorySliceUsesItsOwnOriginAndCopyPolicy(bool copy)
    {
        byte[] bytes = { 10, 20, 30, 128, 233, 255, 40 };
        ReadOnlyMemory<byte> memory = bytes.AsMemory(2, 4);
        AsciiString value = new AsciiString(memory, 1, 2, copy);
        Assert.Equal(new byte[] { 128, 233 }, value.AsSpan().ToArray());
        Assert.Equal("\u0080é", value.ToString());
        Assert.True(MemoryMarshal.TryGetArray(value.AsMemory(), out ArraySegment<byte> storage));
        if (copy)
        {
            Assert.NotSame(bytes, storage.Array);
            Assert.Equal(0, storage.Offset);
        }
        else
        {
            Assert.Same(bytes, storage.Array);
            Assert.Equal(3, storage.Offset);
        }

        bytes[3] = 255;
        value.arrayChanged();
        Assert.Equal(copy ? "\u0080é" : "ÿé", value.ToString());
    }

    [Fact]
    public void DefaultMemoryConstructionCopiesAndEmptyMemoryIsValid()
    {
        byte[] bytes = { 233 };
        AsciiString value = new AsciiString(bytes.AsMemory());
        bytes[0] = 0;
        Assert.Equal("é", value.ToString());
        Assert.Equal(string.Empty, new AsciiString(default(ReadOnlyMemory<byte>), false).ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NonArrayMemoryCopiesBeforeOwnerDisposal(bool copy)
    {
        AsciiString value;
        using (MemoryManager<byte> owner = new NonArrayMemoryOwner(new byte[] { 1, 128, 255, 2 }))
        {
            value = new AsciiString(owner.Memory.Slice(1, 2), copy);
            owner.GetSpan()[1] = 0;
        }
        Assert.Equal(new byte[] { 128, 255 }, value.AsSpan().ToArray());
        Assert.Equal("\u0080ÿ", value.ToString());
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, -1)]
    [InlineData(2, 2)]
    [InlineData(int.MaxValue, 1)]
    public void MemoryConstructionCannotEscapeLogicalSlice(int start, int length)
    {
        byte[] bytes = new byte[20];
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AsciiString(bytes.AsMemory(5, 3), start, length, false));
    }

    [Fact]
    public void StringAndSpanConstructionUseTheSameSingleByteMapping()
    {
        const string input = "!Aéÿ\u0100\ud83d\ude00!";
        AsciiString fromString = new AsciiString(input, 1, input.Length - 2);
        AsciiString fromSpan = new AsciiString(input.AsSpan(1, input.Length - 2));
        Assert.Equal("Aéÿ???", fromString.ToString());
        Assert.Equal(fromString, fromSpan);
        Assert.Equal(new byte[] { 65, 233, 255, 63, 63, 63 }, fromSpan.AsSpan().ToArray());
    }

    [Fact]
    public void EncodingConstructionUsesExactRangeAndCallerFallbackWithoutPreamble()
    {
        const string input = "!é😀!";
        Encoding strictUtf8 = new UTF8Encoding(false, true);
        byte[] expected = { 0xc3, 0xa9, 0xf0, 0x9f, 0x98, 0x80 };
        Assert.Equal(expected, new AsciiString(input, strictUtf8, 1, 3).AsSpan().ToArray());
        Assert.Equal(expected, new AsciiString(input.AsSpan(1, 3), strictUtf8).AsSpan().ToArray());
        Assert.Throws<EncoderFallbackException>(() => new AsciiString("\ud800", strictUtf8));
        Assert.Throws<EncoderFallbackException>(() => new AsciiString("é",
            Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)));
    }

    [Fact]
    public void EncodedSequenceSliceHasTheSameBytesAsNativeInput()
    {
        ICharSequence value = new StringCharSequence("unused!é😀!unused", 6, 5);
        AsciiString encoded = new AsciiString(value, Encoding.UTF8, 1, 3);
        Assert.Equal(new byte[] { 0xc3, 0xa9, 0xf0, 0x9f, 0x98, 0x80 }, encoded.AsSpan().ToArray());
    }

    [Fact]
    public void NullNativeInputsAreRejectedAtTheBoundary()
    {
        Assert.Throws<ArgumentNullException>(() => new AsciiString((string)null));
        Assert.Throws<ArgumentNullException>(() => new AsciiString((byte[])null));
        Assert.Throws<ArgumentNullException>(() => new AsciiString("value", (Encoding)null));
    }

    private sealed class NonArrayMemoryOwner : MemoryManager<byte>
    {
        private byte[] _storage;
        public NonArrayMemoryOwner(byte[] storage) => _storage = storage;
        public override Span<byte> GetSpan() => _storage ?? throw new ObjectDisposedException(nameof(NonArrayMemoryOwner));
        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
        public override void Unpin() { }
        protected override void Dispose(bool disposing) => _storage = null;
    }
}
