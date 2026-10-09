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
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

/**
 * Tests channel buffer streams
 */
// Input half of pinned ByteBufStreamTest plus CLR Stream integration, paired with ByteBufOutputStreamTest.
public class ByteBufInputStreamTest
{
    private static ByteBuf Owner(int kind, byte[] bytes)
    {
        if (kind == 0) return Unpooled.WrappedBuffer(bytes);
        if (kind == 1) return Unpooled.DirectBuffer(bytes.Length).WriteBytes(bytes);
        int split = bytes.Length / 2;
        ByteBuf first = Unpooled.WrappedBuffer(bytes[..split]), second = Unpooled.WrappedBuffer(bytes[split..]);
        return kind == 2 ? Unpooled.CompositeBuffer().AddComponents(new[] { first, second }, true)
            : Unpooled.WrappedUnmodifiableBuffer(first, second);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)] [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(2, false)] [InlineData(2, true)] [InlineData(3, false)] [InlineData(3, true)]
    public void OriginalBoundedReadsMarksAndSkipShareTheReader(int kind, bool readOnly)
    {
        ByteBuf owner = Owner(kind, new byte[] { 9, 1, 2, 3, 4, 5, 6 }); owner.ReaderIndex = 1;
        ByteBuf input = readOnly ? owner.AsReadOnly() : owner;
        using var stream = new ByteBufInputStream(input, 4, true);
        Assert.True(stream.CanRead); Assert.False(stream.CanSeek); Assert.False(stream.CanWrite);
        Assert.Equal(0, stream.BytesRead); Assert.Equal(4, stream.Available);
        Assert.Equal(0, stream.Skip(-1)); Assert.Equal(0, stream.SkipBytes(-1));
        Assert.Equal(1, stream.ReadByte()); stream.Mark(); Assert.Equal(2, stream.ReadByte());
        stream.Reset(); Assert.Equal(2, stream.ReadByte());
        Assert.Equal(2, stream.Skip(long.MaxValue)); Assert.Equal(4, stream.BytesRead);
        Assert.Equal(-1, stream.ReadByte()); Assert.Equal(-1, stream.ReadByte());
        Assert.Equal(0, stream.Read(new byte[3])); Assert.Equal(0, stream.Read(Span<byte>.Empty));
        Assert.Equal(0, stream.Available);
        Assert.Equal(5, input.ReaderIndex);
        Assert.Equal(ReferenceEquals(input, owner) ? 5 : 1, owner.ReaderIndex);
        stream.Reset(); Assert.Equal(3, stream.Available); Assert.Equal(2, stream.ReadByte());
        stream.Dispose(); Assert.Equal(0, owner.ReferenceCount);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void OriginalDataInputPrimitivesAndFullyUseBigEndianAcrossBoundaries(int kind)
    {
        ByteBuf encoded = Unpooled.Buffer(0);
        encoded.WriteBoolean(true).WriteBoolean(false).WriteByte(42).WriteByte(224);
        encoded.WriteBytes(Encoding.Latin1.GetBytes("Hello, World!"));
        foreach (char value in "Hello, World!") encoded.WriteChar(value);
        encoded.WriteDouble(42).WriteFloat(42).WriteInt(42).WriteLong(42).WriteShort(42).WriteShort(49152);
        encoded.WriteShort(13).WriteBytes(Encoding.ASCII.GetBytes("Hello, World!"));
        encoded.WriteBytes(Encoding.Latin1.GetBytes("The first line\r\r\n"));
        encoded.WriteBytes(new byte[] { 1, 2, 3, 4 });
        ByteBuf owner = Owner(kind, encoded.ReadableSequence.ToArray()); encoded.Release();
        using var stream = new ByteBufInputStream(owner, true);
        Assert.True(stream.ReadBoolean()); Assert.False(stream.ReadBoolean());
        Assert.Equal(42, stream.ReadSByte()); Assert.Equal(224, stream.ReadUnsignedByte());
        byte[] bytes = new byte[13]; stream.ReadFully(bytes);
        Assert.Equal("Hello, World!", Encoding.Latin1.GetString(bytes));
        foreach (char value in "Hello, World!") Assert.Equal(value, stream.ReadChar());
        Assert.Equal(42, stream.ReadDouble()); Assert.Equal(42, stream.ReadSingle());
        Assert.Equal(42, stream.ReadInt32()); Assert.Equal(42, stream.ReadInt64());
        Assert.Equal(42, stream.ReadInt16()); Assert.Equal(49152, stream.ReadUInt16());
        Assert.Equal("Hello, World!", stream.ReadUtf());
        Assert.Equal("The first line", stream.ReadLine()); Assert.Equal("", stream.ReadLine());
        Assert.Equal(4, stream.Read(bytes)); Assert.Equal(new byte[] { 1, 2, 3, 4 }, bytes[..4]);
        Assert.Equal(-1, stream.ReadByte()); Assert.Equal(0, stream.Read(bytes));
        Assert.Throws<EndOfStreamException>(() => stream.ReadSByte());
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.ReadFully(bytes, 0, -1));
        Assert.Throws<EndOfStreamException>(() => stream.ReadFully(bytes));
        int count = stream.BytesRead; stream.Dispose(); Assert.Equal(count, stream.BytesRead);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)] [InlineData(4)] [InlineData(6)]
    public void OriginalLineLengthRespectedAndCrLfNeverCrossesTheCapturedEnd(int length)
    {
        ByteBuf buffer = Owner(2, new byte[] { (byte)'A', (byte)'B', 13, 10, (byte)'C', (byte)'E' });
        using var stream = new ByteBufInputStream(buffer, length, true);
        if (length == 0) Assert.Null(stream.ReadLine());
        else
        {
            Assert.Equal(length == 1 ? "A" : "AB", stream.ReadLine());
            if (length == 6) Assert.Equal("CE", stream.ReadLine());
            Assert.Null(stream.ReadLine());
        }
        Assert.Equal(length, buffer.ReaderIndex);
    }

    [Fact]
    public void OriginalReadLineSequenceAndReset()
    {
        ByteBuf buffer = Owner(3, Encoding.ASCII.GetBytes("\na\n\nb\r\nc\nd\ne"));
        using var stream = new ByteBufInputStream(buffer, true);
        int charCount = 7; //total chars in the string below without new line characters
        stream.Mark();
        foreach (string expected in new[] { "", "a", "", "b", "c", "d", "e" }) Assert.Equal(expected, stream.ReadLine());
        Assert.Null(stream.ReadLine()); stream.Reset();
        int count = 0; while (stream.ReadLine() != null) { ++count; Assert.True(count <= charCount); }
        Assert.Equal(charCount, count);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)]
    public void TypedEofDoesNotPartiallyConsumeOrAlterDestination(int operation)
    {
        ByteBuf buffer = Owner(1, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        using var stream = new ByteBufInputStream(buffer, 0, true);
        byte[] destination = { 9, 9 };
        Action[] reads =
        {
            () => stream.ReadBoolean(), () => stream.ReadSByte(), () => stream.ReadUnsignedByte(),
            () => stream.ReadChar(), () => stream.ReadInt16(), () => stream.ReadUInt16(),
            () => stream.ReadInt32(), () => stream.ReadInt64(), () => stream.ReadSingle(),
            () => stream.ReadFully(destination)
        };
        Assert.Throws<EndOfStreamException>(reads[operation]);
        Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(new byte[] { 9, 9 }, destination);
        stream.ReadFully(Span<byte>.Empty);
    }

    [Fact]
    public void CapturedEndDoesNotFollowAppendedWrites()
    {
        ByteBuf buffer = Unpooled.Buffer(2, 64).WriteByte(1);
        using var stream = new ByteBufInputStream(buffer, true);
        buffer.WriteByte(2).WriteByte(3);
        Assert.Equal(1, stream.Available); Assert.Equal(1, stream.ReadByte());
        Assert.Equal(-1, stream.ReadByte()); Assert.Equal(2, buffer.ReadableBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task DisposeAndDisposeAsyncReleaseAtMostOneExistingReference(bool releaseOnDispose)
    {
        ByteBuf buffer = Unpooled.CopyInt(1); buffer.Retain();
        var stream = new ByteBufInputStream(buffer, releaseOnDispose);
        Assert.Equal(2, buffer.ReferenceCount);
        await stream.DisposeAsync(); stream.Dispose(); stream.Close();
        Assert.Equal(releaseOnDispose ? 1 : 2, buffer.ReferenceCount);
        Assert.False(stream.CanRead);
        Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
        Assert.Throws<ObjectDisposedException>(() => stream.Read(new byte[0]));
        Assert.Throws<ObjectDisposedException>(() => stream.ReadLine());
        Assert.Throws<ObjectDisposedException>(() => stream.Mark());
        Assert.Throws<ObjectDisposedException>(() => stream.Reset());
        Assert.Throws<ObjectDisposedException>(() => stream.Skip(0));
        buffer.Release(buffer.ReferenceCount);
    }

    [Fact]
    public void OriginalInvalidLengthReturnsTransferredReferenceAndNullOrDeadOwnersFail()
    {
        Assert.Throws<ArgumentNullException>(() => new ByteBufInputStream(null));
        Assert.Throws<ArgumentNullException>(() => new ByteBufInputStream(null, true));
        Assert.Throws<ArgumentNullException>(() => new ByteBufInputStream(null, 0, true));
        ByteBuf buffer = Unpooled.CopyInt(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ByteBufInputStream(buffer.RetainedSlice(), -1, true));
        Assert.Equal(1, buffer.ReferenceCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ByteBufInputStream(buffer.RetainedSlice(), 5, true));
        Assert.Equal(1, buffer.ReferenceCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ByteBufInputStream(buffer, -1));
        Assert.Equal(1, buffer.ReferenceCount); buffer.Release();
        Assert.Throws<IllegalReferenceCountException>(() => new ByteBufInputStream(buffer));
    }

    [Fact]
    public void ArgumentValidationStillAppliesAtEofAndExternalReaderCannotEscapeRange()
    {
        ByteBuf buffer = Unpooled.CopyInt(1);
        using var stream = new ByteBufInputStream(buffer, 1, true);
        stream.ReadByte();
        Assert.Throws<ArgumentNullException>(() => stream.Read(null, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Read(new byte[1], -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Read(new byte[1], 1, 1));
        Assert.Equal(1, buffer.ReaderIndex);
        buffer.ReaderIndex = 2;
        Assert.Throws<InvalidOperationException>(() => stream.ReadByte());
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(1));
        Assert.Throws<NotSupportedException>(() => stream.Write(new byte[0]));
        Assert.Throws<NotSupportedException>(() => stream.Length);
        Assert.Throws<NotSupportedException>(() => stream.Position);
    }

    [Theory]
    [InlineData("0000", "")]
    [InlineData("000400c08041", "000000000041")]
    [InlineData("0002c181", "0041")]
    [InlineData("0003e08081", "0001")]
    [InlineData("0006eda0bdedb880", "d83dde00")]
    [InlineData("0003eda080", "d800")]
    public void ModifiedUtf8PreservesJavaNulOverlongAndSurrogateRules(string hex, string expectedCodeUnits)
    {
        using var stream = new ByteBufInputStream(Owner(3, Convert.FromHexString(hex)), true);
        // Attribute serialization cannot preserve isolated surrogates; compare UTF-16 units explicitly.
        string actual = stream.ReadUtf();
        Assert.Equal(expectedCodeUnits.Length / 4, actual.Length);
        for (int i = 0; i < actual.Length; ++i)
            Assert.Equal(Convert.ToUInt16(expectedCodeUnits.Substring(i * 4, 4), 16), actual[i]);
        Assert.Equal(0, stream.Available);
    }

    [Theory]
    [InlineData("000180")] [InlineData("0001c0")] [InlineData("0002c041")]
    [InlineData("0002e080")] [InlineData("0003e04180")] [InlineData("0003e08041")]
    [InlineData("0004f09f9880")] [InlineData("0001ff")]
    public void ModifiedUtf8RejectsMalformedPayloadAfterConsumingItsFramedBytes(string hex)
    {
        using var stream = new ByteBufInputStream(Owner(2, Convert.FromHexString(hex)), true);
        Assert.Throws<InvalidDataException>(() => stream.ReadUtf()); Assert.Equal(0, stream.Available);
    }

    [Fact]
    public void TruncatedUtfPayloadConsumesLengthButNotPartialPayload()
    {
        ByteBuf buffer = Unpooled.WrappedBuffer(new byte[] { 0, 3, 65, 66 });
        using var stream = new ByteBufInputStream(buffer, true);
        Assert.Throws<EndOfStreamException>(() => stream.ReadUtf());
        Assert.Equal(2, buffer.ReaderIndex); Assert.Equal(2, stream.Available);
    }

    [Theory]
    [InlineData(2)] [InlineData(4)] [InlineData(8)]
    public void TypedReadsAndReadFullyDoNotConsumeShortPositiveRanges(int width)
    {
        ByteBuf buffer = Owner(3, new byte[8]);
        using var stream = new ByteBufInputStream(buffer, width - 1, true);
        Action read = width switch { 2 => () => stream.ReadInt16(), 4 => () => stream.ReadInt32(), _ => () => stream.ReadDouble() };
        Assert.Throws<EndOfStreamException>(read);
        byte[] destination = new byte[width]; Array.Fill(destination, (byte)9);
        Assert.Throws<EndOfStreamException>(() => stream.ReadFully(destination));
        Assert.All(destination, value => Assert.Equal(9, value));
        Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(width - 1, stream.Available);
    }

    [Fact]
    public void StandardReadExactlyKeepsItsPartialReadContractWhileReadFullyIsAtomic()
    {
        ByteBuf buffer = Unpooled.WrappedBuffer(new byte[] { 1, 2 });
        using var stream = new ByteBufInputStream(buffer, 1, true);
        byte[] destination = { 9, 9 };
        Assert.Throws<EndOfStreamException>(() => stream.ReadExactly(destination));
        Assert.Equal(1, buffer.ReaderIndex); Assert.Equal(new byte[] { 1, 9 }, destination);
        stream.Reset(); Array.Fill(destination, (byte)9);
        Assert.Throws<EndOfStreamException>(() => stream.ReadFully(destination));
        Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(new byte[] { 9, 9 }, destination);
        stream.Flush();
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task StandardCopyBinaryReaderAndCanceledReadsHonorTheCapturedRange(int kind)
    {
        ByteBuf buffer = Owner(kind, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        using var stream = new ByteBufInputStream(buffer, 6, true);
        byte[] destination = { 9, 9, 9 };
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.ReadAsync(destination.AsMemory(), canceled.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.ReadAsync(destination, 0, 3, canceled.Token));
        Assert.Equal(new byte[] { 9, 9, 9 }, destination); Assert.Equal(0, buffer.ReaderIndex);
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        Assert.Equal(0x04030201, reader.ReadInt32()); // BinaryReader independently selects little endian.
        using var output = new MemoryStream(); await stream.CopyToAsync(output, TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 5, 6 }, output.ToArray()); Assert.Equal(6, buffer.ReaderIndex);
        Assert.Equal(0, await stream.ReadAsync(destination.AsMemory(), TestContext.Current.CancellationToken));
        stream.Reset(); Assert.Equal(3, await stream.ReadAsync(destination, 0, 3, TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 1, 2, 3 }, destination);
    }
}
