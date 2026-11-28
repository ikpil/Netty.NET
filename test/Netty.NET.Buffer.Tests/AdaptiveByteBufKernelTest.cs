/*
 * Copyright 2022 The Netty Project
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
using System.Linq;
using System.Runtime.InteropServices;
using Netty.NET.Common;
using static Netty.NET.Buffer.AdaptivePoolingAllocator;

namespace Netty.NET.Buffer.Tests;

public class AdaptiveByteBufKernelTest
{
    private sealed class KernelFailure : Exception { }
    private sealed class Observation
    {
        public bool Enabled, Fail = true;
        public int? IterationResult;
        public byte? ReadFill;
        public readonly KernelFailure Failure = new();
        public readonly List<(string Method, int Index, int Width, object Peer)> Calls = [];
        public void Visit(string method, int index, int width, object peer = null)
        {
            if (!Enabled)
            {
                return;
            }

            Calls.Add((method, index, width, peer));
            if (Fail)
            {
                throw Failure;
            }
        }
    }
    private sealed class HeapProbe(Observation o, int size, int max) : UnpooledHeapByteBuf(size, max)
    {
        public override bool IsContiguous => false;
        public override byte GetByte(int index)
        {
            o.Visit("PublicByte", index, 1);

            return base.GetByte(index);
        }

        internal override byte ReadByteCore(int index)
        {
            o.Visit("Byte", index, 1);

            return base.ReadByteCore(index);
        }

        internal override void WriteByteCore(int index, int value)
        {
            o.Visit("SetByte", index, 1);
            base.WriteByteCore(index, value);
        }

        internal override ulong ReadWord(int index, int width, bool little)
        {
            o.Visit("Word", index, width);

            return base.ReadWord(index, width, little);
        }

        internal override ByteBuf SetWord(int index, ulong value, int width, bool little)
        {
            o.Visit("SetWord", index, width);

            return base.SetWord(index, value, width, little);
        }

        public override ByteBuf GetBytes(int index, Span<byte> destination)
        {
            o.Visit("Span", index, destination.Length);

            if (o.ReadFill is byte fill)
            {
                destination.Fill(fill);
                return this;
            }

            return base.GetBytes(index, destination);
        }

        public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
        {
            o.Visit("Buffer", index, length, destination);

            return base.GetBytes(index, destination, destinationIndex, length);
        }

        public override int ForEachByte(int index, int length, Func<byte, bool> processor)
        {
            o.Visit("Asc", index, length, processor);
            return o.IterationResult ?? base.ForEachByte(index, length, processor);
        }

        public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
        {
            o.Visit("Desc", index, length, processor);
            return o.IterationResult ?? base.ForEachByteDesc(index, length, processor);
        }

        public override ByteBuf SetZero(int index, int length)
        {
            o.Visit("Zero", index, length);

            return base.SetZero(index, length);
        }
    }
    private sealed class NativeProbe(Observation o, int size, int max, NativeMemoryAllocator native) : UnpooledDirectByteBuf(size, max, native)
    {
        public override bool IsContiguous => false;
        public override byte GetByte(int index)
        {
            o.Visit("PublicByte", index, 1);

            return base.GetByte(index);
        }

        internal override byte ReadByteCore(int index)
        {
            o.Visit("Byte", index, 1);

            return base.ReadByteCore(index);
        }

        internal override void WriteByteCore(int index, int value)
        {
            o.Visit("SetByte", index, 1);
            base.WriteByteCore(index, value);
        }

        internal override ulong ReadWord(int index, int width, bool little)
        {
            o.Visit("Word", index, width);

            return base.ReadWord(index, width, little);
        }

        internal override ByteBuf SetWord(int index, ulong value, int width, bool little)
        {
            o.Visit("SetWord", index, width);

            return base.SetWord(index, value, width, little);
        }

        public override ByteBuf GetBytes(int index, Span<byte> destination)
        {
            o.Visit("Span", index, destination.Length);

            if (o.ReadFill is byte fill)
            {
                destination.Fill(fill);
                return this;
            }

            return base.GetBytes(index, destination);
        }

        public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
        {
            o.Visit("Buffer", index, length, destination);

            return base.GetBytes(index, destination, destinationIndex, length);
        }

        public override int ForEachByte(int index, int length, Func<byte, bool> processor)
        {
            o.Visit("Asc", index, length, processor);
            return o.IterationResult ?? base.ForEachByte(index, length, processor);
        }

        public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
        {
            o.Visit("Desc", index, length, processor);
            return o.IterationResult ?? base.ForEachByteDesc(index, length, processor);
        }

        public override ByteBuf SetZero(int index, int length)
        {
            o.Visit("Zero", index, length);

            return base.SetZero(index, length);
        }
    }
    private sealed class Storage : IDisposable
    {
        public readonly Observation Observation = new();
        public readonly AdaptiveBufferTestAllocator Allocator;
        public readonly ByteBuf Guard;
        public readonly AdaptiveByteBuf Buffer;
        public readonly List<ByteBuf> Raw = [];
        public Storage(bool direct, int profile)
        {
            Allocator = new AdaptiveBufferTestAllocator(profile == 4, profile == 1);
            Allocator.StorageFactory = (_, size, max) =>
            {
                ByteBuf root = direct ? new NativeProbe(Observation, size, max, Allocator.Native) : new HeapProbe(Observation, size, max);
                byte[] bytes = new byte[size];

                for (int i = 0; i < bytes.Length; ++i)
                {
                    bytes[i] = (byte)(i % 251);
                }

                root.SetBytes(0, bytes.AsSpan());
                Raw.Add(root);
                return root;
            };
            int size = profile is 2 or 4 ? 17000 : profile == 3 ? 1048577 : profile == 1 ? 513 : 13;
            int max = profile == 3 ? 1048640 : 65536;
            Guard = direct ? Allocator.DirectBuffer(size, max) : Allocator.HeapBuffer(size, max);
            Buffer = (AdaptiveByteBuf)(direct ? Allocator.DirectBuffer(size, max) : Allocator.HeapBuffer(size, max));
        }

        public void Dispose()
        {
            Observation.Enabled = false;

            if (Buffer.RefCnt > 0)
                Buffer.Release();

            if (Guard.RefCnt > 0)
                Guard.Release();

            Allocator.Dispose();
            Assert.Equal(0, Allocator.Native.ReservedBytes);
        }
    }
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool direct in new[] { false, true })
        for (int profile = 0; profile < 5; ++profile)
        for (int operation = 0; operation < 14; ++operation)
                {
                    yield return [direct, profile, operation];
                }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void RootKernelsReceiveCapturedCoordinatesAndRelativeFailureDoesNotPublishIndices(bool direct, int profile, int operation)
    {
        using var s = new Storage(direct, profile);
        ByteBuf b = s.Buffer;
        int offset = s.Buffer.Allocation.Offset;
        ByteBuf peer = new UnpooledHeapByteBuf(8);
        Func<byte, bool> visitor = _ => true;

        if (operation is 9 or 13)
            b.SetIndex(1, 3);
        else if (operation >= 10)
            b.SetIndex(3, 7);
        else
            b.SetIndex(1, 2);

        int reader = b.ReaderIndex, writer = b.WriterIndex;
        b.MarkReaderIndex().MarkWriterIndex();
        s.Observation.Enabled = true;

        try
        {
            void Request()
            {
                switch (operation)
                {
                    case 0:
                        b.GetByte(3);
                        break;
                    case 1:
                        b.GetLongLE(3);
                        break;
                    case 2:
                        b.SetByte(3, 17);
                        break;
                    case 3:
                        b.SetIntLE(3, 17);
                        break;
                    case 4:
                        b.GetBytes(3, new byte[4].AsSpan());
                        break;
                    case 5:
                        b.GetBytes(3, peer, 1, 4);
                        break;
                    case 6:
                        b.ForEachByte(3, 4, visitor);
                        break;
                    case 7:
                        b.ForEachByteDesc(3, 4, visitor);
                        break;
                    case 8:
                        b.SetZero(3, 4);
                        break;
                    case 9:
                        b.WriteZero(4);
                        break;
                    case 10:
                        b.ReadBytes(new byte[4].AsSpan());
                        break;
                    case 11:
                        b.ReadBytes(peer, 1, 4);
                        break;
                    case 12:
                        b.ReadByte();
                        break;
                    case 13:
                        b.WriteLongLE(17);
                        break;
                }
            }

            Assert.Same(s.Observation.Failure, Assert.Throws<KernelFailure>(() => Request()));
            var call = Assert.Single(s.Observation.Calls);
            string expected = operation switch
            {
                0 or 12 => "Byte",
                1 => "Word",
                2 => "SetByte",
                3 or 13 => "SetWord",
                4 or 10 => "Span",
                5 or 11 => "Buffer",
                6 => "Asc",
                7 => "Desc",
                _ => "Zero"
            };
            Assert.Equal(expected, call.Method);
            Assert.Equal(offset + 3, call.Index);
            Assert.Equal(operation is 0 or 2 or 12 ? 1 : operation is 1 or 13 ? 8 : 4, call.Width);

            if (operation is 5 or 11)
                Assert.Same(peer, call.Peer);

            if (operation is 6 or 7)
                Assert.Same(visitor, call.Peer);

            Assert.Equal(reader, b.ReaderIndex);
            Assert.Equal(writer, b.WriterIndex);
            Assert.Equal(0, peer.ReaderIndex);
            Assert.Equal(0, peer.WriterIndex);
            Assert.Equal(1, b.RefCnt);
            Assert.Equal(1, s.Guard.RefCnt);
            Assert.Equal(1, s.Raw[^1].RefCnt);
        }
        finally
        {
            s.Observation.Enabled = false;
            peer.Release();
        }
    }

    public static IEnumerable<object[]> Reallocations()
    {
        foreach (bool direct in new[] { false, true })
        for (int profile = 0; profile < 5; ++profile)
        for (int mode = 0; mode < 3; ++mode)
                {
                    yield return [direct, profile, mode];
                }
    }

    [Theory]
    [MemberData(nameof(Reallocations))]
    public void ReallocationReadsLogicalPrefixThroughOldRootBeforePublishingReplacement(bool direct, int profile, int mode)
    {
        using var s = new Storage(direct, profile);
        AdaptiveByteBuf b = s.Buffer;
        AdaptiveChunkAllocation old = b.Allocation, replacement = null;
        int capacity = b.Capacity, target = old.Length + 1;
        byte[] original = b.AsSpan(0, capacity).ToArray();
        byte[] guard = s.Guard.AsSpan(0, s.Guard.Capacity).ToArray();
        b.SetIndex(2, 7).MarkReaderIndex().MarkWriterIndex().SetIndex(3, 8);
        s.Allocator.AfterReservation = (_, allocation) => replacement = allocation;
        s.Observation.Enabled = true;
        s.Observation.Fail = mode == 0;

        if (mode == 1)
        {
            s.Observation.ReadFill = 0xA5;
        }

        try
        {
            if (mode == 0)
            {
                Assert.Same(s.Observation.Failure, Assert.Throws<KernelFailure>(() => b.Capacity = target));
                Assert.Same(old, b.Allocation);
                Assert.Equal(capacity, b.Capacity);
                Assert.Equal(original, b.AsSpan(0, capacity).ToArray());
                Assert.NotNull(replacement);
                Assert.Throws<ObjectDisposedException>(() =>
                {
                    _ = replacement.GetSpan().Length;
                });
            }
            else
            {
                b.Capacity = target;
                Assert.Same(replacement, b.Allocation);
                Assert.NotSame(old, b.Allocation);
                Assert.Equal(target, b.Capacity);
                byte[] expected = mode == 1 ? new byte[capacity] : original;

                if (mode == 1)
                {
                    Array.Fill(expected, (byte)0xA5);
                }

                Assert.Equal(expected, b.AsSpan(0, capacity).ToArray());
                Assert.Throws<ObjectDisposedException>(() =>
                {
                    _ = old.GetSpan().Length;
                });
            }

            var call = Assert.Single(s.Observation.Calls);
            Assert.Equal(("Span", old.Offset, capacity), (call.Method, call.Index, call.Width));
            Assert.Equal((3, 8), (b.ReaderIndex, b.WriterIndex));
            b.ResetWriterIndex().ResetReaderIndex();
            Assert.Equal((2, 7), (b.ReaderIndex, b.WriterIndex));
            Assert.Equal(guard, s.Guard.AsSpan(0, s.Guard.Capacity).ToArray());
            Assert.Equal(1, b.RefCnt);
            Assert.Equal(1, s.Guard.RefCnt);

            if (mode == 0)
            {
                // The failed destination has been returned. Retrying must still read the old prefix.
                s.Observation.Enabled = false;
                b.Capacity = target;
                Assert.Equal(original, b.AsSpan(0, capacity).ToArray());
            }
        }
        finally
        {
            s.Observation.Enabled = false;
        }
    }

    public static IEnumerable<object[]> Iterations()
    {
        foreach (bool direct in new[] { false, true })
        for (int profile = 0; profile < 5; ++profile)
        foreach (bool descending in new[] { false, true })
        foreach (bool indexed in new[] { false, true })
                    {
                        yield return [direct, profile, descending, indexed];
                    }
    }

    [Theory]
    [MemberData(nameof(Iterations))]
    public void IndexedIterationForwardsTheProcessorWhileReadableIterationUsesOwnStorageLoop(bool direct, int profile, bool descending, bool indexed)
    {
        using var s = new Storage(direct, profile);
        ByteBuf b = s.Buffer;
        int offset = s.Buffer.Allocation.Offset;
        b.SetIndex(3, 7).MarkReaderIndex().MarkWriterIndex();
        s.Observation.Enabled = true;
        s.Observation.Fail = false;
        var seen = new List<byte>();
        Func<byte, bool> visitor = value =>
        {
            seen.Add(value);
            return seen.Count < 2;
        };
        int result = indexed ? descending ? b.ForEachByteDesc(3, 4, visitor) : b.ForEachByte(3, 4, visitor)
            : descending ? b.ForEachByteDesc(visitor) : b.ForEachByte(visitor);
        Assert.Equal(descending ? 5 : 4, result);
        Assert.Equal(descending ? new byte[] { (byte)((offset + 6) % 251), (byte)((offset + 5) % 251) }
            : new byte[] { (byte)((offset + 3) % 251), (byte)((offset + 4) % 251) }, seen);
        var delegates = s.Observation.Calls.Where(x => x.Method is "Asc" or "Desc").ToArray();
        Assert.Equal(indexed ? 1 : 0, delegates.Length);

        if (indexed)
        {
            Assert.Equal(offset + 3, delegates[0].Index);
            Assert.Equal(4, delegates[0].Width);
            Assert.Same(visitor, delegates[0].Peer);
        }

        Assert.DoesNotContain(s.Observation.Calls, x => x.Method == "PublicByte");
        Assert.Equal(3, b.ReaderIndex);
        Assert.Equal(7, b.WriterIndex);
    }

    public static IEnumerable<object[]> Results()
    {
        foreach (bool direct in new[] { false, true })
        for (int profile = 0; profile < 5; ++profile)
        foreach (bool descending in new[] { false, true })
        for (int result = 0; result < 4; ++result)
                    {
                        yield return [direct, profile, descending, result];
                    }
    }

    [Theory]
    [MemberData(nameof(Results))]
    public void RootStopIndicesAreTranslatedUsingTheCapturedRegion(bool direct, int profile, bool descending, int result)
    {
        using var s = new Storage(direct, profile);
        int offset = s.Buffer.Allocation.Offset;
        s.Observation.IterationResult = result switch
        {
            0 => -2,
            1 => -1,
            2 => offset - 1,
            _ => offset + 4
        };
        s.Observation.Enabled = true;
        s.Observation.Fail = false;
        int actual = descending ? s.Buffer.ForEachByteDesc(3, 4, _ => true) : s.Buffer.ForEachByte(3, 4, _ => true);
        Assert.Equal(result == 3 ? 4 : -1, actual);
        Assert.Single(s.Observation.Calls);
    }

    public static IEnumerable<object[]> Profiles()
    {
        foreach (bool direct in new[] { false, true })
        for (int profile = 0; profile < 5; ++profile)
            {
                yield return [direct, profile];
            }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void PhysicalBorrowsStillInvalidateOnReturnWhileLeasesKeepTheirRegion(bool direct, int profile)
    {
        using var s = new Storage(direct, profile);
        ByteBuf b = s.Buffer;
        Assert.False(b.IsContiguous);
        Memory<byte> memory = b.AsMemory(3, 4);
        var sequence = b.AsReadOnlySequence(3, 4);
        byte first = memory.Span[0];
        using var lease = b.AcquireReadLease();
        ReadOnlySpan<byte> bytes = memory.Span;

        try
        {
            b.Release();
            s.Guard.Release();
            s.Allocator.Dispose();
            Assert.Throws<ObjectDisposedException>(() => memory.Span.Clear());
            Assert.Throws<ObjectDisposedException>(() => sequence.FirstSpan.ToArray());
            Assert.Equal(first, bytes[0]);
            Assert.Throws<IllegalReferenceCountException>(() => b.GetBytes(0, new byte[1].AsSpan()));
        }
        finally
        {
            lease.Dispose();
        }

        Assert.Equal(0, s.Allocator.UsedHeapMemory + s.Allocator.UsedDirectMemory);
        Assert.Equal(0, s.Allocator.Native.ReservedBytes);
    }

    public static IEnumerable<object[]> Callbacks()
    {
        foreach (bool direct in new[] { false, true })
        for (int profile = 0; profile < 5; ++profile)
        foreach (bool descending in new[] { false, true })
        foreach (bool resize in new[] { false, true })
                    {
                        yield return [direct, profile, descending, resize];
                    }
    }

    [Theory]
    [MemberData(nameof(Callbacks))]
    public void RootIterationLeasePreventsReuseDuringReturnOrResizeCallback(bool direct, int profile, bool descending, bool resize)
    {
        using var s = new Storage(direct, profile);
        ByteBuf b = s.Buffer;
        AdaptiveChunkAllocation old = s.Buffer.Allocation;
        ByteBuf raw = old.Root, replacement = null;
        int offset = old.Offset;
        var seen = new List<byte>();
        b.SetIndex(3, 7);
        s.Observation.Enabled = true;
        s.Observation.Fail = false;
        Func<byte, bool> visitor = value =>
        {
            seen.Add(value);

            if (seen.Count == 1)
            {
                if (resize)
                    b.Capacity = old.Length + 1;
                else
                {
                    int capacity = b.Capacity, max = b.MaxCapacity;
                    b.Release();
                    replacement = direct ? s.Allocator.DirectBuffer(capacity, max) : s.Allocator.HeapBuffer(capacity, max);
                    var region = ((AdaptiveByteBuf)replacement).Allocation;
                    Assert.True(!ReferenceEquals(raw, region.Root) || offset != region.Offset);
                    replacement.SetBytes(0, new byte[] { 0xEE, 0xEE, 0xEE, 0xEE });
                }

                Assert.Equal(1, raw.RefCnt);
            }

            return seen.Count < 2;
        };

        try
        {
            int result = descending ? b.ForEachByteDesc(3, 4, visitor) : b.ForEachByte(3, 4, visitor);
            Assert.Equal(descending ? 5 : 4, result);
            Assert.Equal(descending ? new byte[] { (byte)((offset + 6) % 251), (byte)((offset + 5) % 251) }
                : new byte[] { (byte)((offset + 3) % 251), (byte)((offset + 4) % 251) }, seen);
            Assert.Equal(resize ? 1 : 0, b.RefCnt);
        }
        finally
        {
            replacement?.Release();
        }
    }

    public static IEnumerable<object[]> Aliases()
    {
        foreach (bool direct in new[] { false, true })
        for (int profile = 0; profile < 5; ++profile)
        foreach (bool typed in new[] { false, true })
        foreach (bool reverse in new[] { false, true })
                    {
                        yield return [direct, profile, typed, reverse];
                    }
    }

    [Theory]
    [MemberData(nameof(Aliases))]
    public void DelegatedReadsKeepMemmoveSemanticsInsideOneRegion(bool direct, int profile, bool typed, bool reverse)
    {
        using var s = new Storage(direct, profile);
        ByteBuf b = s.Buffer;
        byte[] expected = b.AsReadOnlyMemory(0, 8).ToArray();
        int source = reverse ? 2 : 0, destination = reverse ? 0 : 2;
        Array.Copy(expected, source, expected, destination, 4);
        s.Observation.Enabled = true;
        s.Observation.Fail = false;

        if (typed)
            b.GetBytes(source, b, destination, 4);
        else
            b.GetBytes(source, b.AsSpan(destination, 4));

        Assert.Equal(expected, b.AsReadOnlyMemory(0, 8).ToArray());
        Assert.Equal(0, b.ReaderIndex);
        Assert.Equal(0, b.WriterIndex);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void ScalarBulkAndZeroSuccessKeepBytesAndCursorRules(bool direct, int profile)
    {
        using var s = new Storage(direct, profile);
        ByteBuf b = s.Buffer;
        s.Observation.Enabled = true;
        s.Observation.Fail = false;
        b.SetIntLE(3, 0x04030201);
        Assert.Equal(0x04030201, b.GetIntLE(3));
        b.SetByte(3, 0x7F);
        Assert.Equal(0x7F, b.GetByte(3));
        byte[] bytes = new byte[4];
        b.GetBytes(3, bytes.AsSpan());
        Assert.Equal(new byte[] { 0x7F, 2, 3, 4 }, bytes);
        ByteBuf peer = new UnpooledHeapByteBuf(8);

        try
        {
            b.GetBytes(3, peer, 1, 4);
            Assert.Equal(bytes, peer.AsReadOnlyMemory(1, 4).ToArray());
        }
        finally
        {
            peer.Release();
        }

        b.SetIndex(1, 3);
        b.WriteZero(4);
        Assert.Equal(7, b.WriterIndex);
        Assert.Equal(new byte[4], b.AsReadOnlyMemory(3, 4).ToArray());
        b.SetByte(3, 9);
        b.SetZero(3, 4);
        Assert.Equal(1, b.ReaderIndex);
        Assert.Equal(7, b.WriterIndex);
        Assert.DoesNotContain(s.Observation.Calls, x => x.Method == "PublicByte");
    }
}
