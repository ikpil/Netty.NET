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
using System.Collections.Generic;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class CompositeByteBufBulkKernelTest
{
    private sealed class ProbeFailure : Exception { }
    private sealed class Observation
    {
        public bool Fail = true;
        public int FailAt = 1;
        public readonly List<(string Method, int Index, int Length, ByteBuf Other)> Visits = [];
        public void Visit(string method, int index, int length, ByteBuf other = null)
        {
            Visits.Add((method, index, length, other));
            if (Fail && Visits.Count >= FailAt)
                throw new ProbeFailure();
        }
    }
    private sealed class HeapProbe : UnpooledHeapByteBuf
    {
        private readonly Observation _observation;
        public HeapProbe(Observation observation) : base(32)
        {
            _observation = observation;
        }

        public override ByteBuf GetBytes(int index, Span<byte> destination)
        {
            _observation.Visit("GetSpan", index, destination.Length);
            base.GetBytes(index, destination);

            for (int i = 0; i < destination.Length; ++i)
            {
                destination[i] ^= 0x5A;
            }

            return this;
        }

        public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
        {
            _observation.Visit("GetBuffer", index, length, destination);
            byte[] bytes = AsReadOnlyMemory(index, length).ToArray();

            for (int i = 0; i < bytes.Length; ++i)
                bytes[i] ^= 0x5A;

            destination.SetBytes(destinationIndex, bytes);
            return this;
        }

        public override ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
        {
            _observation.Visit("SetBuffer", index, length, source);

            return base.SetBytes(index, source, sourceIndex, length);
        }
    }
    private sealed class NativeProbe : UnpooledDirectByteBuf
    {
        private readonly Observation _observation;
        public NativeProbe(Observation observation, NativeMemoryAllocator native) : base(32, 32, native)
        {
            _observation = observation;
        }

        public override ByteBuf GetBytes(int index, Span<byte> destination)
        {
            _observation.Visit("GetSpan", index, destination.Length);
            base.GetBytes(index, destination);

            for (int i = 0; i < destination.Length; ++i)
            {
                destination[i] ^= 0x5A;
            }

            return this;
        }

        public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
        {
            _observation.Visit("GetBuffer", index, length, destination);
            byte[] bytes = AsReadOnlyMemory(index, length).ToArray();

            for (int i = 0; i < bytes.Length; ++i)
                bytes[i] ^= 0x5A;

            destination.SetBytes(destinationIndex, bytes);
            return this;
        }

        public override ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
        {
            _observation.Visit("SetBuffer", index, length, source);

            return base.SetBytes(index, source, sourceIndex, length);
        }
    }
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool direct in new[] { false, true })
        for (int form = 0; form < 7; ++form)
        for (int operation = 0; operation < 9; ++operation)
        foreach (bool boundary in new[] { false, true })
                    {
                        yield return [direct, form, operation, boundary];
                    }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void BulkRequestsReachTheSelectedComponentOverload(bool direct, int form, int operation, bool boundary)
    {
        var native = new NativeMemoryAllocator();
        var observation = new Observation();
        ByteBuf first = direct ? new NativeProbe(observation, native) : new HeapProbe(observation);
        ByteBuf second = direct ? new NativeProbe(observation, native) : new HeapProbe(observation);
        ByteBuf owner = null, retained = null;
        ByteBuf external = new UnpooledHeapByteBuf(32);

        try
        {
            byte[] a = new byte[32], b = new byte[32];

            for (int i = 0; i < 32; ++i)
            {
                a[i] = (byte)(i + 1);
                b[i] = (byte)(i + 33);
            }

            first.SetBytes(0, a).SetIndex(5, 21);
            second.SetBytes(0, b).SetIndex(3, 19);
            external.SetIndex(2, 12);
            owner = form == 4 ? Unpooled.WrappedUnmodifiableBuffer(first, second)
                : new CompositeByteBuf().AddComponent(first, true).AddComponent(second, true);
            ByteBuf caller = form switch
            {
                1 => owner.Slice(4, 24),
                2 => owner.Duplicate(),
                3 => owner.AsReadOnly(),
                5 => PooledDerivedByteBuf.NewSlice(owner, owner, 4, 24),
                6 => PooledDerivedByteBuf.NewDuplicate(owner, owner, 0, 32),
                _ => owner
            };

            if (form >= 5)
                retained = caller;

            int rootIndex = boundary ? 15 : 8, index = rootIndex - (form is 1 or 5 ? 4 : 0);
            bool relativeRead = operation is 4 or 5, relativeWrite = operation == 6;

            if (relativeRead)
                caller.SetIndex(index, index + 4);
            else if (relativeWrite)
                caller.SetIndex(1, index);
            else
                caller.SetIndex(1, 2);

            caller.MarkReaderIndex().MarkWriterIndex();
            int reader = caller.ReaderIndex, writer = caller.WriterIndex;
            Action request = () =>
            {
                switch (operation)
                {
                    case 0:
                        caller.GetBytes(index, new byte[4]);
                        break;
                    case 1:
                        caller.GetBytes(index, external, 3, 4);
                        break;
                    case 2:
                        caller.SetBytes(index, external, 2, 4);
                        break;
                    case 3:
                        external.SetBytes(3, caller, index, 4);
                        break;
                    case 4:
                        caller.ReadBytes(new byte[4]);
                        break;
                    case 5:
                        caller.ReadBytes(external, 3, 4);
                        break;
                    case 6:
                        caller.WriteBytes(external, 2, 4);
                        break;
                    case 7:
                        external.WriteBytes(caller, index, 4);
                        break;
                    case 8:
                        caller.Copy(index, 4).Release();
                        break;
                }
            };

            if ((operation is 2 or 6) && caller.IsReadOnly)
            {
                Assert.Throws<NotSupportedException>(request);
                Assert.Empty(observation.Visits);
            }
            else
            {
                Assert.Throws<ProbeFailure>(request);
                var visit = Assert.Single(observation.Visits);
                string method = operation is 1 or 5 || operation == 8 && form != 4 ? "GetBuffer"
                    : operation is 2 or 6 ? "SetBuffer" : "GetSpan";
                Assert.Equal(method, visit.Method);
                Assert.Equal(rootIndex + (form == 4 ? 0 : 5), visit.Index);
                Assert.Equal(boundary ? 1 : 4, visit.Length);

                if (operation is 1 or 2 or 5 or 6)
                    Assert.Same(external, visit.Other);
                else if (operation == 8 && form != 4)
                    Assert.Equal(0, visit.Other.RefCnt);
                else
                    Assert.Null(visit.Other);
            }

            Assert.Equal(reader, caller.ReaderIndex);
            Assert.Equal(writer, caller.WriterIndex);
            caller.SetIndex(0, caller.Capacity).ResetWriterIndex().ResetReaderIndex();
            Assert.Equal(reader, caller.ReaderIndex);
            Assert.Equal(writer, caller.WriterIndex);
            Assert.Equal(2, external.ReaderIndex);
            Assert.Equal(12, external.WriterIndex);
            Assert.Equal(a, first.AsReadOnlyMemory(0, 32).ToArray());
            Assert.Equal(b, second.AsReadOnlyMemory(0, 32).ToArray());
            Assert.Equal(form >= 5 ? 2 : 1, owner.RefCnt);
            Assert.Equal(1, first.RefCnt);
            Assert.Equal(1, second.RefCnt);
        }
        finally
        {
            if (retained?.RefCnt > 0)
                retained.Release();

            if (owner?.RefCnt > 0)
                owner.Release();

            if (first.RefCnt > 0)
                first.Release();

            if (second.RefCnt > 0)
                second.Release();

            external.Release();
            Assert.Equal(0, native.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> LaterFailures()
    {
        foreach (bool direct in new[] { false, true })
        foreach (bool fixedComposite in new[] { false, true })
        foreach (bool relative in new[] { false, true })
        for (int operation = 0; operation < 3; ++operation)
            if (!fixedComposite || operation != 1)
                        {
                            yield return [direct, fixedComposite, relative, operation];
                        }
    }

    [Theory]
    [MemberData(nameof(LaterFailures))]
    public void LaterComponentFailureKeepsPublishedPrefixAndReleasesFailedCopies(bool direct, bool fixedComposite, bool relative, int operation)
    {
        var native = new NativeMemoryAllocator();
        var observation = new Observation { FailAt = 2 };
        ByteBuf first = direct ? new NativeProbe(observation, native) : new HeapProbe(observation);
        ByteBuf second = direct ? new NativeProbe(observation, native) : new HeapProbe(observation);
        ByteBuf owner = null;
        ByteBuf peer = new UnpooledHeapByteBuf(32);

        try
        {
            byte[] a = new byte[32], b = new byte[32], peerBytes = new byte[32];

            for (int i = 0; i < 32; ++i)
            {
                a[i] = (byte)(i + 1);
                b[i] = (byte)(i + 33);
                peerBytes[i] = (byte)(0x80 + i);
            }

            first.SetBytes(0, a).SetIndex(5, 21);
            second.SetBytes(0, b).SetIndex(3, 19);
            peer.WriteBytes(peerBytes);
            owner = fixedComposite ? Unpooled.WrappedUnmodifiableBuffer(first, second)
                : new CompositeByteBuf().AddComponent(first, true).AddComponent(second, true);

            if (relative)
                owner.SetIndex(operation == 1 ? 1 : 14, operation == 1 ? 14 : 18);
            else
                owner.SetIndex(1, 2);

            int reader = owner.ReaderIndex, writer = owner.WriterIndex;
            Assert.Throws<ProbeFailure>(() =>
            {
                if (operation == 0)
                {
                    if (relative)
                        owner.ReadBytes(peer, 4, 4);
                    else
                        owner.GetBytes(14, peer, 4, 4);
                }
                else if (operation == 1)
                {
                    if (relative)
                        owner.WriteBytes(peer, 2, 4);
                    else
                        owner.SetBytes(14, peer, 2, 4);
                }
                else
                {
                    if (relative)
                        owner.Copy().Release();
                    else
                        owner.Copy(14, 4).Release();
                }
            });
            Assert.Equal(2, observation.Visits.Count);
            Assert.Equal(reader, owner.ReaderIndex);
            Assert.Equal(writer, owner.WriterIndex);

            if (operation == 0)
            {
                peerBytes[4] = (byte)(a[fixedComposite ? 14 : 19] ^ 0x5A);
                peerBytes[5] = (byte)(a[fixedComposite ? 15 : 20] ^ 0x5A);
            }
            else if (operation == 1)
            {
                a[19] = peerBytes[2];
                a[20] = peerBytes[3];
            }
            else if (!fixedComposite)
                Assert.All(observation.Visits, visit => Assert.Equal(0, visit.Other.RefCnt));

            Assert.Equal(a, first.AsReadOnlyMemory(0, 32).ToArray());
            Assert.Equal(b, second.AsReadOnlyMemory(0, 32).ToArray());
            Assert.Equal(peerBytes, peer.AsReadOnlyMemory(0, 32).ToArray());
            Assert.Equal(1, owner.RefCnt);
            Assert.Equal(1, first.RefCnt);
            Assert.Equal(1, second.RefCnt);
        }
        finally
        {
            if (owner?.RefCnt > 0)
                owner.Release();

            if (first.RefCnt > 0)
                first.Release();

            if (second.RefCnt > 0)
                second.Release();

            peer.Release();
            Assert.Equal(0, native.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> AliasFailures()
    {
        foreach (bool direct in new[] { false, true })
        foreach (bool fixedComposite in new[] { false, true })
        foreach (bool relative in new[] { false, true })
                {
                    yield return [direct, fixedComposite, relative];
                }
    }

    [Theory]
    [MemberData(nameof(AliasFailures))]
    public void AliasedGetterFailureDoesNotPublishTheStagedPrefix(bool direct, bool fixedComposite, bool relative)
    {
        var native = new NativeMemoryAllocator();
        var observation = new Observation { FailAt = 2 };
        ByteBuf first = direct ? new NativeProbe(observation, native) : new HeapProbe(observation);
        ByteBuf second = direct ? new NativeProbe(observation, native) : new HeapProbe(observation);
        ByteBuf owner = null;

        try
        {
            byte[] a = new byte[32], b = new byte[32];

            for (int i = 0; i < 32; ++i)
            {
                a[i] = (byte)(i + 1);
                b[i] = (byte)(i + 33);
            }

            first.SetBytes(0, a).SetIndex(5, 21);
            second.SetBytes(0, b).SetIndex(3, 19);
            owner = fixedComposite ? Unpooled.WrappedUnmodifiableBuffer(first, second)
                : new CompositeByteBuf().AddComponent(first, true).AddComponent(second, true);
            owner.SetIndex(relative ? 14 : 1, relative ? 18 : 2);
            int destination = fixedComposite ? 13 : 18;
            Assert.Throws<ProbeFailure>(() => {
                if (relative)
                {
                    owner.ReadBytes(first, destination, 4);
                }
                else
                {
                    owner.GetBytes(14, first, destination, 4);
                }
            });
            Assert.Equal(2, observation.Visits.Count);
            Assert.All(observation.Visits, visit =>
            {
                Assert.NotSame(first, visit.Other);
                Assert.Equal(0, visit.Other.RefCnt);
            });
            Assert.Equal(relative ? 14 : 1, owner.ReaderIndex);
            Assert.Equal(relative ? 18 : 2, owner.WriterIndex);
            Assert.Equal(a, first.AsReadOnlyMemory(0, 32).ToArray());
            Assert.Equal(b, second.AsReadOnlyMemory(0, 32).ToArray());
        }
        finally
        {
            if (owner?.RefCnt > 0)
                owner.Release();

            if (first.RefCnt > 0)
                first.Release();

            if (second.RefCnt > 0)
                second.Release();

            Assert.Equal(0, native.ReservedBytes);
        }
    }

    private sealed class MetadataTrap : WrappedByteBuf
    {
        public MetadataTrap(ByteBuf buffer) : base(buffer)
        {
        }

        public override ReadOnlyMemory<byte> AsReadOnlyMemory(int index, int length)
        {
            throw new InvalidOperationException("Bulk overlap checks must not call public memory accessors.");
        }

        public override ReadOnlySequence<byte> AsReadOnlySequence(int index, int length)
        {
            throw new InvalidOperationException("Bulk overlap checks must not call public sequence accessors.");
        }
    }
    private sealed class Wrapper : WrappedByteBuf
    {
        public Wrapper(ByteBuf b) : base(b)
        {
        }
    }
    public static IEnumerable<object[]> Aliases()
    {
        foreach (bool direct in new[] { false, true })
        foreach (bool wrapped in new[] { false, true })
        for (int range = 0; range < 3; ++range)
        for (int operation = 0; operation < 6; ++operation)
                    {
                        yield return [direct, wrapped, range, operation];
                    }
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
            byte[] original = new byte[32];

            for (int i = 0; i < 32; ++i)
            {
                original[i] = (byte)(i + 1);
            }

            backing.WriteBytes(original);
            ByteBuf first = backing.RetainedSlice(0, 16), second = backing.RetainedSlice(16, 16);
            owner = operation >= 4 ? Unpooled.WrappedUnmodifiableBuffer(first, second)
                : new CompositeByteBuf().AddComponent(first, true).AddComponent(second, true);
            ByteBuf aggregate = wrapped ? new Wrapper(new MetadataTrap(owner)) : owner;
            ByteBuf peer = wrapped ? new Wrapper(new MetadataTrap(backing)) : backing;
            (int source, int destination, int length) = range switch { 0 => (0, 4, 20), 1 => (4, 0, 20), _ => (4, 6, 16) };
            byte[] expected = (byte[])original.Clone();
            Array.Copy(original, source, expected, destination, length);

            if (operation is 0 or 4)
            {
                aggregate.GetBytes(source, peer, destination, length);
            }
            else if (operation == 1)
            {
                aggregate.SetBytes(destination, peer, source, length);
            }
            else if (operation is 2 or 5)
            {
                aggregate.SetIndex(source, source + length);
                aggregate.ReadBytes(peer, destination, length);
                Assert.Equal(source + length, aggregate.ReaderIndex);
            }
            else
            {
                aggregate.SetIndex(0, destination);
                aggregate.WriteBytes(peer, source, length);
                Assert.Equal(destination + length, aggregate.WriterIndex);
            }

            Assert.Equal(expected, backing.AsReadOnlyMemory(0, 32).ToArray());
            Assert.Equal(1, owner.RefCnt);
            Assert.Equal(3, backing.RefCnt);
        }
        finally
        {
            if (owner?.RefCnt > 0)
            {
                owner.Release();
            }

            if (backing.RefCnt > 0)
            {
                backing.Release();
            }

            Assert.Equal(0, native.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> Outputs()
    {
        foreach (bool direct in new[] { false, true })
        foreach (bool fixedComposite in new[] { false, true })
        for (int endpoint = 0; endpoint < 6; ++endpoint)
                {
                    yield return [direct, fixedComposite, endpoint];
                }
    }

    [Theory]
    [MemberData(nameof(Outputs))]
    public async Task OutputStagingReadsLogicalComponentBytesForStreamFileAndSocket(bool direct, bool fixedComposite, int endpoint)
    {
        var native = new NativeMemoryAllocator();
        var observation = new Observation { Fail = false };
        var cancellation = TestContext.Current.CancellationToken;
        ByteBuf first = direct ? new NativeProbe(observation, native) : new HeapProbe(observation);
        ByteBuf second = direct ? new NativeProbe(observation, native) : new HeapProbe(observation);
        ByteBuf owner = null;
        string path = null;

        try
        {
            byte[] a = new byte[32], b = new byte[32];

            for (int i = 0; i < 32; ++i)
            {
                a[i] = (byte)(i + 1);
                b[i] = (byte)(i + 33);
            }

            first.SetBytes(0, a).SetIndex(5, 21);
            second.SetBytes(0, b).SetIndex(3, 19);
            owner = fixedComposite ? Unpooled.WrappedUnmodifiableBuffer(first, second)
                : new CompositeByteBuf().AddComponent(first, true).AddComponent(second, true);
            owner.SetIndex(1, 3).MarkReaderIndex().MarkWriterIndex();
            byte[] expected = fixedComposite ? [15, 16, 33, 34] : [20, 21, 36, 37];

            for (int i = 0; i < expected.Length; ++i)
                expected[i] ^= 0x5A;

            if (endpoint < 2)
            {
                using var output = new MemoryStream();

                if (endpoint == 0)
                    owner.GetBytes(14, output, 4);
                else
                    await owner.GetBytesAsync(14, output, 4, cancellation);

                Assert.Equal(expected, output.ToArray());
                Assert.True(output.CanWrite);
            }
            else if (endpoint < 4)
            {
                path = Path.GetTempFileName();
                using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                int count = endpoint == 2 ? owner.GetBytes(14, handle, 7, 4) : await owner.GetBytesAsync(14, handle, 7, 4, cancellation);
                byte[] actual = new byte[4];
                Assert.Equal(4, count);
                Assert.Equal(4, RandomAccess.Read(handle, actual, 7));
                Assert.Equal(expected, actual);
                Assert.False(handle.IsClosed);
            }
            else
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                using var sender = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                try
                {
                    ValueTask connect = sender.ConnectAsync(listener.LocalEndpoint, cancellation);
                    using Socket receiver = await listener.AcceptSocketAsync(cancellation);
                    await connect;
                    int count = endpoint == 4 ? owner.GetBytes(14, sender, 4) : await owner.GetBytesAsync(14, sender, 4, cancellation);
                    Assert.InRange(count, 1, 4);
                    byte[] actual = new byte[count];

                    for (int offset = 0; offset < count;)
                    {
                        int read = await receiver.ReceiveAsync(actual.AsMemory(offset), SocketFlags.None, cancellation);
                        Assert.True(read > 0);
                        offset += read;
                    }

                    Assert.Equal(expected[..count], actual);
                }
                finally
                {
                    listener.Stop();
                }
            }

            Assert.Equal(2, observation.Visits.Count);
            Assert.All(observation.Visits, visit =>
            {
                Assert.Equal("GetSpan", visit.Method);
                Assert.Equal(2, visit.Length);
            });
            Assert.Equal(1, owner.ReaderIndex);
            Assert.Equal(3, owner.WriterIndex);
            owner.SetIndex(0, owner.Capacity).ResetWriterIndex().ResetReaderIndex();
            Assert.Equal(1, owner.ReaderIndex);
            Assert.Equal(3, owner.WriterIndex);
            Assert.Equal(a, first.AsReadOnlyMemory(0, 32).ToArray());
            Assert.Equal(b, second.AsReadOnlyMemory(0, 32).ToArray());
            Assert.Equal(1, first.RefCnt);
            Assert.Equal(1, second.RefCnt);
        }
        finally
        {
            if (owner?.RefCnt > 0)
                owner.Release();

            if (first.RefCnt > 0)
                first.Release();

            if (second.RefCnt > 0)
                second.Release();

            if (path != null)
                File.Delete(path);

            Assert.Equal(0, native.ReservedBytes);
        }
    }
}
