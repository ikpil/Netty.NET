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

namespace Netty.NET.Buffer.Tests;

public partial class AdaptiveByteBufKernelTest
{
    public static IEnumerable<object[]> Iterations()
    {
        foreach (bool direct in new[] { false, true })
        for (int profile = 0; profile < 5; ++profile)
        foreach (bool descending in new[] { false, true })
        foreach (bool indexed in new[] { false, true }) yield return [direct, profile, descending, indexed];
    }
    [Theory]
    [MemberData(nameof(Iterations))]
    public void IndexedIterationForwardsTheProcessorWhileReadableIterationUsesOwnStorageLoop(bool direct, int profile, bool descending, bool indexed)
    {
        using var s = new Storage(direct, profile); ByteBuf b = s.Buffer; int offset = s.Buffer.Allocation.Offset;
        b.SetIndex(3, 7).MarkReaderIndex().MarkWriterIndex(); s.Observation.Enabled = true; s.Observation.Fail = false;
        var seen = new List<byte>(); Func<byte, bool> visitor = value => { seen.Add(value); return seen.Count < 2; };
        int result = indexed ? descending ? b.ForEachByteDesc(3, 4, visitor) : b.ForEachByte(3, 4, visitor)
            : descending ? b.ForEachByteDesc(visitor) : b.ForEachByte(visitor);
        Assert.Equal(descending ? 5 : 4, result);
        Assert.Equal(descending ? new byte[] { (byte)((offset + 6) % 251), (byte)((offset + 5) % 251) }
            : new byte[] { (byte)((offset + 3) % 251), (byte)((offset + 4) % 251) }, seen);
        var delegates = s.Observation.Calls.Where(x => x.Method is "Asc" or "Desc").ToArray();
        Assert.Equal(indexed ? 1 : 0, delegates.Length);
        if (indexed) { Assert.Equal(offset + 3, delegates[0].Index); Assert.Equal(4, delegates[0].Width); Assert.Same(visitor, delegates[0].Peer); }
        Assert.DoesNotContain(s.Observation.Calls, x => x.Method == "PublicByte");
        Assert.Equal(3, b.ReaderIndex); Assert.Equal(7, b.WriterIndex);
    }
    public static IEnumerable<object[]> Results()
    {
        foreach (bool direct in new[] { false, true })
        for (int profile = 0; profile < 5; ++profile)
        foreach (bool descending in new[] { false, true })
        for (int result = 0; result < 4; ++result) yield return [direct, profile, descending, result];
    }
    [Theory]
    [MemberData(nameof(Results))]
    public void RootStopIndicesAreTranslatedUsingTheCapturedRegion(bool direct, int profile, bool descending, int result)
    {
        using var s = new Storage(direct, profile); int offset = s.Buffer.Allocation.Offset;
        s.Observation.IterationResult = result switch { 0 => -2, 1 => -1, 2 => offset - 1, _ => offset + 4 };
        s.Observation.Enabled = true; s.Observation.Fail = false;
        int actual = descending ? s.Buffer.ForEachByteDesc(3, 4, _ => true) : s.Buffer.ForEachByte(3, 4, _ => true);
        Assert.Equal(result == 3 ? 4 : -1, actual); Assert.Single(s.Observation.Calls);
    }
    public static IEnumerable<object[]> Profiles()
    {
        foreach (bool direct in new[] { false, true })
        for (int profile = 0; profile < 5; ++profile) yield return [direct, profile];
    }
    [Theory]
    [MemberData(nameof(Profiles))]
    public unsafe void PhysicalBorrowsStillInvalidateOnReturnWhilePinsKeepTheirRegion(bool direct, int profile)
    {
        using var s = new Storage(direct, profile); ByteBuf b = s.Buffer;
        Assert.False(b.IsContiguous);
        Memory<byte> memory = b.AsMemory(3, 4); var sequence = b.AsReadOnlySequence(3, 4); byte first = memory.Span[0];
        var pin = memory.Pin(); var copied = pin;
        try
        {
            b.Release(); s.Guard.Release(); s.Allocator.Dispose();
            Assert.Throws<ObjectDisposedException>(() => memory.Span.Clear());
            Assert.Throws<ObjectDisposedException>(() => sequence.FirstSpan.ToArray());
            Assert.Equal(first, Marshal.ReadByte((IntPtr)pin.Pointer));
            Assert.Throws<IllegalReferenceCountException>(() => b.GetBytes(0, new byte[1].AsSpan()));
        }
        finally { pin.Dispose(); copied.Dispose(); }
        Assert.Equal(0, s.Allocator.UsedHeapMemory + s.Allocator.UsedDirectMemory);
        Assert.Equal(0, s.Allocator.Native.ReservedBytes);
    }
    public static IEnumerable<object[]> Callbacks()
    {
        foreach (bool direct in new[] { false, true })
        for (int profile = 0; profile < 5; ++profile)
        foreach (bool descending in new[] { false, true })
        foreach (bool resize in new[] { false, true }) yield return [direct, profile, descending, resize];
    }
    [Theory]
    [MemberData(nameof(Callbacks))]
    public void RootIterationLeasePreventsReuseDuringReturnOrResizeCallback(bool direct, int profile, bool descending, bool resize)
    {
        using var s = new Storage(direct, profile); ByteBuf b = s.Buffer; AdaptiveChunkAllocation old = s.Buffer.Allocation;
        ByteBuf raw = old.Root, replacement = null; int offset = old.Offset; var seen = new List<byte>();
        b.SetIndex(3, 7); s.Observation.Enabled = true; s.Observation.Fail = false;
        Func<byte, bool> visitor = value =>
        {
            seen.Add(value);
            if (seen.Count == 1)
            {
                if (resize) b.Capacity = old.Length + 1;
                else
                {
                    int capacity = b.Capacity, max = b.MaxCapacity; b.Release();
                    replacement = direct ? s.Allocator.DirectBuffer(capacity, max) : s.Allocator.HeapBuffer(capacity, max);
                    var region = ((AdaptiveByteBuf)replacement).Allocation;
                    Assert.True(!ReferenceEquals(raw, region.Root) || offset != region.Offset);
                    replacement.SetBytes(0, new byte[] { 0xEE, 0xEE, 0xEE, 0xEE });
                }
                Assert.Equal(1, raw.ReferenceCount);
            }
            return seen.Count < 2;
        };
        try
        {
            int result = descending ? b.ForEachByteDesc(3, 4, visitor) : b.ForEachByte(3, 4, visitor);
            Assert.Equal(descending ? 5 : 4, result);
            Assert.Equal(descending ? new byte[] { (byte)((offset + 6) % 251), (byte)((offset + 5) % 251) }
                : new byte[] { (byte)((offset + 3) % 251), (byte)((offset + 4) % 251) }, seen);
            Assert.Equal(resize ? 1 : 0, b.ReferenceCount);
        }
        finally { replacement?.Release(); }
    }
    public static IEnumerable<object[]> Aliases()
    {
        foreach (bool direct in new[] { false, true })
        for (int profile = 0; profile < 5; ++profile)
        foreach (bool typed in new[] { false, true })
        foreach (bool reverse in new[] { false, true }) yield return [direct, profile, typed, reverse];
    }
    [Theory]
    [MemberData(nameof(Aliases))]
    public void DelegatedReadsKeepMemmoveSemanticsInsideOneRegion(bool direct, int profile, bool typed, bool reverse)
    {
        using var s = new Storage(direct, profile); ByteBuf b = s.Buffer;
        byte[] expected = b.AsReadOnlyMemory(0, 8).ToArray(); int source = reverse ? 2 : 0, destination = reverse ? 0 : 2;
        Array.Copy(expected, source, expected, destination, 4); s.Observation.Enabled = true; s.Observation.Fail = false;
        if (typed) b.GetBytes(source, b, destination, 4); else b.GetBytes(source, b.AsSpan(destination, 4));
        Assert.Equal(expected, b.AsReadOnlyMemory(0, 8).ToArray()); Assert.Equal(0, b.ReaderIndex); Assert.Equal(0, b.WriterIndex);
    }
    [Theory]
    [MemberData(nameof(Profiles))]
    public void ScalarBulkAndZeroSuccessKeepBytesAndCursorRules(bool direct, int profile)
    {
        using var s = new Storage(direct, profile); ByteBuf b = s.Buffer;
        s.Observation.Enabled = true; s.Observation.Fail = false;
        b.SetIntLE(3, 0x04030201); Assert.Equal(0x04030201, b.GetIntLE(3));
        b.SetByte(3, 0x7F); Assert.Equal(0x7F, b.GetByte(3));
        byte[] bytes = new byte[4]; b.GetBytes(3, bytes.AsSpan()); Assert.Equal(new byte[] { 0x7F, 2, 3, 4 }, bytes);
        ByteBuf peer = new UnpooledHeapByteBuf(8);
        try { b.GetBytes(3, peer, 1, 4); Assert.Equal(bytes, peer.AsReadOnlyMemory(1, 4).ToArray()); }
        finally { peer.Release(); }
        b.SetIndex(1, 3); b.WriteZero(4); Assert.Equal(7, b.WriterIndex); Assert.Equal(new byte[4], b.AsReadOnlyMemory(3, 4).ToArray());
        b.SetByte(3, 9); b.SetZero(3, 4); Assert.Equal(1, b.ReaderIndex); Assert.Equal(7, b.WriterIndex);
        Assert.DoesNotContain(s.Observation.Calls, x => x.Method == "PublicByte");
    }
}
