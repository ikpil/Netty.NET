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
using System.Linq;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// Selected stream-transfer scenarios from pinned AbstractByteBufTest and CLR contracts.
public class ByteBufStreamTransferTest
{
    private static ByteBuf Owner(int kind)
    {
        if (kind == 0) return Unpooled.Buffer(32, 4096).SetZero(0, 32);
        if (kind == 1) return Unpooled.DirectBuffer(32, 4096).SetZero(0, 32);
        return Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(new byte[7]))
            .AddComponent(Unpooled.WrappedBuffer(Array.Empty<byte>()))
            .AddComponent(Unpooled.WrappedBuffer(new byte[25]));
    }

    private static byte[] Bytes(ByteBuf b, int index, int length)
        => b.AsReadOnlySequence(index, length).ToArray();

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void OriginalAbsoluteBlockTransferPreservesIndices(int kind)
    {
        ByteBuf b = Owner(kind);
        try
        {
            byte[] expected = Enumerable.Range(0, 32).Select(i => (byte)(i * 7)).ToArray();
            using var output = new MemoryStream();
            for (int i = 0; i < 32; i += 8)
            {
                using var input = new MemoryStream(expected, i, 8);
                Assert.Equal(8, b.SetBytes(i, input, 8));
                Assert.Equal(0, b.SetBytes(i, input, 0)); // CLR empty request does not query EOF.
                Assert.Same(b, b.GetBytes(i, output, 8));
            }
            Assert.Equal(expected, output.ToArray());
            Assert.Equal(0, b.ReaderIndex); Assert.Equal(0, b.WriterIndex);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void OriginalRelativeBlockTransferAdvancesBothIndices(int kind)
    {
        ByteBuf b = Owner(kind);
        try
        {
            byte[] expected = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
            for (int i = 0; i < 32; i += 8)
            {
                using var input = new MemoryStream(expected, i, 8);
                Assert.Equal(8, b.WriteBytes(input, 8)); Assert.Equal(i + 8, b.WriterIndex);
            }
            using var output = new MemoryStream();
            for (int i = 0; i < 32; i += 8)
            {
                Assert.Same(b, b.ReadBytes(output, 8)); Assert.Equal(i + 8, b.ReaderIndex);
            }
            Assert.Equal(expected, output.ToArray()); Assert.Equal(1, b.ReferenceCount);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void ShortReadAndEofUseOneBoundedReadAcrossAllStorage(int kind)
    {
        ByteBuf b = Owner(kind); b.WriterIndex = 3;
        using var input = new ProbeStream { ReadCount = 2 };
        try
        {
            Assert.Equal(2, b.WriteBytes(input, 20)); Assert.Equal(1, input.Calls);
            Assert.Equal(5, b.WriterIndex); Assert.Equal(0, b.ReaderIndex);
            Assert.Equal(new byte[] { 1, 2 }, Bytes(b, 3, 2));
            Assert.Equal(new byte[15], Bytes(b, 5, 15));
            input.ReadCount = 0;
            Assert.Equal(0, b.WriteBytes(input, 20)); Assert.Equal(5, b.WriterIndex);
            Assert.Equal(2, input.Calls); Assert.False(input.Disposed);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void ReadFailureDoesNotPublishStagingOrAdvanceWriter(int kind)
    {
        ByteBuf b = Owner(kind); b.WriterIndex = 3;
        using var input = new ProbeStream { ReadCount = 2, Fail = true };
        try
        {
            Assert.Throws<IOException>(() => b.WriteBytes(input, 20));
            Assert.Equal(new byte[32], Bytes(b, 0, 32)); Assert.Equal(3, b.WriterIndex);
            Assert.Equal(1, input.Calls); Assert.False(input.Disposed);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void ThrowingOutputCanAcceptPrefixButReaderStaysUnchanged(int kind)
    {
        ByteBuf b = Owner(kind).WriteBytes(new byte[] { 9, 8, 7, 6 }); b.ReaderIndex = 1;
        using var output = new ProbeStream { Fail = true };
        try
        {
            Assert.Throws<IOException>(() => b.ReadBytes(output, 3));
            Assert.Equal(new byte[] { 8 }, output.Accepted);
            Assert.Equal(1, b.ReaderIndex); Assert.Equal(4, b.WriterIndex);
            Assert.Equal(1, output.Calls); Assert.False(output.Disposed);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void EmptyAndInvalidRequestsDoNotCallStream(int kind)
    {
        ByteBuf b = Owner(kind); using var stream = new ProbeStream();
        try
        {
            Assert.Same(b, b.GetBytes(32, stream, 0)); Assert.Equal(0, b.SetBytes(32, stream, 0));
            Assert.Same(b, b.ReadBytes(stream, 0)); Assert.Equal(0, b.WriteBytes(stream, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => b.GetBytes(33, stream, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => b.SetBytes(1, stream, int.MaxValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => b.ReadBytes(stream, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => b.WriteBytes(stream, -1));
            Assert.Throws<ArgumentNullException>(() => b.GetBytes(0, (Stream)null, 0));
            Assert.Throws<ArgumentNullException>(() => b.SetBytes(0, (Stream)null, 0));
            Assert.Throws<ArgumentNullException>(() => b.ReadBytes((Stream)null, 0));
            Assert.Throws<ArgumentNullException>(() => b.WriteBytes((Stream)null, 0));
            Assert.Equal(0, stream.Calls);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void CapacityIsReservedEvenWhenSourceIsAtEof(int kind)
    {
        ByteBuf b = kind == 0 ? Unpooled.Buffer(0, 64) : Unpooled.DirectBuffer(0, 64);
        using var input = new ProbeStream { ReadCount = 0 };
        try
        {
            Assert.Equal(0, b.WriteBytes(input, 40)); Assert.Equal(64, b.Capacity);
            Assert.Equal(0, b.WriterIndex);
            Assert.Throws<ArgumentOutOfRangeException>(() => b.WriteBytes(input, 65));
            Assert.Equal(1, input.Calls);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(-1)] [InlineData(33)]
    public void InvalidStreamReadCountsAreRejectedBeforePublishing(int count)
    {
        ByteBuf b = Owner(1); using var input = new ProbeStream { ReadCount = count };
        try
        {
            Assert.Throws<IOException>(() => b.WriteBytes(input, 32));
            Assert.Equal(0, b.WriterIndex); Assert.Equal(new byte[32], Bytes(b, 0, 32));
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void OriginalReleasedOperationsAndReadonlyWritesRejectEvenEmptyRequests(int kind)
    {
        ByteBuf b = Owner(kind).WriteBytes(new byte[] { 1, 2 });
        ByteBuf ro = b.AsReadOnly(); using var stream = new ProbeStream();
        Assert.Throws<NotSupportedException>(() => ro.SetBytes(0, stream, 0));
        Assert.Throws<NotSupportedException>(() => ro.WriteBytes(stream, 0));
        ro.ReadBytes(stream, 2); Assert.Equal(new byte[] { 1, 2 }, stream.Accepted);
        Assert.Equal(0, b.ReaderIndex); b.Release();
        Assert.Throws<IllegalReferenceCountException>(() => b.GetBytes(0, stream, 0));
        Assert.Throws<IllegalReferenceCountException>(() => b.SetBytes(0, stream, 0));
        Assert.Throws<IllegalReferenceCountException>(() => b.ReadBytes(stream, 0));
        Assert.Throws<IllegalReferenceCountException>(() => b.WriteBytes(stream, 0));
    }

    [Fact]
    public void EmptySingletonValidatesRangesAndBorrowsStreams()
    {
        using var stream = new ProbeStream(); ByteBuf b = Unpooled.EmptyBuffer;
        Assert.Equal(0, b.WriteBytes(stream, 0)); Assert.Equal(0, b.SetBytes(0, stream, 0));
        b.ReadBytes(stream, 0); b.GetBytes(0, stream, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => b.SetBytes(1, stream, 0));
        Assert.Equal(0, stream.Calls); Assert.False(stream.Disposed);
    }

    [Fact]
    public void ViewsRespectOffsetsAndUnreleasableWriterSharesIndices()
    {
        ByteBuf b = Owner(0); ByteBuf slice = b.Slice(4, 10).Clear();
        using var input = new MemoryStream(new byte[] { 7, 8 });
        using var output = new MemoryStream();
        try
        {
            Assert.Equal(2, slice.WriteBytes(input, 2)); Assert.Equal(0, b.WriterIndex);
            slice.ReadBytes(output, 2); Assert.Equal(new byte[] { 7, 8 }, output.ToArray());
            Assert.Equal(new byte[] { 7, 8 }, Bytes(b, 4, 2));
            ByteBuf wrapper = Unpooled.UnreleasableBuffer(b);
            using var more = new MemoryStream(new byte[] { 9 });
            Assert.Equal(1, wrapper.WriteBytes(more, 1)); Assert.Equal(1, b.WriterIndex);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OutputSnapshotSurvivesNativeGrowthOrCompositeConsolidation(bool composite)
    {
        ByteBuf b;
        if (composite)
        {
            var c = Unpooled.CompositeBuffer(2);
            c.AddComponent(Unpooled.DirectBuffer(2, 2).WriteBytes(new byte[] { 1, 2 }), true);
            c.AddComponent(Unpooled.DirectBuffer(2, 2).WriteBytes(new byte[] { 3, 4 }), true);
            b = c;
        }
        else b = Unpooled.DirectBuffer(4, 64).WriteBytes(new byte[] { 1, 2, 3, 4 });
        using var output = new ByteBufOutputStream(b);
        try
        {
            b.ReadBytes(output, 4);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 1, 2, 3, 4 }, Bytes(b, 0, 8));
            Assert.Equal(4, b.ReaderIndex); Assert.Equal(8, b.WriterIndex);
        }
        finally { b.Release(); }
    }

    [Fact]
    public void InputStagingSurvivesNativeDestinationResizeInCallback()
    {
        ByteBuf b = Unpooled.DirectBuffer(4, 64);
        using var input = new ProbeStream { ReadCount = 3, Callback = () => b.Capacity = 64 };
        try
        {
            Assert.Equal(3, b.WriteBytes(input, 4)); Assert.Equal(64, b.Capacity);
            Assert.Equal(new byte[] { 1, 2, 3 }, Bytes(b, 0, 3)); Assert.Equal(3, b.WriterIndex);
        }
        finally { b.Release(); }
    }

    [Fact]
    public void FixedReadonlyCompositeTransfersAcrossComponentsAndRejectsInput()
    {
        ByteBuf b = Unpooled.WrappedUnmodifiableBuffer(
            Unpooled.WrappedBuffer(new byte[] { 1, 2 }),
            Unpooled.DirectBuffer(2, 2).WriteBytes(new byte[] { 3, 4 }));
        using var output = new MemoryStream(); using var input = new ProbeStream();
        try
        {
            b.ReaderIndex = 1; b.GetBytes(0, output, 4);
            b.ReadBytes(output, 3);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 2, 3, 4 }, output.ToArray());
            Assert.Equal(4, b.ReaderIndex);
            Assert.Throws<NotSupportedException>(() => b.SetBytes(0, input, 0));
            Assert.Throws<NotSupportedException>(() => b.WriteBytes(input, 0));
            Assert.Equal(0, input.Calls);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void InputAdapterCanReadAndAppendToSameGrowingBuffer(int kind)
    {
        ByteBuf b = Owner(kind).WriteBytes(new byte[] { 1, 2, 3, 4 });
        b.Capacity = 4;
        using var input = new ByteBufInputStream(b);
        try
        {
            Assert.Equal(4, b.WriteBytes(input, 4));
            Assert.Equal(new byte[] { 1, 2, 3, 4, 1, 2, 3, 4 }, Bytes(b, 0, 8));
            Assert.Equal(4, b.ReaderIndex); Assert.Equal(8, b.WriterIndex);
            Assert.Equal(0, input.Available); Assert.Equal(1, b.ReferenceCount);
        }
        finally { b.Release(); }
    }

    [Fact]
    public void CompositeCommitFailureCanChangePrefixWithoutAdvancingWriter()
    {
        ByteBuf b = Unpooled.CompositeBuffer()
            .AddComponent(Unpooled.WrappedBuffer(new byte[2]))
            .AddComponent(Unpooled.WrappedBuffer(new byte[2]).AsReadOnly());
        using var input = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        try
        {
            Assert.Throws<NotSupportedException>(() => b.WriteBytes(input, 4));
            Assert.Equal(0, b.WriterIndex); Assert.Equal(4, input.Position);
            Assert.Equal(new byte[] { 1, 2, 0, 0 }, Bytes(b, 0, 4));
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalIndependentDuplicateAndSliceOutputTransfersAreConcurrent(bool slice)
    {
        ByteBuf b = Owner(1).WriteBytes(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
        try
        {
            Parallel.For(0, 1000, _ =>
            {
                ByteBuf view = slice ? b.Slice(3, 20) : b.Duplicate().SetIndex(3, 23);
                using var output = new MemoryStream(); view.ReadBytes(output, 20);
                Assert.Equal(Enumerable.Range(3, 20).Select(i => (byte)i).ToArray(), output.ToArray());
            });
            Assert.Equal(0, b.ReaderIndex); Assert.Equal(32, b.WriterIndex);
        }
        finally { b.Release(); }
    }

    private sealed class ProbeStream : Stream
    {
        public int Calls;
        public int ReadCount = 1;
        public bool Fail;
        public bool Disposed;
        public Action Callback;
        public byte[] Accepted;
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(Span<byte> destination)
        {
            Calls++;
            for (int i = 0; i < Math.Min(destination.Length, Math.Max(0, ReadCount)); i++)
                destination[i] = (byte)(i + 1);
            Callback?.Invoke();
            if (Fail) throw new IOException("Input failure after touching staging.");
            return ReadCount;
        }
        public override void Write(ReadOnlySpan<byte> source)
        {
            Calls++; Callback?.Invoke(); Accepted = (Fail ? source[..1] : source).ToArray();
            if (Fail) throw new IOException("Output failure after accepting a prefix.");
        }
        public override int Read(byte[] b, int offset, int count) => Read(b.AsSpan(offset, count));
        public override void Write(byte[] b, int offset, int count) => Write(b.AsSpan(offset, count));
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
