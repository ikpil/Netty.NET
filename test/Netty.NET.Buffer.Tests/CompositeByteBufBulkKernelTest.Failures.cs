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
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public partial class CompositeByteBufBulkKernelTest
{
    public static IEnumerable<object[]> LaterFailures()
    {
        foreach (bool direct in new[] { false, true })
        foreach (bool fixedComposite in new[] { false, true })
        foreach (bool relative in new[] { false, true })
        for (int operation = 0; operation < 3; ++operation)
            if (!fixedComposite || operation != 1) yield return [direct, fixedComposite, relative, operation];
    }
    [Theory]
    [MemberData(nameof(LaterFailures))]
    public void LaterComponentFailureKeepsPublishedPrefixAndReleasesFailedCopies(bool direct, bool fixedComposite, bool relative, int operation)
    {
        var native = new NativeMemoryAllocator(); var observation = new Observation { FailAt = 2 };
        ByteBuf first = direct ? new NativeProbe(observation, native) : new HeapProbe(observation);
        ByteBuf second = direct ? new NativeProbe(observation, native) : new HeapProbe(observation);
        ByteBuf owner = null; ByteBuf peer = new UnpooledHeapByteBuf(32);
        try
        {
            byte[] a = new byte[32], b = new byte[32], peerBytes = new byte[32];
            for (int i = 0; i < 32; ++i) { a[i] = (byte)(i + 1); b[i] = (byte)(i + 33); peerBytes[i] = (byte)(0x80 + i); }
            first.SetBytes(0, a).SetIndex(5, 21); second.SetBytes(0, b).SetIndex(3, 19); peer.WriteBytes(peerBytes);
            owner = fixedComposite ? Unpooled.WrappedUnmodifiableBuffer(first, second)
                : new CompositeByteBuf().AddComponent(first, true).AddComponent(second, true);
            if (relative) owner.SetIndex(operation == 1 ? 1 : 14, operation == 1 ? 14 : 18);
            else owner.SetIndex(1, 2);
            int reader = owner.ReaderIndex, writer = owner.WriterIndex;
            Assert.Throws<ProbeFailure>(() =>
            {
                if (operation == 0) { if (relative) owner.ReadBytes(peer, 4, 4); else owner.GetBytes(14, peer, 4, 4); }
                else if (operation == 1) { if (relative) owner.WriteBytes(peer, 2, 4); else owner.SetBytes(14, peer, 2, 4); }
                else { if (relative) owner.Copy().Release(); else owner.Copy(14, 4).Release(); }
            });
            Assert.Equal(2, observation.Visits.Count);
            Assert.Equal(reader, owner.ReaderIndex); Assert.Equal(writer, owner.WriterIndex);
            if (operation == 0)
            {
                peerBytes[4] = (byte)(a[fixedComposite ? 14 : 19] ^ 0x5A);
                peerBytes[5] = (byte)(a[fixedComposite ? 15 : 20] ^ 0x5A);
            }
            else if (operation == 1) { a[19] = peerBytes[2]; a[20] = peerBytes[3]; }
            else if (!fixedComposite)
                Assert.All(observation.Visits, visit => Assert.Equal(0, visit.Other.ReferenceCount));
            Assert.Equal(a, first.AsReadOnlyMemory(0, 32).ToArray()); Assert.Equal(b, second.AsReadOnlyMemory(0, 32).ToArray());
            Assert.Equal(peerBytes, peer.AsReadOnlyMemory(0, 32).ToArray());
            Assert.Equal(1, owner.ReferenceCount); Assert.Equal(1, first.ReferenceCount); Assert.Equal(1, second.ReferenceCount);
        }
        finally
        {
            if (owner?.ReferenceCount > 0) owner.Release();
            if (first.ReferenceCount > 0) first.Release(); if (second.ReferenceCount > 0) second.Release(); peer.Release();
            Assert.Equal(0, native.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> AliasFailures()
    {
        foreach (bool direct in new[] { false, true })
        foreach (bool fixedComposite in new[] { false, true })
        foreach (bool relative in new[] { false, true }) yield return [direct, fixedComposite, relative];
    }
    [Theory]
    [MemberData(nameof(AliasFailures))]
    public void AliasedGetterFailureDoesNotPublishTheStagedPrefix(bool direct, bool fixedComposite, bool relative)
    {
        var native = new NativeMemoryAllocator(); var observation = new Observation { FailAt = 2 };
        ByteBuf first = direct ? new NativeProbe(observation, native) : new HeapProbe(observation);
        ByteBuf second = direct ? new NativeProbe(observation, native) : new HeapProbe(observation);
        ByteBuf owner = null;
        try
        {
            byte[] a = new byte[32], b = new byte[32];
            for (int i = 0; i < 32; ++i) { a[i] = (byte)(i + 1); b[i] = (byte)(i + 33); }
            first.SetBytes(0, a).SetIndex(5, 21); second.SetBytes(0, b).SetIndex(3, 19);
            owner = fixedComposite ? Unpooled.WrappedUnmodifiableBuffer(first, second)
                : new CompositeByteBuf().AddComponent(first, true).AddComponent(second, true);
            owner.SetIndex(relative ? 14 : 1, relative ? 18 : 2);
            int destination = fixedComposite ? 13 : 18;
            Assert.Throws<ProbeFailure>(() => { if (relative) owner.ReadBytes(first, destination, 4); else owner.GetBytes(14, first, destination, 4); });
            Assert.Equal(2, observation.Visits.Count);
            Assert.All(observation.Visits, visit => { Assert.NotSame(first, visit.Other); Assert.Equal(0, visit.Other.ReferenceCount); });
            Assert.Equal(relative ? 14 : 1, owner.ReaderIndex); Assert.Equal(relative ? 18 : 2, owner.WriterIndex);
            Assert.Equal(a, first.AsReadOnlyMemory(0, 32).ToArray()); Assert.Equal(b, second.AsReadOnlyMemory(0, 32).ToArray());
        }
        finally
        {
            if (owner?.ReferenceCount > 0) owner.Release();
            if (first.ReferenceCount > 0) first.Release(); if (second.ReferenceCount > 0) second.Release();
            Assert.Equal(0, native.ReservedBytes);
        }
    }
}
