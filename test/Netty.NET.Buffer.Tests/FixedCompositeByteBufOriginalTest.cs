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

// Declared original fixture contracts. JVM raw capabilities are represented by
// bounded Memory/Sequence and scoped pins; loopback sockets replace NIO channels.
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
            for (int i = _buffers.Count - 1; i >= 0; --i) if (_buffers[i].ReferenceCount > 0) _buffers[i].Release();
            (_allocator as IDisposable)?.Dispose(); Assert.Equal(0, Native.ReservedBytes);
        }
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    public void OriginalReadOnlySettersRejectBeforeInputConsumption(int operation)
    {
        using var s = new Storage(); ByteBuf b = s.Fixed(s.Data(false, new byte[8]));
        ByteBuf source = s.Data(false, new byte[4]); using var stream = new MemoryStream(new byte[4]);
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        void Write()
        {
            switch (operation)
            {
                case 0: b.SetBoolean(0, true); break; case 1: b.SetByte(0, 1); break;
                case 2: b.SetBytes(0, source); break; case 3: b.SetBytes(0, new byte[4].AsSpan()); break;
                case 4: b.SetBytes(0, stream, 4); break; case 5: b.SetBytes(0, socket, 4); break;
                case 6: b.SetChar(0, 'b'); break; case 7: b.SetDouble(0, 1); break;
                case 8: b.SetFloat(0, 1); break; case 9: b.SetInt(0, 1); break;
                case 10: b.SetLong(0, 1); break; default: b.SetMedium(0, 1); break;
            }
        }
        Assert.Throws<NotSupportedException>(() => Write());
        Assert.Equal((0, 8), (b.ReaderIndex, b.WriterIndex));
        Assert.Equal((0, 4), (source.ReaderIndex, source.WriterIndex)); Assert.Equal(0, stream.Position);
        Assert.Equal(new byte[8], b.ReadableSequence.ToArray());
    }
    public static IEnumerable<object[]> GatheringCases()
    {
        for (int scenario = 0; scenario < 3; ++scenario)
        foreach (bool pooled in new[] { false, true })
        for (int kind = 0; kind < 3; ++kind)
        foreach (bool asynchronous in new[] { false, true })
            if (scenario != 2 || !pooled && kind < 2) yield return [scenario, pooled, kind, asynchronous];
    }
    [Theory]
    [MemberData(nameof(GatheringCases))]
    public async Task OriginalGatheringWritesPreserveDataAndActualProgress(int scenario, bool pooled, int kind, bool asynchronous)
    {
        using var s = new Storage(pooled);
        bool firstDirect = kind == 1, secondDirect = kind != 0;
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

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalCopiesIntoHeapAndNativeBuffersAndOutputStreams(bool stream)
    {
        using var s = new Storage(); ByteBuf a = s.Data(true, 97), b = s.Data(false, 98), c = s.Data(true, 99);
        ByteBuf composite = s.Fixed(a, b, c), direct = s.Own(s.Allocator.DirectBuffer(3)), heap = s.Own(s.Allocator.HeapBuffer(3));
        if (stream)
        {
            using var directOutput = new ByteBufOutputStream(direct); using var heapOutput = new ByteBufOutputStream(heap);
            composite.GetBytes(0, directOutput, 3); composite.GetBytes(0, heapOutput, 3);
        }
        else { direct.SetBytes(0, composite, 0, 3); heap.SetBytes(0, composite, 0, 3); direct.WriterIndex = heap.WriterIndex = 3; }
        Assert.Equal(0, ByteBufUtil.Compare(direct, composite)); Assert.Equal(0, ByteBufUtil.Compare(heap, composite));
        Assert.Equal(0, ByteBufUtil.Compare(direct, heap)); Assert.Equal(new byte[] { 97, 98, 99 }, direct.ReadableSequence.ToArray());
        Assert.Equal((0, 3), (composite.ReaderIndex, composite.WriterIndex));
        Assert.All(new[] { a, b, c }, component => Assert.Equal((0, 1), (component.ReaderIndex, component.WriterIndex)));
    }
    [Fact]
    public void OriginalExtractedSegmentsAreThreeSingleByteBorrows()
    {
        using var s = new Storage(); ByteBuf b = s.Fixed(s.Data(true, 97), s.Data(false, 98), s.Data(true, 99));
        ReadOnlySequence<byte> sequence = b.AsReadOnlySequence(0, 3);
        Assert.Equal(3, sequence.ToArray().Length); int segments = 0;
        foreach (ReadOnlyMemory<byte> memory in sequence) { Assert.Equal(1, memory.Length); ++segments; }
        Assert.Equal(3, segments); Assert.Equal(new byte[] { 97, 98, 99 }, sequence.ToArray());
    }
    [Fact]
    public unsafe void OriginalEmptyConstructorHasZeroLengthDirectStorageAndBalancedPins()
    {
        using var s = new Storage(); ByteBuf b = s.Fixed();
        Assert.Equal((0, 0, 0, 0), (b.Capacity, b.MaxCapacity, b.ReaderIndex, b.WriterIndex));
        Assert.True(b.IsDirect); Assert.True(b.IsReadOnly); Assert.True(b.ReadableSequence.IsEmpty);
        // CLR reports actual inputs, without Java's synthetic empty component.
        Assert.Contains("components=0)", b.ToString());
        ReadOnlyMemory<byte> memory = b.AsReadOnlyMemory(0, 0);
        Assert.True(MemoryMarshal.TryGetArray(memory, out var array)); Assert.Equal(0, array.Count);
        using var pin = memory.Pin(); Assert.Equal(nint.Zero, (nint)pin.Pointer);
        // CLR has no permanent raw zero-length address capability or JVM assumption gate.
    }
    [Theory]
    [InlineData(false, 0)] [InlineData(false, 10)] [InlineData(true, 0)] [InlineData(true, 10)]
    public unsafe void OriginalSingleInputCapabilitiesUseBoundedArraysAndScopedNativePins(bool direct, int written)
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
    [Theory]
    [InlineData(false, 0)] [InlineData(false, 2)] [InlineData(true, 0)] [InlineData(true, 2)]
    public void OriginalMultipleInputCapabilitiesRequireSegmentsForNonemptyRanges(bool direct, int written)
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
