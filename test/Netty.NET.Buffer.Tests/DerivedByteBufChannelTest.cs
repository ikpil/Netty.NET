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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class DerivedByteBufChannelTest
{
    private sealed class Observation
    {
        public int Calls, Index, Length;
        public long Position;
        public object Channel;
        public bool Async, Fail, DelegateToBase;
        public CancellationToken Token;
        public byte[] Written;
        public TaskCompletionSource<int> Pending;
        public int Transfer(ByteBuf root, int index, object channel, long position, int length,
            bool input, bool asynchronous, CancellationToken token = default)
        {
            Calls++; Index = index; Channel = channel; Position = position; Length = length;
            Async = asynchronous; Token = token;
            if (Fail) throw new IOException("parent policy");
            if (DelegateToBase || Pending != null) return 0;
            return Commit(root, index, length, input);
        }
        public int Commit(ByteBuf root, int index, int length, bool input)
        {
            int count = Math.Min(2, length);
            if (input) root.SetBytes(index, new byte[] { 91, 92 }.AsSpan(0, count));
            else Written = ByteBufUtil.GetBytes(root, index, count);
            return count;
        }
        public async ValueTask<int> CompleteAsync(ByteBuf root, int index, int length, bool input, int count)
        {
            if (Pending == null) return count;
            await Pending.Task.ConfigureAwait(false);
            return Commit(root, index, length, input);
        }
    }
    private sealed class ObservedHeap(Observation o) : UnpooledHeapByteBuf(16, 64)
    {
        public override int GetBytes(int index, Socket destination, int length)
        {
            int count = o.Transfer(this, index, destination, 0, length, false, false);
            return o.DelegateToBase ? base.GetBytes(index, destination, length) : count;
        }
        public override async ValueTask<int> GetBytesAsync(int index, Socket destination, int length,
            CancellationToken token = default)
        {
            int count = o.Transfer(this, index, destination, 0, length, false, true, token);
            return o.DelegateToBase ? await base.GetBytesAsync(index, destination, length, token)
                : await o.CompleteAsync(this, index, length, false, count);
        }
        public override int SetBytes(int index, Socket source, int length)
        {
            int count = o.Transfer(this, index, source, 0, length, true, false);
            return o.DelegateToBase ? base.SetBytes(index, source, length) : count;
        }
        public override async ValueTask<int> SetBytesAsync(int index, Socket source, int length,
            CancellationToken token = default)
        {
            int count = o.Transfer(this, index, source, 0, length, true, true, token);
            return o.DelegateToBase ? await base.SetBytesAsync(index, source, length, token)
                : await o.CompleteAsync(this, index, length, true, count);
        }
        public override int GetBytes(int index, SafeFileHandle destination, long position, int length)
        {
            int count = o.Transfer(this, index, destination, position, length, false, false);
            return o.DelegateToBase ? base.GetBytes(index, destination, position, length) : count;
        }
        public override async ValueTask<int> GetBytesAsync(int index, SafeFileHandle destination, long position, int length,
            CancellationToken token = default)
        {
            int count = o.Transfer(this, index, destination, position, length, false, true, token);
            return o.DelegateToBase ? await base.GetBytesAsync(index, destination, position, length, token)
                : await o.CompleteAsync(this, index, length, false, count);
        }
        public override int SetBytes(int index, SafeFileHandle source, long position, int length)
        {
            int count = o.Transfer(this, index, source, position, length, true, false);
            return o.DelegateToBase ? base.SetBytes(index, source, position, length) : count;
        }
        public override async ValueTask<int> SetBytesAsync(int index, SafeFileHandle source, long position, int length,
            CancellationToken token = default)
        {
            int count = o.Transfer(this, index, source, position, length, true, true, token);
            return o.DelegateToBase ? await base.SetBytesAsync(index, source, position, length, token)
                : await o.CompleteAsync(this, index, length, true, count);
        }
    }
    private sealed class ObservedDirect(Observation o, NativeMemoryAllocator native) : UnpooledDirectByteBuf(16, 64, native)
    {
        public override int GetBytes(int index, Socket destination, int length)
        {
            int count = o.Transfer(this, index, destination, 0, length, false, false);
            return o.DelegateToBase ? base.GetBytes(index, destination, length) : count;
        }
        public override async ValueTask<int> GetBytesAsync(int index, Socket destination, int length,
            CancellationToken token = default)
        {
            int count = o.Transfer(this, index, destination, 0, length, false, true, token);
            return o.DelegateToBase ? await base.GetBytesAsync(index, destination, length, token)
                : await o.CompleteAsync(this, index, length, false, count);
        }
        public override int SetBytes(int index, Socket source, int length)
        {
            int count = o.Transfer(this, index, source, 0, length, true, false);
            return o.DelegateToBase ? base.SetBytes(index, source, length) : count;
        }
        public override async ValueTask<int> SetBytesAsync(int index, Socket source, int length,
            CancellationToken token = default)
        {
            int count = o.Transfer(this, index, source, 0, length, true, true, token);
            return o.DelegateToBase ? await base.SetBytesAsync(index, source, length, token)
                : await o.CompleteAsync(this, index, length, true, count);
        }
        public override int GetBytes(int index, SafeFileHandle destination, long position, int length)
        {
            int count = o.Transfer(this, index, destination, position, length, false, false);
            return o.DelegateToBase ? base.GetBytes(index, destination, position, length) : count;
        }
        public override async ValueTask<int> GetBytesAsync(int index, SafeFileHandle destination, long position, int length,
            CancellationToken token = default)
        {
            int count = o.Transfer(this, index, destination, position, length, false, true, token);
            return o.DelegateToBase ? await base.GetBytesAsync(index, destination, position, length, token)
                : await o.CompleteAsync(this, index, length, false, count);
        }
        public override int SetBytes(int index, SafeFileHandle source, long position, int length)
        {
            int count = o.Transfer(this, index, source, position, length, true, false);
            return o.DelegateToBase ? base.SetBytes(index, source, position, length) : count;
        }
        public override async ValueTask<int> SetBytesAsync(int index, SafeFileHandle source, long position, int length,
            CancellationToken token = default)
        {
            int count = o.Transfer(this, index, source, position, length, true, true, token);
            return o.DelegateToBase ? await base.SetBytesAsync(index, source, position, length, token)
                : await o.CompleteAsync(this, index, length, true, count);
        }
    }
    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new();
        public readonly Observation Observation = new();
        public readonly ByteBuf Root, View, Owner;
        public readonly int Offset;
        public Storage(bool direct, int kind)
        {
            Root = direct ? new ObservedDirect(Observation, Native) : new ObservedHeap(Observation);
            for (int i = 0; i < 16; ++i) Root.WriteByte(i);
            Root.SetIndex(2, 14).MarkReaderIndex().MarkWriterIndex();
            if (kind is >= 5 and <= 9)
                Owner = kind is 6 or 8 ? PooledDerivedByteBuf.NewDuplicate(Root, Root, 2, 14)
                    : PooledDerivedByteBuf.NewSlice(Root, Root, 3, 10);
            View = kind switch
            {
                0 => Root.Duplicate(),
                1 => Root.Slice(3, 10),
                2 => Root.AsReadOnly(),
                3 => Root.Slice(3, 10).Slice(2, 6),
                4 => Root.Slice(3, 10).AsReadOnly(),
                5 or 6 => Owner,
                7 => Owner.Slice(2, 6),
                8 or 9 => Owner.Duplicate(),
                _ => Root.Slice(0, 16)
            };
            Offset = kind is 3 or 7 ? 5 : kind is 1 or 4 or 5 or 9 ? 3 : 0;
            View.SetIndex(Offset == 0 ? 3 : 1, Offset == 0 ? 13 : View.Capacity - 1)
                .MarkReaderIndex().MarkWriterIndex();
        }
        public void Dispose()
        {
            if (Owner?.ReferenceCount > 0) Assert.True(Owner.Release());
            if (Root.ReferenceCount > 0) Assert.True(Root.Release());
            Assert.Equal(0, Native.ReservedBytes);
        }
    }


    private sealed class Transport : IDisposable
    {
        private readonly string _path;
        public readonly SafeFileHandle File;
        public readonly Socket Local, Peer;
        public object Channel => (object)File ?? Local;
        public Transport(bool file, bool input)
        {
            if (file)
            {
                _path = Path.GetTempFileName();
                File = System.IO.File.OpenHandle(_path, FileMode.Open, FileAccess.ReadWrite,
                    FileShare.ReadWrite, FileOptions.Asynchronous);
                if (input) RandomAccess.Write(File, new byte[] { 91, 92 }, 17);
            }
            else
            {
                using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                listener.Bind(new IPEndPoint(IPAddress.Loopback, 0)); listener.Listen(1);
                Local = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                Local.ReceiveTimeout = Local.SendTimeout = 5000;
                Local.Connect(listener.LocalEndPoint); Peer = listener.Accept();
                Peer.ReceiveTimeout = Peer.SendTimeout = 5000;
                if (input) { Assert.Equal(2, Peer.Send(new byte[] { 91, 92 })); Peer.Shutdown(SocketShutdown.Send); }
            }
        }
        public byte[] ReadOutput(int count)
        {
            byte[] bytes = new byte[count];
            if (File != null) Assert.Equal(count, RandomAccess.Read(File, bytes, 17));
            else
            {
                int received = 0;
                while (received < count)
                {
                    int n = Peer.Receive(bytes.AsSpan(received)); Assert.True(n > 0); received += n;
                }
            }
            return bytes;
        }
        public void Dispose()
        {
            Local?.Dispose(); Peer?.Dispose(); File?.Dispose();
            if (_path != null) System.IO.File.Delete(_path);
        }
    }
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool file in new[] { false, true })
        foreach (bool direct in new[] { false, true })
        for (int kind = 0; kind < 11; kind++)
        foreach (bool input in new[] { false, true })
        foreach (bool asynchronous in new[] { false, true }) yield return [file, direct, kind, input, asynchronous];
    }
    private static async Task<int> Transfer(ByteBuf b, Transport t, bool input, bool asynchronous,
        CancellationToken token, bool relative = false, int index = 2, int length = 3, long position = 17)
    {
        if (t.File != null)
        {
            if (input) return asynchronous ? await (relative ? b.WriteBytesAsync(t.File, position, length, token)
                : b.SetBytesAsync(index, t.File, position, length, token))
                : relative ? b.WriteBytes(t.File, position, length) : b.SetBytes(index, t.File, position, length);
            return asynchronous ? await (relative ? b.ReadBytesAsync(t.File, position, length, token)
                : b.GetBytesAsync(index, t.File, position, length, token))
                : relative ? b.ReadBytes(t.File, position, length) : b.GetBytes(index, t.File, position, length);
        }
        if (input) return asynchronous ? await (relative ? b.WriteBytesAsync(t.Local, length, token)
            : b.SetBytesAsync(index, t.Local, length, token))
            : relative ? b.WriteBytes(t.Local, length) : b.SetBytes(index, t.Local, length);
        return asynchronous ? await (relative ? b.ReadBytesAsync(t.Local, length, token)
            : b.GetBytesAsync(index, t.Local, length, token))
            : relative ? b.ReadBytes(t.Local, length) : b.GetBytes(index, t.Local, length);
    }
    private static void Observed(Storage s, Transport t, int index, bool asynchronous, int length = 3, long position = 17)
    {
        Assert.Equal(1, s.Observation.Calls); Assert.Equal(index, s.Observation.Index);
        Assert.Same(t.Channel, s.Observation.Channel); Assert.Equal(length, s.Observation.Length);
        Assert.Equal(t.File == null ? 0 : position, s.Observation.Position);
        Assert.Equal(asynchronous, s.Observation.Async);
        Assert.Equal(2, s.Root.ReaderIndex); Assert.Equal(14, s.Root.WriterIndex);
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ChannelViewsPreserveParentPolicyCoordinatesAndActualCounts(bool file, bool direct,
        int kind, bool input, bool asynchronous)
    {
        using var s = new Storage(direct, kind);
        using var t = new Transport(file, input);
        s.View.SetIndex(1, 4).MarkReaderIndex().MarkWriterIndex();
        if (input && s.View.IsReadOnly)
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => Transfer(s.View, t, input, asynchronous, TestContext.Current.CancellationToken));
            Assert.Equal(0, s.Observation.Calls); return;
        }
        int refs = s.Root.ReferenceCount;
        Assert.Equal(2, await Transfer(s.View, t, input, asynchronous, TestContext.Current.CancellationToken));
        Observed(s, t, s.Offset + 2, asynchronous);
        byte[] expected = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        if (input) { expected[s.Offset + 2] = 91; expected[s.Offset + 3] = 92; }
        else Assert.Equal(expected.Skip(s.Offset + 2).Take(2), s.Observation.Written);
        Assert.Equal(expected, ByteBufUtil.GetBytes(s.Root, 0, 16));
        Assert.Equal(1, s.View.ReaderIndex); Assert.Equal(4, s.View.WriterIndex);
        s.View.SetIndex(0, s.View.Capacity).ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(1, s.View.ReaderIndex); Assert.Equal(4, s.View.WriterIndex);
        Assert.Equal(refs, s.Root.ReferenceCount);

        s.View.SetIndex(1, input ? 3 : 4); s.Observation.Calls = 0;
        Assert.Equal(2, await Transfer(s.View, t, input, asynchronous, TestContext.Current.CancellationToken, relative: true));
        Observed(s, t, s.Offset + (input ? 3 : 1), asynchronous);
        Assert.Equal(input ? 1 : 3, s.View.ReaderIndex); Assert.Equal(input ? 5 : 4, s.View.WriterIndex);
        if (input) { expected[s.Offset + 3] = 91; expected[s.Offset + 4] = 92; }
        Assert.Equal(expected, ByteBufUtil.GetBytes(s.Root, 0, 16));

        s.View.SetIndex(1, input ? 3 : 4); s.Observation.Calls = 0; s.Observation.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => Transfer(s.View, t, input, asynchronous, TestContext.Current.CancellationToken, relative: true));
        Observed(s, t, s.Offset + (input ? 3 : 1), asynchronous);
        Assert.Equal(1, s.View.ReaderIndex); Assert.Equal(input ? 3 : 4, s.View.WriterIndex);
        Assert.Equal(expected, ByteBufUtil.GetBytes(s.Root, 0, 16));
        s.Observation.Fail = false; s.Observation.Calls = 0;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Transfer(s.View, t, input, asynchronous, TestContext.Current.CancellationToken, index: s.View.Capacity, length: 1));
        Assert.Equal(0, s.Observation.Calls);
        if (file)
        {
            foreach (long position in new[] { -1L, long.MaxValue })
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Transfer(s.View, t, input, asynchronous, TestContext.Current.CancellationToken, position: position));
            Assert.Equal(0, s.Observation.Calls);
            Assert.Equal(2, await Transfer(s.View, t, input, asynchronous, TestContext.Current.CancellationToken, position: (long)int.MaxValue + 123));
            Observed(s, t, s.Offset + 2, asynchronous, position: (long)int.MaxValue + 123);
            s.Observation.Calls = 0;
        }
        if (asynchronous)
        {
            CancellationToken testToken = TestContext.Current.CancellationToken;
            Task<int> invalid = file ? (input ? s.View.SetBytesAsync(s.View.Capacity, t.File, 17, 1, testToken)
                    : s.View.GetBytesAsync(s.View.Capacity, t.File, 17, 1, testToken)).AsTask()
                : (input ? s.View.SetBytesAsync(s.View.Capacity, t.Local, 1, testToken)
                    : s.View.GetBytesAsync(s.View.Capacity, t.Local, 1, testToken)).AsTask();
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => invalid);
            Assert.Equal(0, s.Observation.Calls);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Task<int> operation = file ? (input ? s.View.SetBytesAsync(2, t.File, 17, 3, cancelled.Token)
                    : s.View.GetBytesAsync(2, t.File, 17, 3, cancelled.Token)).AsTask()
                : (input ? s.View.SetBytesAsync(2, t.Local, 3, cancelled.Token)
                    : s.View.GetBytesAsync(2, t.Local, 3, cancelled.Token)).AsTask();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
            Assert.Equal(0, s.Observation.Calls);
        }
        Assert.Equal(0, await Transfer(s.View, t, input, asynchronous, TestContext.Current.CancellationToken, index: s.View.Capacity, length: 0));
        Observed(s, t, s.Offset + s.View.Capacity, asynchronous, length: 0);
        s.Observation.Calls = 0;
        // Exercise real loopback/positioned-file I/O after observing custom dispatch.
        s.Observation.DelegateToBase = true; s.View.SetIndex(1, input ? 3 : 4);
        Assert.Equal(input ? 2 : 3, await Transfer(s.View, t, input, asynchronous, TestContext.Current.CancellationToken, relative: true));
        Observed(s, t, s.Offset + (input ? 3 : 1), asynchronous);
        Assert.Equal(input ? 1 : 4, s.View.ReaderIndex); Assert.Equal(input ? 5 : 4, s.View.WriterIndex);
        if (input) { expected[s.Offset + 3] = 91; expected[s.Offset + 4] = 92; }
        else Assert.Equal(expected.Skip(s.Offset + 1).Take(3), t.ReadOutput(3));
        Assert.Equal(expected, ByteBufUtil.GetBytes(s.Root, 0, 16));
        Assert.Equal(refs, s.Root.ReferenceCount);
        if (s.Owner != null)
        {
            s.Observation.Calls = 0;
            Assert.True(s.Owner.Release()); Assert.Equal(1, s.Root.ReferenceCount);
            await Assert.ThrowsAsync<IllegalReferenceCountException>(() => Transfer(s.View, t, input, asynchronous, TestContext.Current.CancellationToken));
            Assert.Equal(0, s.Observation.Calls);
        }
    }
    public static IEnumerable<object[]> PendingCases()
    {
        foreach (bool file in new[] { false, true })
        foreach (bool direct in new[] { false, true })
        foreach (int kind in new[] { 0, 1, 2, 5, 6 })
        foreach (bool input in new[] { false, true }) if (kind != 2 || !input) yield return [file, direct, kind, input];
    }
    [Theory]
    [MemberData(nameof(PendingCases))]
    public async Task PendingParentIoForwardsTokenAndPublishesOnlyCompletedViewCursors(bool file, bool direct, int kind, bool input)
    {
        using var s = new Storage(direct, kind);
        using var t = new Transport(file, input);
        using var token = new CancellationTokenSource();
        s.Observation.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        s.View.SetIndex(1, input ? 3 : 4);
        Task<int> operation = Transfer(s.View, t, input, true, relative: true, token: token.Token);
        try
        {
            Assert.False(operation.IsCompleted);
            Observed(s, t, s.Offset + (input ? 3 : 1), true);
            Assert.Equal(token.Token, s.Observation.Token);
            Assert.Equal(1, s.View.ReaderIndex); Assert.Equal(input ? 3 : 4, s.View.WriterIndex);
        }
        finally { s.Observation.Pending.SetResult(2); await operation; }
        Assert.Equal(2, await operation);
        Assert.Equal(input ? 1 : 3, s.View.ReaderIndex); Assert.Equal(input ? 5 : 4, s.View.WriterIndex);
        Assert.Equal(2, s.Root.ReaderIndex); Assert.Equal(14, s.Root.WriterIndex);
        if (input) Assert.Equal(new byte[] { 91, 92 }, ByteBufUtil.GetBytes(s.Root, s.Offset + 3, 2));
        else Assert.Equal(Enumerable.Range(s.Offset + 1, 2).Select(i => (byte)i), s.Observation.Written);
    }
}
