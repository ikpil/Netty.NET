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
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteBufLifetimeFixtureTest
{
    private sealed class Scope : IDisposable
    {
        private readonly int _provider;
        private readonly NativeMemoryAllocator _native = new();
        private readonly IByteBufAllocator Allocator;
        public Scope(int provider)
        {
            _provider = provider;
            Allocator = (provider / 2) switch
            {
                1 => new PooledByteBufAllocator(false, maxOrder: 4, smallCacheSize: 0, nativeAllocator: _native),
                2 => new AdaptiveByteBufAllocator(false, false, _native),
                _ => new UnpooledByteBufAllocator(false, true, _native)
            };
        }
        public ByteBuf Buffer(int capacity)
        {
            bool direct = (_provider & 1) != 0;
            ByteBuf Part(int size) => direct ? Allocator.DirectBuffer(size) : Allocator.HeapBuffer(size);
            if (_provider < 6) return Part(capacity);
            CompositeByteBuf result = direct ? Allocator.CompositeDirectBuffer() : Allocator.CompositeHeapBuffer();
            try
            {
                int first = (capacity + 1) / 2;
                result.AddComponent(Part(first).WriteZero(first));
                result.AddComponent(Part(capacity - first).WriteZero(capacity - first));
                return result;
            }
            catch { result.Release(); throw; }
        }
        public void Dispose() { (Allocator as IDisposable)?.Dispose(); Assert.Equal(0, _native.ReservedBytes); }
    }
    private static ByteBuf Entry(ByteBuf root, int form) => form switch
    {
        1 => new ByteBufUtilitySourceTest.Wrapper(root), 2 => root.Duplicate(), _ => root
    };
    private static readonly string[] Methods =
    [
        "testGetBytesAfterRelease",
        "testGetBytesAfterRelease2",
        "testGetBytesAfterRelease3",
        "testGetBytesAfterRelease6",
        "testGetBytesAfterRelease7",
        "testGetBytesAfterRelease8",
        "testSetBytesAfterRelease",
        "testSetBytesAfterRelease2",
        "testSetBytesAfterRelease3",
        "testSetBytesAfterRelease6",
        "testSetBytesAfterRelease7",
        "testSetBytesAfterRelease8",
        "testReadBytesAfterRelease",
        "testReadBytesAfterRelease2",
        "testReadBytesAfterRelease3",
        "testReadBytesAfterRelease4",
        "testReadBytesAfterRelease7",
        "testReadBytesAfterRelease8",
        "testReadBytesAfterRelease9",
        "testReadBytesAfterRelease10",
        "testWriteBytesAfterRelease",
        "testWriteBytesAfterRelease2",
        "testWriteBytesAfterRelease3",
        "testWriteBytesAfterRelease6",
        "testWriteBytesAfterRelease7",
        "testWriteBytesAfterRelease8",
        "testNioBufferAfterRelease",
        "testNioBufferAfterRelease1",
        "testInternalNioBufferAfterRelease",
        "testNioBuffersAfterRelease",
        "testNioBuffersAfterRelease2",
        "testArrayAfterRelease",
        "testMemoryAddressAfterRelease",
        "testSliceAfterReleaseRetainedSlice",
        "testSliceAfterReleaseRetainedSliceDuplicate",
        "testSliceAfterReleaseRetainedSliceRetainedDuplicate",
        "testSliceAfterReleaseRetainedDuplicate",
        "testSliceAfterReleaseRetainedDuplicateSlice",
        "testRetainedSliceAfterReleaseRetainedSlice",
        "testRetainedSliceAfterReleaseRetainedSliceDuplicate",
        "testRetainedSliceAfterReleaseRetainedSliceRetainedDuplicate",
        "testRetainedSliceAfterReleaseRetainedDuplicate",
        "testRetainedSliceAfterReleaseRetainedDuplicateSlice",
        "testDuplicateAfterReleaseRetainedSliceDuplicate",
        "testDuplicateAfterReleaseRetainedDuplicate",
        "testDuplicateAfterReleaseRetainedDuplicateSlice",
        "testRetainedDuplicateAfterReleaseRetainedDuplicate",
        "testRetainedDuplicateAfterReleaseDuplicate",
        "testRetainedDuplicateAfterReleaseRetainedSlice",
        "testRetainedSliceAndRetainedDuplicateContentIsExpected",
        "testRetainedDuplicateAndRetainedSliceContentIsExpected",
        "testMultipleLevelRetainedSlice1",
        "testMultipleLevelRetainedSlice2",
        "testMultipleLevelRetainedSlice3",
        "testMultipleLevelRetainedSlice4",
        "testRetainedSliceUnreleasable1",
        "testRetainedSliceUnreleasable2",
        "testRetainedSliceUnreleasable3",
        "testRetainedSliceUnreleasable4",
        "testReadRetainedSliceUnreleasable1",
        "testReadRetainedSliceUnreleasable2",
        "testReadRetainedSliceUnreleasable3",
        "testReadRetainedSliceUnreleasable4",
        "testRetainedDuplicateUnreleasable1",
        "testRetainedDuplicateUnreleasable2",
        "testRetainedDuplicateUnreleasable3",
        "testRetainedDuplicateUnreleasable4",
        "testRefCnt",
        "testRefCnt2",
    ];
    public static IEnumerable<object[]> Cases()
    {
        for (int provider = 0; provider < 8; ++provider)
        for (int form = 0; form < 3; ++form)
        for (int caseIndex = 0; caseIndex < Methods.Length; ++caseIndex) yield return [provider, form, caseIndex];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task OriginalLifetimeCases(int provider, int form, int caseIndex)
    {
        string upstreamMethod = Methods[caseIndex];
        using var scope = new Scope(provider);
        if (upstreamMethod is "testRefCnt" or "testRefCnt2") { await ReferenceVisibility(scope, form, upstreamMethod == "testRefCnt2"); return; }
        if (upstreamMethod.Contains("Unreleasable", StringComparison.Ordinal)) { Unreleasable(scope, form, upstreamMethod); return; }
        if (upstreamMethod.StartsWith("testMultipleLevel", StringComparison.Ordinal)) { MultipleLevels(scope, form, upstreamMethod[^1] - '1'); return; }
        if (upstreamMethod.EndsWith("ContentIsExpected", StringComparison.Ordinal)) { HandlerContent(scope, form, upstreamMethod.StartsWith("testRetainedSlice", StringComparison.Ordinal)); return; }
        if (upstreamMethod.Contains("BytesAfterRelease", StringComparison.Ordinal) || upstreamMethod.Contains("Nio", StringComparison.Ordinal)
            || upstreamMethod is "testArrayAfterRelease" or "testMemoryAddressAfterRelease") { ReleasedAccess(scope, form, upstreamMethod); return; }
        ReleasedFactories(scope, form, upstreamMethod);
    }
    private static void ReleaseOneEach(IEnumerable<ByteBuf> buffers)
    {
        // Matches the original helper: borrowed aliases share an actual counter and may already be at zero.
        foreach (ByteBuf b in buffers) if (b != null && b.ReferenceCount > 0) b.Release();
    }
    private static void ReleasedFactories(Scope scope, int form, string method)
    {
        ByteBuf raw = scope.Buffer(1), b = Entry(raw, form); var all = new List<ByteBuf> { b };
        try
        {
            string chain = method[(method.IndexOf("AfterRelease", StringComparison.Ordinal) + "AfterRelease".Length)..];
            ByteBuf child;
            if (chain.StartsWith("RetainedSlice", StringComparison.Ordinal)) { child = b.RetainedSlice(0, 1); chain = chain["RetainedSlice".Length..]; }
            else if (chain.StartsWith("RetainedDuplicate", StringComparison.Ordinal)) { child = b.RetainedDuplicate(); chain = chain["RetainedDuplicate".Length..]; }
            else { Assert.Equal("Duplicate", chain); child = b.Duplicate(); chain = string.Empty; }
            all.Add(child);
            if (chain.Length > 0) all.Add(chain switch { "Duplicate" => child.Duplicate(), "RetainedDuplicate" => child.RetainedDuplicate(), _ => child.Slice(0, 1) });
            ReleaseOneEach(all);
            foreach (ByteBuf dead in all)
            {
                Assert.Equal(0, dead.ReferenceCount);
                Assert.Throws<IllegalReferenceCountException>(() =>
                {
                    if (method.StartsWith("testRetainedDuplicate", StringComparison.Ordinal)) dead.RetainedDuplicate();
                    else if (method.StartsWith("testDuplicate", StringComparison.Ordinal)) dead.Duplicate();
                    else if (method.StartsWith("testRetainedSlice", StringComparison.Ordinal)) dead.RetainedSlice();
                    else dead.Slice();
                });
            }
            Assert.Equal(0, raw.ReferenceCount);
        }
        finally { ReleaseOneEach(all); if (raw.ReferenceCount > 0) raw.Release(); }
    }
    private static void Content(ByteBuf b, byte[] expected)
    {
        ByteBuf comparison = Unpooled.WrappedBuffer(expected);
        try { Assert.Equal(0, b.CompareTo(comparison)); Assert.Equal(expected, ByteBufUtil.GetBytes(b)); }
        finally { comparison.Release(); }
    }
    private static void HandlerContent(Scope scope, int form, bool firstSlice)
    {
        ByteBuf raw = scope.Buffer(8), b = Entry(raw, form), first = null, second = null, borrowed = null, last = null;
        bool rootReleased = false, firstReleased = false, secondReleased = false, lastReleased = false;
        try
        {
            b.ResetWriterIndex().WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            first = firstSlice ? b.RetainedSlice(b.ReaderIndex + 1, 6) : b.RetainedDuplicate();
            Content(first, firstSlice ? new byte[] { 2, 3, 4, 5, 6, 7 } : new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            Assert.Equal(0, first.CompareTo(firstSlice ? b.Slice(b.ReaderIndex + 1, 6) : b.Slice()));
            // Simulate a handler that releases the original buffer, and propagates a slice.
            b.Release(); rootReleased = true;
            // Advance the reader index on the slice.
            first.ReadByte();
            second = firstSlice ? first.RetainedDuplicate() : first.RetainedSlice(first.ReaderIndex, 6);
            // The duplicate-first path has its own reader coordinate on the captured six-byte slice.
            if (firstSlice) Content(second, new byte[] { 3, 4, 5, 6, 7 });
            else Content(second, new byte[] { 2, 3, 4, 5, 6, 7 });
            Assert.Equal(0, second.CompareTo(firstSlice ? first.Duplicate() : second.Duplicate()));
            // Advance the reader index on dup1.
            second.ReadByte(); borrowed = second.Duplicate();
            Content(borrowed, firstSlice ? new byte[] { 4, 5, 6, 7 } : new byte[] { 3, 4, 5, 6, 7 });
            // Advance the reader index on dup2.
            borrowed.ReadByte();
            last = borrowed.RetainedSlice(borrowed.ReaderIndex + (firstSlice ? 0 : 1), 3);
            Content(last, new byte[] { 5, 6, 7 });
            Assert.Equal(0, last.CompareTo(borrowed.Slice(borrowed.ReaderIndex + (firstSlice ? 0 : 1), 3)));
            // Cleanup the expected buffers used for testing.
            // CLR expectations are managed arrays; the temporary comparison claims have already been released.
            last.Release(); lastReleased = true;
            if (firstSlice) borrowed.Release(); else second.Release(); secondReleased = true;
            Assert.Equal(last.ReferenceCount, borrowed.ReferenceCount); Assert.Equal(borrowed.ReferenceCount, second.ReferenceCount);
            // The handler is now done with the original slice
            Assert.True(first.Release()); firstReleased = true;
            Assert.All(new[] { b, first, second, borrowed, last }, x => Assert.Equal(0, x.ReferenceCount));
        }
        finally
        {
            if (!lastReleased && last?.ReferenceCount > 0) last.Release();
            if (!secondReleased && second?.ReferenceCount > 0) second.Release();
            if (!firstReleased && first?.ReferenceCount > 0) first.Release();
            if (!rootReleased && b.ReferenceCount > 0) b.Release();
        }
    }
    private static void MultipleLevels(Scope scope, int form, int combination)
    {
        bool doSlice1 = combination < 2, doSlice2 = combination % 2 == 0;
        ByteBuf raw = scope.Buffer(8), b = Entry(raw, form), first = null, second = null;
        bool rootReleased = false, firstReleased = false, secondReleased = false;
        try
        {
            b.ResetWriterIndex().WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            first = b.RetainedSlice(b.ReaderIndex + 1, 6); Content(first, new byte[] { 2, 3, 4, 5, 6, 7 });
            // Simulate a handler that releases the original buffer, and propagates a slice.
            b.Release(); rootReleased = true;
            second = first.RetainedSlice(first.ReaderIndex + 1, 4); Content(second, new byte[] { 3, 4, 5, 6 });
            Assert.Equal(0, second.CompareTo(second.Duplicate())); Assert.Equal(0, second.CompareTo(second.Slice()));
            ByteBuf temporary = second.RetainedDuplicate(); try { Assert.Equal(0, second.CompareTo(temporary)); } finally { temporary.Release(); }
            temporary = second.RetainedSlice(); try { Assert.Equal(0, second.CompareTo(temporary)); } finally { temporary.Release(); }
            ByteBuf third = doSlice1 ? second.Slice(second.ReaderIndex + 1, 2) : second.Duplicate();
            Content(third, doSlice1 ? new byte[] { 4, 5 } : new byte[] { 3, 4, 5, 6 });
            ByteBuf fourth = doSlice2 ? third.Slice(third.ReaderIndex + 1, 1) : third.Duplicate();
            if (doSlice2) Content(fourth, new byte[] { doSlice1 ? (byte)5 : (byte)4 }); else Assert.Equal(0, third.CompareTo(fourth));
            // Slice 4, 3, and 2 should effectively "share" a reference count.
            fourth.Release(); secondReleased = true;
            Assert.Equal(third.ReferenceCount, second.ReferenceCount); Assert.Equal(third.ReferenceCount, fourth.ReferenceCount);
            // Slice 1 should also release the original underlying buffer without throwing exceptions
            Assert.True(first.Release()); firstReleased = true;
            Assert.All(new[] { b, first, second, third, fourth }, x => Assert.Equal(0, x.ReferenceCount));
        }
        finally
        {
            if (!secondReleased && second?.ReferenceCount > 0) second.Release();
            if (!firstReleased && first?.ReferenceCount > 0) first.Release();
            if (!rootReleased && b.ReferenceCount > 0) b.Release();
        }
    }
    private static void Unreleasable(Scope scope, int form, string method)
    {
        int combination = method[^1] - '1'; bool initialRetained = combination < 2, finalRetained = combination % 2 == 0;
        bool duplicate = method.StartsWith("testRetainedDuplicate", StringComparison.Ordinal), read = method.StartsWith("testRead", StringComparison.Ordinal);
        ByteBuf raw = scope.Buffer(8), b = Entry(raw, form), first = null; bool firstReleased = false, rootReleased = false;
        try
        {
            first = duplicate ? (initialRetained ? b.RetainedDuplicate() : b.Duplicate().Retain()) : (initialRetained ? b.RetainedSlice() : b.Slice().Retain());
            ByteBuf middle = Unpooled.UnreleasableBuffer(first);
            ByteBuf last = duplicate ? (finalRetained ? middle.RetainedDuplicate() : middle.Duplicate().Retain())
                : read ? (finalRetained ? middle.ReadRetainedSlice(middle.ReadableBytes) : middle.ReadSlice(middle.ReadableBytes).Retain())
                : finalRetained ? middle.RetainedSlice() : middle.Slice().Retain();
            Assert.False(last.Release()); Assert.False(middle.Release());
            first.Release(); firstReleased = true; Assert.True(b.Release()); rootReleased = true;
            Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, b.ReferenceCount); Assert.Equal(0, last.ReferenceCount);
        }
        finally { if (!firstReleased && first?.ReferenceCount > 0) first.Release(); if (!rootReleased && b.ReferenceCount > 0) b.Release(); }
    }
    private sealed class IoProbe : MemoryStream
    {
        internal int Reads, Writes;
        public override int Read(Span<byte> bytes) { ++Reads; return base.Read(bytes); }
        public override void Write(ReadOnlySpan<byte> bytes) { ++Writes; base.Write(bytes); }
    }
    private static void ReleasedAccess(Scope scope, int form, string method)
    {
        ByteBuf raw = scope.Buffer(8), dead = Entry(raw, form), peer = Unpooled.Buffer(8); byte[] bytes = new byte[8];
        using var io = new IoProbe(); using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            // Clear the buffer so we are sure the reader and writer indices are 0.
            // This is important as we may return a slice from newBuffer(...).
            dead.Clear(); Assert.True(dead.Release());
            if (method == "testWriteBytesAfterRelease2") peer.WriteBytes(bytes);
            int peerReader = peer.ReaderIndex, peerWriter = peer.WriterIndex;
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                switch (method)
                {
                    case "testGetBytesAfterRelease": dead.GetBytes(0, peer); break;
                    case "testGetBytesAfterRelease2": dead.GetBytes(0, peer, 1); break;
                    case "testGetBytesAfterRelease3": dead.GetBytes(0, peer, 0, 1); break;
                    case "testGetBytesAfterRelease6": dead.GetBytes(0, bytes.AsSpan()); break;
                    case "testGetBytesAfterRelease7": dead.GetBytes(0, io, 1); break;
                    case "testGetBytesAfterRelease8": dead.GetBytes(0, socket, 1); break;
                    case "testSetBytesAfterRelease": dead.SetBytes(0, peer); break;
                    case "testSetBytesAfterRelease2": dead.SetBytes(0, peer, 1); break;
                    case "testSetBytesAfterRelease3": dead.SetBytes(0, peer, 0, 1); break;
                    case "testSetBytesAfterRelease6": dead.SetBytes(0, bytes.AsSpan()); break;
                    case "testSetBytesAfterRelease7": dead.SetBytes(0, io, 1); break;
                    case "testSetBytesAfterRelease8": dead.SetBytes(0, socket, 1); break;
                    case "testReadBytesAfterRelease": dead.ReadBytes(1); break;
                    case "testReadBytesAfterRelease2": dead.ReadBytes(peer); break;
                    case "testReadBytesAfterRelease3": dead.ReadBytes(peer, 1); break;
                    case "testReadBytesAfterRelease4": dead.ReadBytes(peer, 0, 1); break;
                    case "testReadBytesAfterRelease7": dead.ReadBytes(bytes.AsSpan()); break;
                    case "testReadBytesAfterRelease8": case "testReadBytesAfterRelease9": dead.ReadBytes(io, 1); break;
                    case "testReadBytesAfterRelease10": dead.ReadBytes(socket, 1); break;
                    case "testWriteBytesAfterRelease": dead.WriteBytes(peer); break;
                    case "testWriteBytesAfterRelease2": dead.WriteBytes(peer, 1); break;
                    case "testWriteBytesAfterRelease3": dead.WriteBytes(peer, 0, 1); break;
                    case "testWriteBytesAfterRelease6": dead.WriteBytes(bytes.AsSpan()); break;
                    case "testWriteBytesAfterRelease7": dead.WriteBytes(io, 1); break;
                    case "testWriteBytesAfterRelease8": dead.WriteBytes(socket, 1); break;
                    case "testNioBufferAfterRelease": dead.AsReadOnlyMemory(dead.ReaderIndex, dead.ReadableBytes); break;
                    case "testNioBufferAfterRelease1": dead.AsReadOnlyMemory(0, 1); break;
                    case "testInternalNioBufferAfterRelease": dead.AsMemory(dead.ReaderIndex, 1); break;
                    case "testNioBuffersAfterRelease": dead.AsReadOnlySequence(dead.ReaderIndex, dead.ReadableBytes); break;
                    case "testNioBuffersAfterRelease2": dead.AsReadOnlySequence(0, 1); break;
                    case "testArrayAfterRelease": dead.AsMemory(0, 0); break;
                    default: Assert.Equal("testMemoryAddressAfterRelease", method); using (dead.AsReadOnlyMemory(0, 0).Pin()) { } break;
                }
            });
            Assert.Equal((0, 0, 0), (dead.ReaderIndex, dead.WriterIndex, dead.ReferenceCount));
            Assert.Equal((peerReader, peerWriter, 1), (peer.ReaderIndex, peer.WriterIndex, peer.ReferenceCount));
            Assert.Equal(0, io.Reads); Assert.Equal(0, io.Writes); Assert.False(socket.Connected); Assert.Equal(new byte[8], bytes);
        }
        finally { if (dead.ReferenceCount > 0) dead.Release(); peer.Release(); }
    }
    private static async Task ReferenceVisibility(Scope scope, int form, bool parameter)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        for (int iteration = 0; iteration < 10; ++iteration)
        {
            ByteBuf raw = scope.Buffer(4), b = Entry(raw, form); Assert.Equal(1, b.ReferenceCount);
            var observed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var keepAlive = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task releaser = Task.Run(async () =>
            {
                try
                {
                    Assert.True(parameter ? b.Release(b.ReferenceCount) : b.Release());
                    observed.TrySetResult(await Task.Run(() => b.ReferenceCount, timeout.Token));
                    // Keep Thread alive a bit so the ThreadLocal caches are not freed
                    // CLR tasks may hop physical threads; explicit allocator cleanup and fresh facades replace JVM recycler lifetime.
                    await keepAlive.Task.WaitAsync(timeout.Token);
                }
                catch (Exception failure) { observed.TrySetException(failure); throw; }
            }, timeout.Token);
            try { Assert.Equal(0, await observed.Task.WaitAsync(timeout.Token)); }
            finally { keepAlive.TrySetResult(); await releaser; if (b.ReferenceCount > 0) b.Release(); }
            Assert.Equal(0, raw.ReferenceCount);
        }
    }
}
