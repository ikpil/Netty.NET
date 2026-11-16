/*
 * Copyright 2013 The Netty Project
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// Declared original fixture contracts, one CLR test per original test method. JVM raw capabilities are
// represented by bounded Memory/Sequence and scoped pins; loopback sockets replace NIO channels.
public class FixedCompositeByteBufOriginalTest
{
    private sealed class Storage(bool pooled = false) : IDisposable
    {
        internal readonly NativeMemoryAllocator Native = new();
        private IByteBufAllocator _allocator;
        private readonly List<ByteBuf> _buffers = [];
        internal IByteBufAllocator Allocator => _allocator ??= pooled
            ? new PooledByteBufAllocator(false, maxOrder: 4, nativeAllocator: Native)
            : new UnpooledByteBufAllocator(false, Native);
        internal T Own<T>(T b) where T : ByteBuf { _buffers.Add(b); return b; }
        internal ByteBuf Data(bool direct, params byte[] bytes)
            => Own((direct ? Allocator.DirectBuffer(256) : Allocator.HeapBuffer(256)).WriteBytes(bytes));
        internal ByteBuf Fixed(params ByteBuf[] inputs) => Own(new FixedCompositeByteBuf(inputs));
        public void Dispose()
        {
            // Composite/fixed owners were registered last and consume input claims.
            for (int i = _buffers.Count - 1; i >= 0; --i) if (_buffers[i].RefCnt > 0) _buffers[i].Release();
            (_allocator as IDisposable)?.Dispose(); Assert.Equal(0, Native.ReservedBytes);
        }
    }

    [Fact]
    public void TestSetBoolean() => AssertReadOnlySetter((buf, _, _, _) => buf.SetBoolean(0, true));

    [Fact]
    public void TestSetByte() => AssertReadOnlySetter((buf, _, _, _) => buf.SetByte(0, 1));

    [Fact]
    public void TestSetBytesWithByteBuf() => AssertReadOnlySetter((buf, src, _, _) => buf.SetBytes(0, src));

    [Fact]
    public void TestSetBytesWithByteBuffer() => AssertReadOnlySetter((buf, _, _, _) => buf.SetBytes(0, new byte[4].AsSpan()));

    [Fact]
    public void TestSetBytesWithInputStream() => AssertReadOnlySetter((buf, _, stream, _) => buf.SetBytes(0, stream, 4));

    [Fact]
    public void TestSetBytesWithChannel() => AssertReadOnlySetter((buf, _, _, channel) => buf.SetBytes(0, channel, 4));

    [Fact]
    public void TestSetChar() => AssertReadOnlySetter((buf, _, _, _) => buf.SetChar(0, 'b'));

    [Fact]
    public void TestSetDouble() => AssertReadOnlySetter((buf, _, _, _) => buf.SetDouble(0, 1));

    [Fact]
    public void TestSetFloat() => AssertReadOnlySetter((buf, _, _, _) => buf.SetFloat(0, 1));

    [Fact]
    public void TestSetInt() => AssertReadOnlySetter((buf, _, _, _) => buf.SetInt(0, 1));

    [Fact]
    public void TestSetLong() => AssertReadOnlySetter((buf, _, _, _) => buf.SetLong(0, 1));

    [Fact]
    public void TestSetMedium() => AssertReadOnlySetter((buf, _, _, _) => buf.SetMedium(0, 1));

    // Every original setter test: one heap input, assertThrows(ReadOnlyBufferException). CLR NotSupportedException
    // replaces ReadOnlyBufferException, Span replaces ByteBuffer and Socket replaces ScatteringByteChannel; the CLR
    // test also checks that the rejected call consumed no input and moved no cursor.
    private static void AssertReadOnlySetter(Action<ByteBuf, ByteBuf, MemoryStream, Socket> write)
    {
        using var s = new Storage(); ByteBuf buf = s.Fixed(s.Data(false, new byte[8]));
        ByteBuf src = s.Data(false, new byte[4]); using var stream = new MemoryStream(new byte[4]);
        using var channel = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        Assert.Throws<NotSupportedException>(() => write(buf, src, stream, channel));
        Assert.Equal((0, 8), (buf.ReaderIndex, buf.WriterIndex));
        Assert.Equal((0, 4), (src.ReaderIndex, src.WriterIndex)); Assert.Equal(0, stream.Position);
        Assert.Equal(new byte[8], buf.ReadableSequence.ToArray());
    }

    // CLR: each gathering test runs the synchronous and the asynchronous Socket transfer.
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task TestGatheringWritesHeap(bool asynchronous) => TestGatheringWrites(false, false, false, asynchronous);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task TestGatheringWritesDirect(bool asynchronous) => TestGatheringWrites(false, true, true, asynchronous);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task TestGatheringWritesMixes(bool asynchronous) => TestGatheringWrites(false, false, true, asynchronous);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task TestGatheringWritesHeapPooled(bool asynchronous) => TestGatheringWrites(true, false, false, asynchronous);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task TestGatheringWritesDirectPooled(bool asynchronous) => TestGatheringWrites(true, true, true, asynchronous);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task TestGatheringWritesMixesPooled(bool asynchronous) => TestGatheringWrites(true, false, true, asynchronous);

    // Original testGatheringWrites(buf1, buf2): the scenario builds a mutable CompositeByteBuf, as the source does.
    private static Task TestGatheringWrites(bool pooled, bool direct1, bool direct2, bool asynchronous)
        => GatheringWrites(0, pooled, direct1, direct2, asynchronous);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task TestGatheringWritesPartialHeap(bool asynchronous) => TestGatheringWritesPartial(false, false, false, asynchronous);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task TestGatheringWritesPartialDirect(bool asynchronous) => TestGatheringWritesPartial(false, true, true, asynchronous);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task TestGatheringWritesPartialMixes(bool asynchronous) => TestGatheringWritesPartial(false, false, true, asynchronous);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task TestGatheringWritesPartialHeapPooled(bool asynchronous) => TestGatheringWritesPartial(true, false, false, asynchronous);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task TestGatheringWritesPartialDirectPooled(bool asynchronous) => TestGatheringWritesPartial(true, true, true, asynchronous);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task TestGatheringWritesPartialMixesPooled(bool asynchronous) => TestGatheringWritesPartial(true, false, true, asynchronous);

    private static Task TestGatheringWritesPartial(bool pooled, bool direct1, bool direct2, bool asynchronous)
        => GatheringWrites(1, pooled, direct1, direct2, asynchronous);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task TestGatheringWritesSingleHeap(bool asynchronous) => TestGatheringWritesSingleBuf(false, asynchronous);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task TestGatheringWritesSingleDirect(bool asynchronous) => TestGatheringWritesSingleBuf(true, asynchronous);

    private static Task TestGatheringWritesSingleBuf(bool direct1, bool asynchronous)
        => GatheringWrites(2, false, direct1, false, asynchronous);

    // Scenario 0 = testGatheringWrites, 1 = testGatheringWritesPartial, 2 = testGatheringWritesSingleBuf.
    private static async Task GatheringWrites(int scenario, bool pooled, bool firstDirect, bool secondDirect, bool asynchronous)
    {
        using var s = new Storage(pooled);
        ByteBuf first = s.Data(firstDirect, scenario == 0 ? new byte[] { 1, 2 } : new byte[] { 1, 2, 3, 4 });
        ByteBuf second = scenario == 2 ? null : s.Data(secondDirect, scenario == 0 ? new byte[] { 1, 2 } : new byte[] { 1, 2, 3, 4 });
        ByteBuf b;
        if (scenario == 0)
            b = s.Own(new CompositeByteBuf(s.Allocator, false)).AddComponent(first).AddComponent(second).SetIndex(1, 3);
        else b = s.Fixed(scenario == 2 ? [first] : [first, second]);
        if (scenario == 2) b.ReaderIndex = 1;
        b.MarkReaderIndex().MarkWriterIndex(); int reader = b.ReaderIndex, writer = b.WriterIndex;
        byte[] expected = scenario == 1 ? new byte[] { 1, 2, 3, 4, 1, 2, 3, 4 } : scenario == 0 ? [2, 1] : [2, 3];
        using var pair = new Pair(); int sent = 0;
        while (sent < expected.Length)
        {
            // Socket is sealed and has no Java test-channel write quota. Bounded
            // one-byte requests test the original caller progress loop deterministically;
            // this does not claim that an 8-byte OS send returned only one byte.
            int request = scenario == 1 ? 1 : expected.Length - sent;
            int count = asynchronous ? await b.ReadBytesAsync(pair.Local, request, TestContext.Current.CancellationToken)
                : b.ReadBytes(pair.Local, request);
            Assert.InRange(count, 1, request); sent += count;
            Assert.Equal(reader + sent, b.ReaderIndex); Assert.Equal(writer, b.WriterIndex);
        }
        Assert.Equal(expected, pair.Receive(expected.Length));
        Assert.Equal(expected, ByteBufUtil.GetBytes(b, reader, expected.Length));
        Assert.Equal(0, first.ReaderIndex); if (second != null) Assert.Equal(0, second.ReaderIndex);
        b.ResetReaderIndex().ResetWriterIndex(); Assert.Equal((reader, writer), (b.ReaderIndex, b.WriterIndex));
    }

    [Fact]
    public void TestCopyingToOtherBuffer()
    {
        using var s = new Storage(); ByteBuf buf1 = s.Data(true, 97), buf2 = s.Data(false, 98), buf3 = s.Data(true, 99);
        ByteBuf composite = s.Fixed(buf1, buf2, buf3);
        ByteBuf copy = s.Own(s.Allocator.DirectBuffer(3)), copy2 = s.Own(s.Allocator.HeapBuffer(3));
        copy.SetBytes(0, composite, 0, 3);
        copy2.SetBytes(0, composite, 0, 3);
        copy.WriterIndex = 3;
        copy2.WriterIndex = 3;
        Assert.Equal(0, ByteBufUtil.Compare(copy, composite));
        Assert.Equal(0, ByteBufUtil.Compare(copy2, composite));
        Assert.Equal(0, ByteBufUtil.Compare(copy, copy2));
        AssertCopiedWithoutConsumingInputs(copy, composite, buf1, buf2, buf3);
    }

    [Fact]
    public void TestCopyingToOutputStream()
    {
        using var s = new Storage(); ByteBuf buf1 = s.Data(true, 97), buf2 = s.Data(false, 98), buf3 = s.Data(true, 99);
        ByteBuf composite = s.Fixed(buf1, buf2, buf3);
        ByteBuf copy = s.Own(s.Allocator.DirectBuffer(3)), copy2 = s.Own(s.Allocator.HeapBuffer(3));
        using (var copyStream = new ByteBufOutputStream(copy))
        using (var copy2Stream = new ByteBufOutputStream(copy2))
        {
            composite.GetBytes(0, copyStream, 3);
            composite.GetBytes(0, copy2Stream, 3);
            Assert.Equal(0, ByteBufUtil.Compare(copy, composite));
            Assert.Equal(0, ByteBufUtil.Compare(copy2, composite));
            Assert.Equal(0, ByteBufUtil.Compare(copy, copy2));
        }
        AssertCopiedWithoutConsumingInputs(copy, composite, buf1, buf2, buf3);
    }

    // CLR: the copy tests also check the copied bytes and that neither the composite nor its inputs moved.
    private static void AssertCopiedWithoutConsumingInputs(ByteBuf copy, ByteBuf composite, params ByteBuf[] inputs)
    {
        Assert.Equal(new byte[] { 97, 98, 99 }, copy.ReadableSequence.ToArray());
        Assert.Equal((0, 3), (composite.ReaderIndex, composite.WriterIndex));
        Assert.All(inputs, component => Assert.Equal((0, 1), (component.ReaderIndex, component.WriterIndex)));
    }

    [Fact]
    public void TestExtractNioBuffers()
    {
        // CLR: ReadOnlySequence segments replace the NIO ByteBuffer array.
        using var s = new Storage(); ByteBuf b = s.Fixed(s.Data(true, 97), s.Data(false, 98), s.Data(true, 99));
        ReadOnlySequence<byte> sequence = b.AsReadOnlySequence(0, 3);
        Assert.Equal(3, sequence.ToArray().Length); int segments = 0;
        foreach (ReadOnlyMemory<byte> memory in sequence) { Assert.Equal(1, memory.Length); ++segments; }
        Assert.Equal(3, segments); Assert.Equal(new byte[] { 97, 98, 99 }, sequence.ToArray());
    }

    [Fact]
    public void TestEmptyArray()
    {
        using var s = new Storage(); ByteBuf buf = s.Fixed();
        // CLR: the empty fixed buffer also reports zero capacity and indices, and the CLR reports the actual
        // inputs without Java's synthetic empty component.
        Assert.Equal((0, 0, 0, 0), (buf.Capacity, buf.MaxCapacity, buf.ReaderIndex, buf.WriterIndex));
        Assert.True(buf.IsReadOnly); Assert.True(buf.ReadableSequence.IsEmpty);
        Assert.Contains("components=0)", buf.ToString());
    }

    [Theory]
    [InlineData(0)] [InlineData(10)]
    public void TestHasMemoryAddressWithSingleBuffer(int written) => SingleInputCapabilities(true, written);

    [Fact]
    public unsafe void TestHasMemoryAddressWhenEmpty()
    {
        // CLR has no permanent raw zero-length address capability or JVM assumption gate: a scoped pin of the
        // empty range has the same (null) address as the empty buffer's.
        using var s = new Storage(); ByteBuf buf = s.Fixed();
        using var pin = buf.AsReadOnlyMemory(0, 0).Pin();
        using var emptyPin = Unpooled.EmptyBuffer.AsReadOnlyMemory(0, 0).Pin();
        Assert.Equal((nint)emptyPin.Pointer, (nint)pin.Pointer);
        Assert.Equal(nint.Zero, (nint)pin.Pointer);
    }

    [Theory]
    [InlineData(0)] [InlineData(2)]
    public void TestHasNoMemoryAddressWhenMultipleBuffers(int written) => MultipleInputCapabilities(true, written);

    [Theory]
    [InlineData(0)] [InlineData(10)]
    public void TestHasArrayWithSingleBuffer(int written) => SingleInputCapabilities(false, written);

    [Fact]
    public void TestHasArrayWhenEmptyAndIsDirect()
    {
        using var s = new Storage(); ByteBuf buf = s.Fixed();
        Assert.True(MemoryMarshal.TryGetArray(buf.AsReadOnlyMemory(0, 0), out var array)); Assert.Equal(0, array.Count);
        Assert.Equal(Unpooled.EmptyBuffer.IsDirect, buf.IsDirect);
        Assert.True(buf.IsDirect);
    }

    [Theory]
    [InlineData(0)] [InlineData(2)]
    public void TestHasNoArrayWhenMultipleBuffers(int written) => MultipleInputCapabilities(false, written);

    // CLR: the original single-buffer capability tests use an unwritten 10-byte buffer; the CLR test checks the
    // empty and the 10-byte readable range through bounded arrays (heap) and scoped pins (native).
    private static unsafe void SingleInputCapabilities(bool direct, int written)
    {
        using var s = new Storage(); ByteBuf source = s.Data(direct, new byte[written]); ByteBuf b = s.Fixed(source);
        ReadOnlyMemory<byte> memory = b.AsReadOnlyMemory(0, written);
        Assert.Equal(written, memory.Length); Assert.Equal(direct, b.IsDirect);
        if (written == 0) { Assert.True(memory.IsEmpty); using var zero = memory.Pin(); Assert.Equal(nint.Zero, (nint)zero.Pointer); return; }
        if (!direct)
        {
            Assert.True(MemoryMarshal.TryGetArray(source.AsReadOnlyMemory(0, written), out var original));
            Assert.True(MemoryMarshal.TryGetArray(memory, out var array)); Assert.Same(original.Array, array.Array);
            Assert.Equal((original.Offset, written), (array.Offset, array.Count));
        }
        else
        {
            using var original = source.AsReadOnlyMemory(0, written).Pin(); using var pin = memory.Pin();
            Assert.Equal((nint)original.Pointer, (nint)pin.Pointer); Assert.NotEqual(nint.Zero, (nint)pin.Pointer);
        }
    }

    // CLR: two inputs have no single array or address; a nonempty range is rejected as one Memory and is read as
    // two segments instead. The empty range stays available.
    private static void MultipleInputCapabilities(bool direct, int written)
    {
        using var s = new Storage(); ByteBuf b = s.Fixed(s.Data(direct, new byte[written]), s.Data(direct, new byte[written]));
        if (written == 0) { Assert.True(b.AsReadOnlyMemory(0, 0).IsEmpty); return; }
        Assert.Throws<NotSupportedException>(() => b.AsReadOnlyMemory(0, written * 2));
        int segments = 0;
        foreach (ReadOnlyMemory<byte> memory in b.ReadableSequence)
        {
            Assert.Equal(written, memory.Length);
            Assert.Equal(!direct, MemoryMarshal.TryGetArray(memory, out _));
            using var pin = memory.Pin(); ++segments;
        }
        Assert.Equal(2, segments);
    }

    private sealed class Pair : IDisposable
    {
        internal readonly Socket Local, Peer;
        internal Pair()
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0)); listener.Listen(1);
            Local = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp); Local.Connect(listener.LocalEndPoint);
            Peer = listener.Accept(); Local.SendTimeout = Local.ReceiveTimeout = Peer.SendTimeout = Peer.ReceiveTimeout = 5000;
        }
        internal byte[] Receive(int length)
        {
            byte[] bytes = new byte[length];
            for (int offset = 0; offset < length;)
            { int count = Peer.Receive(bytes.AsSpan(offset)); Assert.True(count > 0); offset += count; }
            return bytes;
        }
        public void Dispose() { Local.Dispose(); Peer.Dispose(); }
    }
}
