/*
 * Copyright 2012 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

/**
 * Tests channel buffer streams
 */
// Output half of pinned ByteBufStreamTest, paired with ByteBufInputStreamTest.
public class ByteBufOutputStreamTest
{
    private static ByteBuf Owner(int kind, int maximum = 100000)
    {
        if (kind == 0) return Unpooled.Buffer(0, maximum);
        if (kind == 1) return Unpooled.DirectBuffer(0, maximum);
        return Unpooled.CompositeBuffer(2);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void OriginalAllOutputOperationsRoundTripThroughInput(int kind)
    {
        ByteBuf buffer = Owner(kind); buffer.WriteByte(9);
        using var output = new ByteBufOutputStream(buffer);
        Assert.Same(buffer, output.Buffer); Assert.True(output.CanWrite);
        Assert.False(output.CanRead); Assert.False(output.CanSeek);
        output.WriteBoolean(true); output.WriteBoolean(false); output.WriteByte(42); output.WriteByte(224);
        output.WriteBytes("Hello, World!"); output.WriteChars("Hello, World"); output.WriteChar('!');
        output.WriteDouble(42); output.WriteSingle(42); output.WriteInt32(42); output.WriteInt64(42);
        output.WriteInt16(42); output.WriteUInt16(49152); output.WriteUtf("Hello, World!");
        output.WriteBytes("The first line\r\r\n"); output.Write(Array.Empty<byte>());
        output.Write(new byte[] { 1, 2, 3, 4 }, 0, 4); output.Write(new byte[] { 1, 3, 3, 4 }, 0, 0);
        int count = output.BytesWritten; Assert.Equal(buffer.WriterIndex - 1, count);
        output.Dispose(); Assert.Equal(1, buffer.ReferenceCount); Assert.Equal(count, output.BytesWritten);
        Assert.Equal(0, buffer.ReaderIndex); buffer.ReaderIndex = 1;
        using var input = new ByteBufInputStream(buffer, true);
        Assert.True(input.ReadBoolean()); Assert.False(input.ReadBoolean());
        Assert.Equal(42, input.ReadSByte()); Assert.Equal(224, input.ReadUnsignedByte());
        byte[] text = new byte[13]; input.ReadFully(text); Assert.Equal("Hello, World!", Encoding.Latin1.GetString(text));
        foreach (char character in "Hello, World!") Assert.Equal(character, input.ReadChar());
        Assert.Equal(42, input.ReadDouble()); Assert.Equal(42, input.ReadSingle());
        Assert.Equal(42, input.ReadInt32()); Assert.Equal(42, input.ReadInt64());
        Assert.Equal(42, input.ReadInt16()); Assert.Equal(49152, input.ReadUInt16());
        Assert.Equal("Hello, World!", input.ReadUtf());
        Assert.Equal("The first line", input.ReadLine()); Assert.Equal("", input.ReadLine());
        Span<byte> tail = stackalloc byte[4]; input.ReadFully(tail);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, tail.ToArray()); Assert.Equal(-1, input.ReadByte());
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)] [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(2, false)] [InlineData(2, true)]
    public async Task OriginalConditionalOwnershipReleasesOnceThroughDisposeOrDisposeAsync(int kind, bool release)
    {
        ByteBuf buffer = Owner(kind).WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6 });
        var output = new ByteBufOutputStream(buffer, release);
        Assert.Equal(1, buffer.ReferenceCount);
        output.WriteBoolean(true); output.WriteBoolean(false); output.WriteByte(42); output.WriteByte(224);
        output.WriteBytes("Hello, World!"); output.Write(new byte[] { 1, 3, 3, 4 }, 0, 0);
        await output.DisposeAsync(); output.Dispose(); output.Close();
        // When releaseOnClose is set to true, ByteBuf will be automatically released after calling the close method of
        // ByteBufOutputStream.
        Assert.Equal(release ? 0 : 1, output.Buffer.ReferenceCount);
        if (!release)
        {
            // When releaseOnClose is not set or releaseOnClose is false, ByteBuf must be released manually.
            buffer.Release();
        }
        Assert.Equal(0, buffer.ReferenceCount); Assert.False(output.CanWrite);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void OriginalStringBytesIgnoreHigherOrderBitsAndCharsPreserveCodeUnits(int kind)
    {
        ByteBuf buffer = Owner(kind);
        using var output = new ByteBufOutputStream(buffer, true);
        string value = new(new[] { '\u221A', '\0', '\u00FF', '\u0100', '\uD800', '\uDC00' });
        output.WriteBytes(value);
        Assert.Equal(0x221A, '√'); // This is a multibyte character
        Assert.Equal(0x1A, buffer.GetByte(0)); // Only the lower-order byte is written
        Assert.Equal(new byte[] { 0x1A, 0, 255, 0, 0, 0 }, buffer.ReadableSequence.ToArray());
        output.WriteChars(value);
        Assert.Equal(Convert.FromHexString("221a000000ff0100d800dc00"), buffer.AsReadOnlySequence(6, 12).ToArray());
        Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(18, output.BytesWritten);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void NumericTruncationAndIeeePayloadsAreExplicit(int kind)
    {
        ByteBuf buffer = Owner(kind);
        using var output = new ByteBufOutputStream(buffer, true);
        output.WriteSByte(-1); output.WriteInt16(int.MaxValue); output.WriteInt16(int.MinValue);
        // Use runtime bit inputs: Release constant folding can quiet a signaling NaN before the stream call.
        float signalingSingle = BitConverter.Int32BitsToSingle(int.Parse("7fa12345", NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        double signalingDouble = BitConverter.Int64BitsToDouble(long.Parse("fff0123456789abc", NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        Assert.Equal(0x7FA12345, BitConverter.SingleToInt32Bits(signalingSingle));
        Assert.Equal(unchecked((long)0xFFF0123456789ABC), BitConverter.DoubleToInt64Bits(signalingDouble));
        output.WriteSingle(signalingSingle);
        output.WriteSingle(BitConverter.Int32BitsToSingle(int.MinValue));
        output.WriteDouble(signalingDouble);
        output.WriteDouble(BitConverter.Int64BitsToDouble(long.MinValue));
        Assert.Equal(Convert.FromHexString("ffffff00007fa1234580000000fff0123456789abc8000000000000000"), buffer.ReadableSequence.ToArray());
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void SpanAndArraySubrangeWritesPreserveSourceAndReader(int kind)
    {
        ByteBuf buffer = Owner(kind); buffer.WriteInt(0x01020304); buffer.ReaderIndex = 2;
        using var output = new ByteBufOutputStream(buffer, true);
        byte[] source = { 9, 5, 6, 9 }; output.Write(source, 1, 2);
        Span<byte> stack = stackalloc byte[] { 7, 8 }; output.Write(stack);
        Assert.Equal(new byte[] { 9, 5, 6, 9 }, source);
        Assert.Equal(2, buffer.ReaderIndex); Assert.Equal(4, output.BytesWritten);
        Assert.Equal(Convert.FromHexString("0102030405060708"), buffer.AsReadOnlySequence(0, 8).ToArray());
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)]
    public void DisposedBorrowedStreamRejectsAllWritesEvenAfterFirstUtfUse(int operation)
    {
        ByteBuf buffer = Unpooled.Buffer(0);
        var output = new ByteBufOutputStream(buffer); output.WriteUtf("x"); output.Dispose();
        Action[] writes =
        {
            () => output.WriteByte(1), () => output.Write(new byte[0]), () => output.Write(new byte[0], 0, 0),
            () => output.WriteBoolean(true), () => output.WriteInt16(1), () => output.WriteChar('a'),
            () => output.WriteInt32(1), () => output.WriteInt64(1), () => output.WriteSingle(1),
            () => output.WriteDouble(1), () => output.WriteBytes(""), () => output.WriteChars(""), () => output.WriteUtf("")
        };
        try
        {
            Assert.Throws<ObjectDisposedException>(writes[operation]);
            Assert.Equal(1, buffer.ReferenceCount); Assert.Equal(3, buffer.WriterIndex);
            Assert.Equal(3, output.BytesWritten); Assert.Same(buffer, output.Buffer);
            Assert.Throws<ObjectDisposedException>(() => output.Flush());
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReadonlyStorageRejectsEvenEmptyWritesWithoutTransferringAdditionalOwnership(bool fixedComposite)
    {
        ByteBuf buffer = fixedComposite ? Unpooled.WrappedUnmodifiableBuffer(Unpooled.CopyShort(1), Unpooled.CopyShort(2))
            : Unpooled.Buffer(8).AsReadOnly();
        using var output = new ByteBufOutputStream(buffer, true);
        Assert.False(output.CanWrite);
        Assert.Throws<NotSupportedException>(() => output.WriteByte(1));
        Assert.Throws<NotSupportedException>(() => output.Write(Array.Empty<byte>()));
        Assert.Throws<NotSupportedException>(() => output.WriteBytes(""));
        Assert.Throws<NotSupportedException>(() => output.WriteChars(""));
        Assert.Throws<NotSupportedException>(() => output.WriteUtf(""));
        Assert.Equal(0, output.BytesWritten); Assert.Equal(1, buffer.ReferenceCount);
    }

    [Fact]
    public void NullRangeUnsupportedAndDeadConstructionChecksDoNotConsumeReferences()
    {
        Assert.Throws<ArgumentNullException>(() => new ByteBufOutputStream(null));
        ByteBuf buffer = Unpooled.Buffer(0);
        using var output = new ByteBufOutputStream(buffer, true);
        Assert.Throws<ArgumentNullException>(() => output.Write(null, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => output.Write(new byte[0], -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => output.Write(new byte[0], 0, 1));
        Assert.Throws<ArgumentNullException>(() => output.WriteBytes(null));
        Assert.Throws<ArgumentNullException>(() => output.WriteChars(null));
        Assert.Throws<ArgumentNullException>(() => output.WriteUtf(null));
        Assert.Throws<NotSupportedException>(() => output.Read(new byte[0]));
        Assert.Throws<NotSupportedException>(() => output.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => output.SetLength(1));
        Assert.Throws<NotSupportedException>(() => output.Length);
        Assert.Throws<NotSupportedException>(() => output.Position);
        Assert.Equal(0, buffer.WriterIndex); Assert.Equal(1, buffer.ReferenceCount);
        ByteBuf dead = Unpooled.Buffer(0); dead.Release();
        Assert.Throws<IllegalReferenceCountException>(() => new ByteBufOutputStream(dead));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void OriginalCapacityFailureProgressIsPreserved(int operation)
    {
        ByteBuf buffer = Unpooled.Buffer(0, 3).WriteByte(9);
        using var output = new ByteBufOutputStream(buffer, true);
        Action write = operation switch { 0 => () => output.WriteChars("AB"), 1 => () => output.WriteBytes("ABC"), _ => () => output.WriteUtf("a") };
        Assert.Throws<ArgumentOutOfRangeException>(write);
        Assert.Equal(operation == 0 ? new byte[] { 9, 0, 65 } : new byte[] { 9 }, buffer.ReadableSequence.ToArray());
        Assert.Equal(operation == 0 ? 2 : 0, output.BytesWritten);
    }

    [Theory]
    [InlineData(0, 65535)] [InlineData(1, 32767)] [InlineData(2, 21845)]
    public void MaximumModifiedUtfPayloadsRoundTripWithoutFallback(int kind, int length)
    {
        string value = new(kind == 0 ? 'A' : kind == 1 ? '\0' : '\uD800', length);
        ByteBuf buffer = Owner(kind);
        using var output = new ByteBufOutputStream(buffer);
        output.WriteUtf(value);
        using var input = new ByteBufInputStream(buffer, true);
        Assert.Equal(value, input.ReadUtf()); Assert.Equal(0, input.Available);
        Assert.Equal(kind == 1 ? 65536 : 65537, output.BytesWritten);
    }

    [Theory]
    [InlineData('A', 65536)] [InlineData('\0', 32768)] [InlineData('\u0800', 21846)]
    public void OversizedModifiedUtfFailsBeforeWritingItsLengthOrPayload(char character, int length)
    {
        ByteBuf buffer = Unpooled.Buffer(1).WriteByte(9);
        using var output = new ByteBufOutputStream(buffer, true);
        Assert.Throws<InvalidDataException>(() => output.WriteUtf(new string(character, length)));
        Assert.Equal(0, output.BytesWritten); Assert.Equal(new byte[] { 9 }, buffer.ReadableSequence.ToArray());
    }

    [Fact]
    public void ModifiedUtfPreservesNulAndIsolatedSurrogatesAndItsUnsignedLengthPrefix()
    {
        string value = new(new[] { '\0', '\u007F', '\u0080', '\u07FF', '\u0800', '\uD800', '\uDC00', '\uFFFF' });
        ByteBuf buffer = Unpooled.Buffer(0);
        using var output = new ByteBufOutputStream(buffer);
        output.WriteUtf(value);
        Assert.Equal(Convert.FromHexString("0013c0807fc280dfbfe0a080eda080edb080efbfbf"), buffer.ReadableSequence.ToArray());
        using var input = new ByteBufInputStream(buffer, true); Assert.Equal(value, input.ReadUtf());
    }

    [Fact]
    public void NativeAllocationFailureLeavesExistingContentAndWriterUnchanged()
    {
        var allocator = new NativeMemoryAllocator(4);
        ByteBuf buffer = new UnpooledDirectByteBuf(2, 64, allocator).WriteByte(9);
        using var output = new ByteBufOutputStream(buffer, true);
        Assert.Throws<OutOfMemoryException>(() => output.WriteUtf("a"));
        Assert.Equal(0, output.BytesWritten); Assert.Equal(1, buffer.WriterIndex);
        Assert.Equal(9, buffer.GetByte(0)); Assert.Equal(2, allocator.ReservedBytes);
        output.Dispose(); Assert.Equal(0, allocator.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task StandardAsyncCancellationAndBinaryWriterUseClrStreamContracts(bool native)
    {
        ByteBuf buffer = Owner(native ? 1 : 0);
        using var output = new ByteBufOutputStream(buffer, true);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        byte[] bytes = { 1, 2, 3, 4 };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => output.WriteAsync(bytes.AsMemory(), canceled.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => output.WriteAsync(bytes, 0, 4, canceled.Token));
        Assert.Equal(0, buffer.WriterIndex);
        await output.WriteAsync(bytes.AsMemory(1, 2), TestContext.Current.CancellationToken);
        await output.WriteAsync(bytes, 0, 1, TestContext.Current.CancellationToken);
        using (var writer = new BinaryWriter(output, Encoding.UTF8, true)) writer.Write(0x04030201);
        Assert.Equal(Convert.FromHexString("02030101020304"), buffer.ReadableSequence.ToArray());
        await output.FlushAsync(TestContext.Current.CancellationToken);
        output.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => output.WriteAsync(bytes.AsMemory(), TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeAliasedMemorySurvivesGrowthOrCompositeConsolidation(bool composite)
    {
        var allocator = new NativeMemoryAllocator(128);
        ByteBuf native = new UnpooledDirectByteBuf(2, 64, allocator).WriteShort(0x0102);
        ByteBuf buffer = composite ? new CompositeByteBuf(1).AddComponent(native, true) : native;
        using var output = new ByteBufOutputStream(buffer, true);
        ReadOnlyMemory<byte> source = native.AsReadOnlyMemory(0, 2);
        await output.WriteAsync(source, TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 1, 2, 1, 2 }, buffer.ReadableSequence.ToArray());
        Assert.Equal(2, output.BytesWritten); Assert.Equal(composite ? 0 : 1, native.ReferenceCount);
        output.Dispose(); Assert.Equal(0, allocator.ReservedBytes);
    }
}
