/*
 * Copyright 2016 The Netty Project
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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using Microsoft.Win32.SafeHandles;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public partial class WrappedByteBufDispatchTest
{
    private static ByteBuf Wrap(CompositeByteBuf root, int form, Tracker tracker, out ByteBuf owner)
    {
        ByteBuf inner = form is 7 or 8 ? new AdvancedLeakAwareByteBuf(root, tracker) : root;
        ByteBuf result = form switch
        {
            0 => new Wrapper(root), 1 => new SimpleLeakAwareByteBuf(root, tracker),
            2 => new Wrapper(new Wrapper(root)), 3 => new SimpleLeakAwareByteBuf(new Wrapper(root), tracker),
            4 => new AdvancedLeakAwareByteBuf(root, tracker), 5 => new UnreleasableByteBuf(root),
            6 => new Wrapper(new UnreleasableByteBuf(root)), 7 => new UnreleasableByteBuf(inner),
            8 => new Wrapper(inner), _ => new UnreleasableByteBuf(new Wrapper(root))
        };
        tracker.Expected = form is 7 or 8 ? root : result.Unwrap();
        owner = form == 7 ? inner : form is 5 or 6 or 9 ? root : result;
        return result;
    }
    private static ByteBuf Data(IByteBufAllocator allocator, int count)
    {
        ByteBuf b = allocator.Buffer(count, Math.Max(count, 1));
        for (int i = 0; i < count; ++i) b.WriteByte(i + 1);
        return b;
    }
    private sealed class KernelProbe(IByteBufAllocator allocator, bool direct, ByteBuf[] components)
        : CompositeByteBuf(allocator, direct, 16, components)
    {
        internal readonly DispatchFailure Failure = new();
        internal readonly List<(string Method, int Index, int Width, bool Little)> Calls = [];
        public override byte GetByte(int index) => throw new InvalidOperationException("Public primitive trap");
        private void Visit(string name, int index, int width, bool little)
        { Calls.Add((name, index, width, little)); throw Failure; }
        internal override byte ReadByteCore(int index) { Visit("ReadByte", index, 1, false); return 0; }
        internal override void WriteByteCore(int index, int value) { Assert.Equal(17, value); Visit("WriteByte", index, 1, false); }
        internal override ulong ReadWord(int index, int width, bool little) { Visit("ReadWord", index, width, little); return 0; }
        internal override ByteBuf SetWord(int index, ulong value, int width, bool little)
        { Assert.Equal(17UL, value); Visit("SetWord", index, width, little); return this; }
    }
    public static IEnumerable<object[]> KernelCases()
    {
        foreach (bool direct in new[] { false, true })
        for (int form = 0; form < 10; ++form)
        for (int operation = 0; operation < 6; ++operation) yield return [direct, form, operation];
    }
    [Theory]
    [MemberData(nameof(KernelCases))]
    public void InternalPrimitivesKeepWrappedKernelsSeparateFromPublicOverrides(bool direct, int form, int operation)
    {
        var native = new NativeMemoryAllocator(); var allocator = new UnpooledByteBufAllocator(direct, native);
        ByteBuf data = Data(allocator, 32); var root = new KernelProbe(allocator, direct, [data]);
        var tracker = new Tracker(); ByteBuf b = Wrap(root, form, tracker, out ByteBuf owner);
        try
        {
            void Request()
            {
                switch (operation)
                {
                    case 0: b.ReadByteCore(3); break;
                    case 1: b.WriteByteCore(3, 17); break;
                    case 2: b.ReadWord(3, 8, false); break;
                    case 3: b.ReadWord(3, 8, true); break;
                    case 4: b.SetWord(3, 17, 4, false); break;
                    default: b.SetWord(3, 17, 4, true); break;
                }
            }
            Assert.Same(root.Failure, Assert.Throws<DispatchFailure>(() => Request()));
            var call = Assert.Single(root.Calls);
            Assert.Equal(operation switch { 0 => "ReadByte", 1 => "WriteByte", 2 or 3 => "ReadWord", _ => "SetWord" }, call.Method);
            Assert.Equal((3, operation < 2 ? 1 : operation < 4 ? 8 : 4, operation is 3 or 5), (call.Index, call.Width, call.Little));
            Assert.Equal((0, 32, 1), (root.ReaderIndex, root.WriterIndex, root.ReferenceCount));
            Assert.Equal(4, data.GetByte(3)); Assert.Equal(0, tracker.Records);
        }
        finally { owner.Release(); Assert.Equal(0, native.ReservedBytes); }
        Assert.Equal(form is 1 or 3 or 4 or 7 or 8 ? 1 : 0, tracker.Closes);
    }

    // A pending single-consumption CLR operation proves that forwarding preserves
    // the original completion/exception/token, rather than executing local I/O.
    private sealed class PendingResult : IValueTaskSource<int>
    {
        private ManualResetValueTaskSourceCore<int> _core = new() { RunContinuationsAsynchronously = true };
        internal int Consumptions, Continuations;
        internal ValueTask<int> Task => new(this, _core.Version);
        internal void Complete(int value) => _core.SetResult(value);
        internal void Fail(Exception failure) => _core.SetException(failure);
        public int GetResult(short token) { ++Consumptions; return _core.GetResult(token); }
        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
        public void OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
        { ++Continuations; _core.OnCompleted(continuation, state, token, flags); }
    }
    private sealed class PendingProbe(IByteBufAllocator allocator, bool direct, ByteBuf[] components)
        : CompositeByteBuf(allocator, direct, 16, components)
    {
        internal readonly PendingResult Result = new();
        internal readonly DispatchFailure Failure = new();
        internal string Method;
        internal object Peer;
        internal int Index, Length, Calls;
        internal long Position;
        internal CancellationToken Token;
        private ValueTask<int> Begin(string method, object peer, int index, long position, int length, CancellationToken token)
        { ++Calls; Method = method; Peer = peer; Index = index; Position = position; Length = length; Token = token; return Result.Task; }
        public override ValueTask<int> SetBytesAsync(int index, Stream source, int length, CancellationToken token = default)
            => Begin("SetStream", source, index, 0, length, token);
        public override ValueTask<int> WriteBytesAsync(Stream source, int length, CancellationToken token = default)
            => Begin("WriteStream", source, WriterIndex, 0, length, token);
        public override ValueTask<int> GetBytesAsync(int index, Socket destination, int length, CancellationToken token = default)
            => Begin("GetSocket", destination, index, 0, length, token);
        public override ValueTask<int> ReadBytesAsync(Socket destination, int length, CancellationToken token = default)
            => Begin("ReadSocket", destination, ReaderIndex, 0, length, token);
        public override ValueTask<int> SetBytesAsync(int index, SafeFileHandle source, long position, int length, CancellationToken token = default)
            => Begin("SetFile", source, index, position, length, token);
        public override ValueTask<int> WriteBytesAsync(SafeFileHandle source, long position, int length, CancellationToken token = default)
            => Begin("WriteFile", source, WriterIndex, position, length, token);
    }
    public static IEnumerable<object[]> PendingCases()
    {
        foreach (bool direct in new[] { false, true })
        for (int form = 0; form < 10; ++form)
        for (int operation = 0; operation < 6; ++operation)
        for (int mode = 0; mode < 3; ++mode) yield return [direct, form, operation, mode];
    }
    [Theory]
    [MemberData(nameof(PendingCases))]
    public async Task AsyncForwardingPreservesSingleCompletionRelativeStateAndCancellation(bool direct, int form, int operation, int mode)
    {
        var native = new NativeMemoryAllocator(); var allocator = new UnpooledByteBufAllocator(direct, native);
        var root = new PendingProbe(allocator, direct, [Data(allocator, 32)]); root.SetIndex(3, 10);
        var tracker = new Tracker(); ByteBuf b = Wrap(root, form, tracker, out ByteBuf owner);
        using var stream = new MemoryStream(new byte[64]);
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        using var file = new SafeFileHandle(nint.Zero, false); using var cancellation = new CancellationTokenSource();
        if (mode == 2) cancellation.Cancel();
        try
        {
            CancellationToken token = cancellation.Token;
            ValueTask<int> pending = operation switch
            {
                0 => b.SetBytesAsync(5, stream, 4, token),
                1 => b.WriteBytesAsync(stream, 4, token),
                2 => b.GetBytesAsync(5, socket, 4, token),
                3 => b.ReadBytesAsync(socket, 4, token),
                4 => b.SetBytesAsync(5, file, 11, 4, token),
                _ => b.WriteBytesAsync(file, 11, 4, token)
            };
            Assert.False(pending.IsCompleted); Assert.Equal(1, root.Calls);
            Assert.Equal(new[] { "SetStream", "WriteStream", "GetSocket", "ReadSocket", "SetFile", "WriteFile" }[operation], root.Method);
            Assert.Same(operation < 2 ? stream : operation < 4 ? (object)socket : file, root.Peer);
            Assert.Equal(operation is 1 or 5 ? 10 : operation == 3 ? 3 : 5, root.Index);
            Assert.Equal(operation >= 4 ? 11 : 0, root.Position); Assert.Equal(4, root.Length); Assert.Equal(token, root.Token);
            Task<int> task = pending.AsTask(); Assert.False(task.IsCompleted);
            Assert.Equal((3, 10), (b.ReaderIndex, b.WriterIndex));
            if (mode == 0)
            {
                // The wrapped operation owns index publication. The wrapper must not advance twice.
                if (operation is 1 or 5) root.WriterIndex += 2;
                if (operation == 3) root.ReaderIndex += 2;
                root.Result.Complete(2); Assert.Equal(2, await task);
            }
            else if (mode == 1)
            { root.Result.Fail(root.Failure); Assert.Same(root.Failure, await Assert.ThrowsAsync<DispatchFailure>(() => task)); }
            else
            { root.Result.Fail(new OperationCanceledException(token)); Assert.Equal(token, (await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task)).CancellationToken); }
            Assert.Equal((mode == 0 && operation == 3 ? 5 : 3, mode == 0 && operation is 1 or 5 ? 12 : 10), (b.ReaderIndex, b.WriterIndex));
            Assert.Equal(1, root.Result.Consumptions); Assert.Equal(1, root.Result.Continuations);
            Assert.Equal(form is 4 or 7 or 8 ? 1 : 0, tracker.Records); Assert.Equal(0, tracker.Closes);
        }
        finally { owner.Release(); Assert.Equal(0, native.ReservedBytes); }
        Assert.Equal(form is 1 or 3 or 4 or 7 or 8 ? 1 : 0, tracker.Closes);
    }

    public static IEnumerable<object[]> NestedCases()
    {
        foreach (bool direct in new[] { false, true })
        for (int form = 0; form < 3; ++form)
        for (int operation = 0; operation < 10; ++operation) yield return [direct, form, operation];
    }
    [Theory]
    [MemberData(nameof(NestedCases))]
    public async Task OrdinaryOuterWrapperPreservesInnerAdvancedTrackerAndFluentOwner(bool direct, int form, int operation)
    {
        var native = new NativeMemoryAllocator(); var allocator = new UnpooledByteBufAllocator(direct, native);
        var root = new CompositeByteBuf(allocator, direct, 16, Data(allocator, 8), Data(allocator, 24)); root.SetIndex(3, 16);
        var innerTracker = new Tracker { Expected = root }; var inner = new AdvancedLeakAwareByteBuf(root, innerTracker);
        var outerTracker = new Tracker { Expected = inner };
        ByteBuf b = form == 0 ? new Wrapper(inner) : form == 1 ? new SimpleLeakAwareByteBuf(inner, outerTracker) : new UnreleasableByteBuf(inner);
        ByteBuf owner = form == 2 ? inner : b;
        using var stream = new MemoryStream(new byte[] { 41, 42, 43, 44 });
        try
        {
            switch (operation)
            {
                case 0: Assert.Equal(4, b.GetByte(3)); break;
                case 1: Assert.Same(b, b.SetIntLE(3, 0x01020304)); break;
                case 2: byte[] bytes = new byte[4]; Assert.Same(b, b.GetBytes(3, bytes.AsSpan())); Assert.Equal(new byte[] { 4, 5, 6, 7 }, bytes); break;
                case 3: Assert.Equal(4, b.ReadByte()); Assert.Equal(4, b.ReaderIndex); break;
                case 4: Assert.Same(b, b.WriteByte(17)); Assert.Equal(17, b.WriterIndex); break;
                case 5: Assert.Equal(new byte[] { 4, 5, 6, 7 }, b.AsReadOnlySequence(3, 4).ToArray()); break;
                case 6: Assert.Equal(4, await b.SetBytesAsync(3, stream, 4, TestContext.Current.CancellationToken)); break;
                case 7: Assert.Same(b, b.SetBoolean(3, true)); break;
                case 8: Assert.Same(b, b.WriteZero(2)); Assert.Equal(18, b.WriterIndex); break;
                default: b.GetFloatLE(3); break;
            }
            Assert.Equal(1, innerTracker.Records); Assert.Equal(0, outerTracker.Records);
            Assert.Equal(0, innerTracker.Closes); Assert.Equal(0, outerTracker.Closes);
        }
        finally { owner.Release(); Assert.Equal(0, native.ReservedBytes); }
        Assert.Equal(1, innerTracker.Closes); Assert.Equal(form == 1 ? 1 : 0, outerTracker.Closes);
    }
}
