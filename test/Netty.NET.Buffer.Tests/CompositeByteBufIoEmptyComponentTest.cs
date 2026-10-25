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
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class CompositeByteBufIoEmptyComponentTest
{
    private sealed class EmptyReadOnly : UnpooledHeapByteBuf
    {
        internal int Writes;
        internal EmptyReadOnly() : base(0, 0) { }
        public override bool IsReadOnly => true;
        public override ByteBuf SetBytes(int index, ReadOnlySpan<byte> source)
        { ++Writes; return base.SetBytes(index, source); }
        public override ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
        { ++Writes; return base.SetBytes(index, source, sourceIndex, length); }
    }
    private sealed class Wrapper(ByteBuf b) : WrappedByteBuf(b) { }
    private sealed class Tracker(ByteBuf expected) : IResourceLeakTracker<ByteBuf>
    {
        internal int Closes;
        public void Record() { }
        public void Record(object hint) { }
        public bool Close(ByteBuf b) { Assert.Same(expected, b); ++Closes; return true; }
    }
    private sealed class Storage : IDisposable
    {
        internal readonly NativeMemoryAllocator Native = new();
        internal readonly IByteBufAllocator Allocator;
        internal readonly CompositeByteBuf Root;
        internal readonly ByteBuf Owner;
        private readonly ByteBuf _lifetimeOwner;
        internal readonly EmptyReadOnly FirstEmpty = new(), SecondEmpty = new();
        internal readonly Tracker Tracker;
        internal readonly int Index;
        internal Storage(bool pooled, bool direct, int facade)
        {
            Allocator = pooled ? new PooledByteBufAllocator(false, maxOrder: 4, nativeAllocator: Native)
                : new UnpooledByteBufAllocator(false, Native);
            ByteBuf Data(int start) { ByteBuf b = direct ? Allocator.DirectBuffer(4, 4) : Allocator.HeapBuffer(4, 4); for (int i = 0; i < 4; ++i) b.WriteByte(start + i); return b; }
            Root = new CompositeByteBuf(Allocator, direct, 16, Data(1), FirstEmpty, Data(5), SecondEmpty, Data(9));
            Root.SetIndex(0, 12); Tracker = new Tracker(Root); Index = facade is 5 or 6 or 9 or 11 ? 1 : 2;
            CompositeByteBuf Nested(ByteBuf b) => new(Allocator, direct, 16, b);
            Owner = facade switch
            {
                0 => Root, 1 => new WrappedCompositeByteBuf(Root),
                2 => new SimpleLeakAwareCompositeByteBuf(Root, Tracker),
                3 => new AdvancedLeakAwareCompositeByteBuf(Root, Tracker),
                4 => new Wrapper(Root), 5 => Root.Slice(1, 10),
                6 => new Wrapper(Root.Slice(1, 10)), 7 => Nested(Root),
                8 => Nested(new AdvancedLeakAwareCompositeByteBuf(Root, Tracker)),
                9 => new Wrapper(Nested(Root).Slice(1, 10)),
                10 => new UnreleasableByteBuf(Root),
                11 => new Wrapper(new UnreleasableByteBuf(Nested(Root).Slice(1, 10))),
                _ => new UnreleasableByteBuf(new AdvancedLeakAwareCompositeByteBuf(Root, Tracker))
            };
            _lifetimeOwner = facade == 11 ? Owner.Unwrap().Unwrap() : facade is 10 or 12 ? Owner.Unwrap() : Owner;
            Root.SetIndex(2, 2);
            Owner.SetIndex(Index, Index).MarkReaderIndex().MarkWriterIndex();
        }
        public void Dispose()
        {
            if (_lifetimeOwner.ReferenceCount > 0) _lifetimeOwner.Release();
            (Allocator as IDisposable)?.Dispose(); Assert.Equal(0, Native.ReservedBytes);
        }
    }
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool pooled in new[] { false, true })
        foreach (bool direct in new[] { false, true })
        for (int facade = 0; facade < 13; ++facade)
        for (int input = 0; input < 3; ++input)
        foreach (bool asynchronous in new[] { false, true })
        foreach (bool relative in new[] { false, true }) yield return [pooled, direct, facade, input, asynchronous, relative];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task InputPublicationSkipsEmptyReadOnlyComponentsAndKeepsViewCoordinates(bool pooled, bool direct, int facade, int input, bool asynchronous, bool relative)
    {
        using var s = new Storage(pooled, direct, facade); byte[] bytes = [91, 92, 93, 94, 95, 96, 97, 98];
        using var stream = new MemoryStream(bytes); using var pair = new Pair(); pair.Send(bytes);
        string path = Path.GetTempFileName();
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, 1, FileOptions.Asynchronous);
            file.Write(new byte[] { 11, 12, 13 }); file.Write(bytes); file.WriteByte(14); file.Position = 1;
            int total = 0;
            while (total < bytes.Length)
            {
                int index = s.Index + total, length = bytes.Length - total;
                var token = TestContext.Current.CancellationToken;
                int count;
                if (input == 0)
                    count = asynchronous ? relative ? await s.Owner.WriteBytesAsync(stream, length, token) : await s.Owner.SetBytesAsync(index, stream, length, token)
                        : relative ? s.Owner.WriteBytes(stream, length) : s.Owner.SetBytes(index, stream, length);
                else if (input == 1)
                    count = asynchronous ? relative ? await s.Owner.WriteBytesAsync(pair.Local, length, token) : await s.Owner.SetBytesAsync(index, pair.Local, length, token)
                        : relative ? s.Owner.WriteBytes(pair.Local, length) : s.Owner.SetBytes(index, pair.Local, length);
                else
                    count = asynchronous ? relative ? await s.Owner.WriteBytesAsync(file.SafeFileHandle, 3 + total, length, token) : await s.Owner.SetBytesAsync(index, file.SafeFileHandle, 3 + total, length, token)
                        : relative ? s.Owner.WriteBytes(file.SafeFileHandle, 3 + total, length) : s.Owner.SetBytes(index, file.SafeFileHandle, 3 + total, length);
                Assert.InRange(count, 1, length); total += count;
                Assert.Equal(s.Index, s.Owner.ReaderIndex); Assert.Equal(s.Index + (relative ? total : 0), s.Owner.WriterIndex);
            }
            Assert.Equal(new byte[] { 1, 2, 91, 92, 93, 94, 95, 96, 97, 98, 11, 12 }, ByteBufUtil.GetBytes(s.Root, 0, 12));
            Assert.Equal(0, s.FirstEmpty.Writes); Assert.Equal(0, s.SecondEmpty.Writes);
            Assert.Equal(1, s.FirstEmpty.ReferenceCount); Assert.Equal(1, s.SecondEmpty.ReferenceCount);
            Assert.Equal(1, file.Position); if (input == 0) Assert.Equal(8, stream.Position);
            s.Owner.ResetWriterIndex().ResetReaderIndex(); Assert.Equal((s.Index, s.Index), (s.Owner.ReaderIndex, s.Owner.WriterIndex));
        }
        finally { File.Delete(path); }
        s.Dispose(); Assert.Equal(facade is 2 or 3 or 8 or 12 ? 1 : 0, s.Tracker.Closes);
    }
    [Theory]
    [InlineData(false, false, false)] [InlineData(false, false, true)]
    [InlineData(false, true, false)] [InlineData(false, true, true)]
    [InlineData(true, false, false)] [InlineData(true, false, true)]
    [InlineData(true, true, false)] [InlineData(true, true, true)]
    public void OrdinaryBulkWritesKeepTheirZeroLengthComponentDispatch(bool pooled, bool direct, bool typed)
    {
        using var s = new Storage(pooled, direct, 0); ByteBuf input = new UnpooledHeapByteBuf(8).WriteBytes(new byte[] { 91, 92, 93, 94, 95, 96, 97, 98 });
        try
        {
            Assert.Throws<NotSupportedException>(() => { if (typed) s.Root.SetBytes(2, input, 0, 8); else s.Root.SetBytes(2, new byte[] { 91, 92, 93, 94, 95, 96, 97, 98 }.AsSpan()); });
            Assert.Equal(1, s.FirstEmpty.Writes); Assert.Equal(0, s.SecondEmpty.Writes);
            Assert.Equal(new byte[] { 1, 2, 91, 92, 5, 6, 7, 8, 9, 10, 11, 12 }, ByteBufUtil.GetBytes(s.Root, 0, 12));
            Assert.Equal((2, 2), (s.Root.ReaderIndex, s.Root.WriterIndex));
        }
        finally { input.Release(); }
    }
    private sealed class Pair : IDisposable
    {
        internal readonly Socket Local, Peer;
        internal Pair()
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0)); listener.Listen(1);
            Local = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp); Local.Connect(listener.LocalEndPoint); Peer = listener.Accept();
            Local.ReceiveTimeout = Local.SendTimeout = Peer.ReceiveTimeout = Peer.SendTimeout = 5000;
        }
        internal void Send(byte[] bytes) { for (int i = 0; i < bytes.Length;) i += Peer.Send(bytes.AsSpan(i)); }
        public void Dispose() { Local.Dispose(); Peer.Dispose(); }
    }
}
