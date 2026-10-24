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
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public partial class CompositeByteBufBulkKernelTest
{
    private sealed class MetadataTrap : WrappedByteBuf
    {
        public MetadataTrap(ByteBuf buffer) : base(buffer) { }
        public override ReadOnlyMemory<byte> AsReadOnlyMemory(int index, int length)
            => throw new InvalidOperationException("Bulk overlap checks must not call public memory accessors.");
        public override ReadOnlySequence<byte> AsReadOnlySequence(int index, int length)
            => throw new InvalidOperationException("Bulk overlap checks must not call public sequence accessors.");
    }
    private sealed class Wrapper : WrappedByteBuf { public Wrapper(ByteBuf b) : base(b) { } }
    public static IEnumerable<object[]> Aliases()
    {
        foreach (bool direct in new[] { false, true })
        foreach (bool wrapped in new[] { false, true })
        for (int range = 0; range < 3; ++range)
        for (int operation = 0; operation < 6; ++operation) yield return [direct, wrapped, range, operation];
    }
    [Theory]
    [MemberData(nameof(Aliases))]
    public void OverlappingCopiesPreserveSnapshotBytesWithoutPublicMetadataAccess(bool direct, bool wrapped, int range, int operation)
    {
        var native = new NativeMemoryAllocator();
        ByteBuf backing = direct ? new UnpooledDirectByteBuf(32, 32, native) : new UnpooledHeapByteBuf(32);
        ByteBuf owner = null;
        try
        {
            byte[] original = new byte[32]; for (int i = 0; i < 32; ++i) original[i] = (byte)(i + 1);
            backing.WriteBytes(original);
            ByteBuf first = backing.RetainedSlice(0, 16), second = backing.RetainedSlice(16, 16);
            owner = operation >= 4 ? Unpooled.WrappedUnmodifiableBuffer(first, second)
                : new CompositeByteBuf().AddComponent(first, true).AddComponent(second, true);
            ByteBuf aggregate = wrapped ? new Wrapper(new MetadataTrap(owner)) : owner;
            ByteBuf peer = wrapped ? new Wrapper(new MetadataTrap(backing)) : backing;
            (int source, int destination, int length) = range switch { 0 => (0, 4, 20), 1 => (4, 0, 20), _ => (4, 6, 16) };
            byte[] expected = (byte[])original.Clone(); Array.Copy(original, source, expected, destination, length);
            if (operation is 0 or 4) aggregate.GetBytes(source, peer, destination, length);
            else if (operation == 1) aggregate.SetBytes(destination, peer, source, length);
            else if (operation is 2 or 5)
            { aggregate.SetIndex(source, source + length); aggregate.ReadBytes(peer, destination, length); Assert.Equal(source + length, aggregate.ReaderIndex); }
            else
            { aggregate.SetIndex(0, destination); aggregate.WriteBytes(peer, source, length); Assert.Equal(destination + length, aggregate.WriterIndex); }
            Assert.Equal(expected, backing.AsReadOnlyMemory(0, 32).ToArray());
            Assert.Equal(1, owner.ReferenceCount); Assert.Equal(3, backing.ReferenceCount);
        }
        finally
        {
            if (owner?.ReferenceCount > 0) owner.Release();
            if (backing.ReferenceCount > 0) backing.Release();
            Assert.Equal(0, native.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> Outputs()
    {
        foreach (bool direct in new[] { false, true })
        foreach (bool fixedComposite in new[] { false, true })
        for (int endpoint = 0; endpoint < 6; ++endpoint) yield return [direct, fixedComposite, endpoint];
    }
    [Theory]
    [MemberData(nameof(Outputs))]
    public async Task OutputStagingReadsLogicalComponentBytesForStreamFileAndSocket(bool direct, bool fixedComposite, int endpoint)
    {
        var native = new NativeMemoryAllocator(); var observation = new Observation { Fail = false };
        var cancellation = TestContext.Current.CancellationToken;
        ByteBuf first = direct ? new NativeProbe(observation, native) : new HeapProbe(observation);
        ByteBuf second = direct ? new NativeProbe(observation, native) : new HeapProbe(observation);
        ByteBuf owner = null;
        string path = null;
        try
        {
            byte[] a = new byte[32], b = new byte[32];
            for (int i = 0; i < 32; ++i) { a[i] = (byte)(i + 1); b[i] = (byte)(i + 33); }
            first.SetBytes(0, a).SetIndex(5, 21); second.SetBytes(0, b).SetIndex(3, 19);
            owner = fixedComposite ? Unpooled.WrappedUnmodifiableBuffer(first, second)
                : new CompositeByteBuf().AddComponent(first, true).AddComponent(second, true);
            owner.SetIndex(1, 3).MarkReaderIndex().MarkWriterIndex();
            byte[] expected = fixedComposite ? [15, 16, 33, 34] : [20, 21, 36, 37];
            for (int i = 0; i < expected.Length; ++i) expected[i] ^= 0x5A;
            if (endpoint < 2)
            {
                using var output = new MemoryStream();
                if (endpoint == 0) owner.GetBytes(14, output, 4); else await owner.GetBytesAsync(14, output, 4, cancellation);
                Assert.Equal(expected, output.ToArray()); Assert.True(output.CanWrite);
            }
            else if (endpoint < 4)
            {
                path = Path.GetTempFileName();
                using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                int count = endpoint == 2 ? owner.GetBytes(14, handle, 7, 4) : await owner.GetBytesAsync(14, handle, 7, 4, cancellation);
                byte[] actual = new byte[4]; Assert.Equal(4, count); Assert.Equal(4, RandomAccess.Read(handle, actual, 7));
                Assert.Equal(expected, actual); Assert.False(handle.IsClosed);
            }
            else
            {
                var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
                using var sender = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    ValueTask connect = sender.ConnectAsync(listener.LocalEndpoint, cancellation);
                    using Socket receiver = await listener.AcceptSocketAsync(cancellation); await connect;
                    int count = endpoint == 4 ? owner.GetBytes(14, sender, 4) : await owner.GetBytesAsync(14, sender, 4, cancellation);
                    Assert.InRange(count, 1, 4); byte[] actual = new byte[count];
                    for (int offset = 0; offset < count;)
                    { int read = await receiver.ReceiveAsync(actual.AsMemory(offset), SocketFlags.None, cancellation); Assert.True(read > 0); offset += read; }
                    Assert.Equal(expected[..count], actual);
                }
                finally { listener.Stop(); }
            }
            Assert.Equal(2, observation.Visits.Count);
            Assert.All(observation.Visits, visit => { Assert.Equal("GetSpan", visit.Method); Assert.Equal(2, visit.Length); });
            Assert.Equal(1, owner.ReaderIndex); Assert.Equal(3, owner.WriterIndex);
            owner.SetIndex(0, owner.Capacity).ResetWriterIndex().ResetReaderIndex();
            Assert.Equal(1, owner.ReaderIndex); Assert.Equal(3, owner.WriterIndex);
            Assert.Equal(a, first.AsReadOnlyMemory(0, 32).ToArray()); Assert.Equal(b, second.AsReadOnlyMemory(0, 32).ToArray());
            Assert.Equal(1, first.ReferenceCount); Assert.Equal(1, second.ReferenceCount);
        }
        finally
        {
            if (owner?.ReferenceCount > 0) owner.Release();
            if (first.ReferenceCount > 0) first.Release(); if (second.ReferenceCount > 0) second.Release();
            if (path != null) File.Delete(path);
            Assert.Equal(0, native.ReservedBytes);
        }
    }
}
