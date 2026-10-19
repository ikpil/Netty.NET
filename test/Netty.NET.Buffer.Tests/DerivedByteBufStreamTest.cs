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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class DerivedByteBufStreamTest
{
    private sealed class Observation
    {
        public int Calls, Index, Length;
        public bool Async, Fail;
        public Stream Stream;
        public CancellationToken Token;
        public void Visit(int index, Stream stream, int length, bool asynchronous, CancellationToken token = default)
        {
            Calls++; Index = index; Length = length; Stream = stream; Async = asynchronous; Token = token;
            if (Fail) throw new IOException("parent policy");
        }
    }
    private sealed class ObservedHeap(Observation o) : UnpooledHeapByteBuf(16, 64)
    {
        public override ByteBuf GetBytes(int index, Stream destination, int length)
        { o.Visit(index, destination, length, false); return base.GetBytes(index, destination, length); }
        public override int SetBytes(int index, Stream source, int length)
        { o.Visit(index, source, length, false); return base.SetBytes(index, source, length); }
        public override async ValueTask GetBytesAsync(int index, Stream destination, int length, CancellationToken token = default)
        { o.Visit(index, destination, length, true, token); await base.GetBytesAsync(index, destination, length, token); }
        public override async ValueTask<int> SetBytesAsync(int index, Stream source, int length, CancellationToken token = default)
        { o.Visit(index, source, length, true, token); return await base.SetBytesAsync(index, source, length, token); }
    }
    private sealed class ObservedDirect(Observation o, NativeMemoryAllocator native) : UnpooledDirectByteBuf(16, 64, native)
    {
        public override ByteBuf GetBytes(int index, Stream destination, int length)
        { o.Visit(index, destination, length, false); return base.GetBytes(index, destination, length); }
        public override int SetBytes(int index, Stream source, int length)
        { o.Visit(index, source, length, false); return base.SetBytes(index, source, length); }
        public override async ValueTask GetBytesAsync(int index, Stream destination, int length, CancellationToken token = default)
        { o.Visit(index, destination, length, true, token); await base.GetBytesAsync(index, destination, length, token); }
        public override async ValueTask<int> SetBytesAsync(int index, Stream source, int length, CancellationToken token = default)
        { o.Visit(index, source, length, true, token); return await base.SetBytesAsync(index, source, length, token); }
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


    public static IEnumerable<object[]> Cases()
    {
        foreach (bool direct in new[] { false, true })
        for (int kind = 0; kind < 11; kind++)
        foreach (bool input in new[] { false, true })
        foreach (bool asynchronous in new[] { false, true }) yield return [direct, kind, input, asynchronous];
    }
    private sealed class Input() : MemoryStream(new byte[] { 91, 92, 93 })
    {
        public override int Read(Span<byte> destination) => base.Read(destination[..Math.Min(2, destination.Length)]);
        public override ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken token = default)
            => base.ReadAsync(destination[..Math.Min(2, destination.Length)], token);
    }
    private static async Task Transfer(ByteBuf b, Stream stream, bool input, bool asynchronous, bool relative)
    {
        if (input)
        {
            int count = asynchronous ? await (relative ? b.WriteBytesAsync(stream, 3) : b.SetBytesAsync(2, stream, 3))
                : relative ? b.WriteBytes(stream, 3) : b.SetBytes(2, stream, 3);
            Assert.Equal(2, count);
        }
        else if (asynchronous)
        {
            if (relative) await b.ReadBytesAsync(stream, 3); else await b.GetBytesAsync(2, stream, 3);
        }
        else Assert.Same(b, relative ? b.ReadBytes(stream, 3) : b.GetBytes(2, stream, 3));
    }
    private static void Observed(Storage s, Stream stream, int index, bool asynchronous)
    {
        Assert.Equal(1, s.Observation.Calls); Assert.Equal(index, s.Observation.Index);
        Assert.Equal(3, s.Observation.Length); Assert.Same(stream, s.Observation.Stream);
        Assert.Equal(asynchronous, s.Observation.Async);
        Assert.Equal(2, s.Root.ReaderIndex); Assert.Equal(14, s.Root.WriterIndex);
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task AbsoluteAndRelativeStreamsPreserveParentPolicyAndViewCoordinates(bool direct, int kind, bool input, bool asynchronous)
    {
        using var s = new Storage(direct, kind);
        s.View.SetIndex(1, 4).MarkReaderIndex().MarkWriterIndex();
        using Stream stream = input ? new Input() : new MemoryStream();
        if (input && s.View.IsReadOnly)
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => Transfer(s.View, stream, true, asynchronous, false));
            Assert.Equal(0, s.Observation.Calls); Assert.Equal(0, stream.Position); return;
        }
        int references = s.Root.ReferenceCount;
        await Transfer(s.View, stream, input, asynchronous, false);
        Observed(s, stream, s.Offset + 2, asynchronous);
        byte[] expected = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        if (input) { expected[s.Offset + 2] = 91; expected[s.Offset + 3] = 92; }
        else Assert.Equal(expected.Skip(s.Offset + 2).Take(3), ((MemoryStream)stream).ToArray());
        Assert.Equal(expected, ByteBufUtil.GetBytes(s.Root, 0, 16));
        Assert.Equal(1, s.View.ReaderIndex); Assert.Equal(4, s.View.WriterIndex);
        s.View.SetIndex(0, s.View.Capacity).ResetWriterIndex().ResetReaderIndex();
        Assert.Equal(1, s.View.ReaderIndex); Assert.Equal(4, s.View.WriterIndex);
        Assert.Equal(references, s.Root.ReferenceCount);

        s.Root.SetBytes(0, Enumerable.Range(0, 16).Select(i => (byte)i).ToArray());
        s.View.SetIndex(1, input ? 3 : 4); s.Observation.Calls = 0;
        using Stream relative = input ? new Input() : new MemoryStream();
        await Transfer(s.View, relative, input, asynchronous, true);
        Observed(s, relative, s.Offset + (input ? 3 : 1), asynchronous);
        Assert.Equal(input ? 1 : 4, s.View.ReaderIndex); Assert.Equal(input ? 5 : 4, s.View.WriterIndex);
        expected = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        if (input) { expected[s.Offset + 3] = 91; expected[s.Offset + 4] = 92; }
        else Assert.Equal(expected.Skip(s.Offset + 1).Take(3), ((MemoryStream)relative).ToArray());
        Assert.Equal(expected, ByteBufUtil.GetBytes(s.Root, 0, 16));

        s.View.SetIndex(1, 4); s.Observation.Calls = 0; s.Observation.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => Transfer(s.View, stream, input, asynchronous, false));
        Observed(s, stream, s.Offset + 2, asynchronous);
        Assert.Equal(1, s.View.ReaderIndex); Assert.Equal(4, s.View.WriterIndex);
        Assert.Equal(expected, ByteBufUtil.GetBytes(s.Root, 0, 16));
        s.Observation.Fail = false; s.Observation.Calls = 0;
        // CLR view guards stay before custom parent dispatch, including duplicates.
        if (asynchronous)
        {
            Task operation = input ? s.View.SetBytesAsync(s.View.Capacity, stream, 1, TestContext.Current.CancellationToken).AsTask()
                : s.View.GetBytesAsync(s.View.Capacity, stream, 1, TestContext.Current.CancellationToken).AsTask();
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => operation);
        }
        else if (input) Assert.Throws<ArgumentOutOfRangeException>(() => s.View.SetBytes(s.View.Capacity, stream, 1));
        else Assert.Throws<ArgumentOutOfRangeException>(() => s.View.GetBytes(s.View.Capacity, stream, 1));
        Assert.Equal(0, s.Observation.Calls);
        if (s.Owner != null)
        {
            Assert.True(s.Owner.Release()); Assert.Equal(1, s.Root.ReferenceCount);
            await Assert.ThrowsAsync<IllegalReferenceCountException>(() => Transfer(s.View, stream, input, asynchronous, false));
            Assert.Equal(0, s.Observation.Calls);
        }
    }

    private sealed class Deferred(bool input) : Stream
    {
        private readonly TaskCompletionSource<int> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Memory<byte> _read;
        private ReadOnlyMemory<byte> _write;
        public byte[] Written;
        public CancellationToken Token;
        public override bool CanRead => input;
        public override bool CanWrite => !input;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] bytes, int offset, int length) => throw new NotSupportedException();
        public override void Write(byte[] bytes, int offset, int length) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> bytes, CancellationToken token = default)
        { _read = bytes; Token = token; return new(_done.Task); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token = default)
        { _write = bytes; Token = token; return new(_done.Task); }
        public void Complete()
        {
            if (input) { _read.Span[0] = 91; _read.Span[1] = 92; }
            else Written = _write.ToArray();
            _done.SetResult(input ? 2 : 0);
        }
    }
    public static IEnumerable<object[]> PendingCases()
    {
        foreach (bool direct in new[] { false, true })
        foreach (int kind in new[] { 0, 1, 2, 5, 6 })
        foreach (bool input in new[] { false, true }) if (kind != 2 || !input) yield return [direct, kind, input];
    }
    [Theory]
    [MemberData(nameof(PendingCases))]
    public async Task PendingAsyncIoForwardsTokenAndPublishesOnlyViewCursorsAfterCompletion(bool direct, int kind, bool input)
    {
        using var s = new Storage(direct, kind);
        using var stream = new Deferred(input);
        using var cancellation = new CancellationTokenSource();
        s.View.SetIndex(1, input ? 3 : 4);
        Task<int> read = input ? s.View.WriteBytesAsync(stream, 3, cancellation.Token).AsTask() : null;
        Task operation = read ?? s.View.ReadBytesAsync(stream, 3, cancellation.Token).AsTask();
        try
        {
            Assert.False(operation.IsCompleted);
            Observed(s, stream, s.Offset + (input ? 3 : 1), true);
            Assert.Equal(cancellation.Token, s.Observation.Token); Assert.Equal(cancellation.Token, stream.Token);
            Assert.Equal(1, s.View.ReaderIndex); Assert.Equal(input ? 3 : 4, s.View.WriterIndex);
        }
        finally { stream.Complete(); await operation; }
        if (input) Assert.Equal(2, await read);
        else Assert.Equal(Enumerable.Range(s.Offset + 1, 3).Select(i => (byte)i), stream.Written);
        Assert.Equal(input ? 1 : 4, s.View.ReaderIndex); Assert.Equal(input ? 5 : 4, s.View.WriterIndex);
        Assert.Equal(2, s.Root.ReaderIndex); Assert.Equal(14, s.Root.WriterIndex);
    }
}
