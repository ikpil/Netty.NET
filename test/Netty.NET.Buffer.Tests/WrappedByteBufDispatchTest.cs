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
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// Verify public entry/arguments against a throwing wrapped buffer, separately from the
// existing ordinary-value matrix. A different nested call is an observable failure.
public partial class WrappedByteBufDispatchTest
{
    private sealed class DispatchFailure : Exception { }
    private sealed class Tracker : IResourceLeakTracker<ByteBuf>
    {
        internal ByteBuf Expected;
        internal int Records, Closes;
        public void Record() => ++Records;
        public void Record(object hint) => ++Records;
        public bool Close(ByteBuf value) { Assert.Same(Expected, value); ++Closes; return true; }
    }
    private sealed class Context : IDisposable
    {
        internal readonly NativeMemoryAllocator Native = new();
        internal readonly CancellationTokenSource Cancellation = new();
        internal readonly byte[] Bytes = [1, 2, 3, 4, 5, 6, 7, 8];
        internal readonly MemoryStream Stream = new(new byte[64]);
        internal readonly Socket Socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        internal readonly SafeFileHandle File = new(nint.Zero, false);
        internal readonly Tracker Tracker = new();
        internal readonly Probe Root;
        internal readonly ByteBuf Buffer;
        internal readonly ByteBuf LifetimeOwner;
        internal readonly int Form;
        internal readonly ByteBuf Peer;
        internal readonly ByteBuf[] Batch;
        private readonly ByteBuf[] _components;
        internal Context(bool direct, int form)
        {
            var allocator = new UnpooledByteBufAllocator(direct, Native);
            ByteBuf Data(int length) { ByteBuf b = allocator.Buffer(length, length); for (int i = 0; i < length; ++i) b.WriteByte(i + 1); return b; }
            _components = [Data(8), Data(0), Data(24), Data(32)];
            Peer = Data(64); Peer.SetIndex(1, 17); Batch = [Data(8), Data(8)];
            Root = new Probe(allocator, direct, _components); Root.C = this; Root.SetIndex(3, 40);
            Form = form;
            ByteBuf inner = form is 7 or 8 ? new AdvancedLeakAwareByteBuf(Root, Tracker) : Root;
            Buffer = form switch
            {
                0 => new Wrapper(Root),
                1 => new SimpleLeakAwareByteBuf(Root, Tracker),
                2 => new Wrapper(new Wrapper(Root)),
                3 => new SimpleLeakAwareByteBuf(new Wrapper(Root), Tracker),
                4 => new AdvancedLeakAwareByteBuf(Root, Tracker),
                5 => new UnreleasableByteBuf(Root),
                6 => new Wrapper(new UnreleasableByteBuf(Root)),
                7 => new UnreleasableByteBuf(inner),
                8 => new Wrapper(inner),
                _ => new UnreleasableByteBuf(new Wrapper(Root))
            };
            LifetimeOwner = form == 7 ? inner : form is 5 or 6 or 9 ? Root : Buffer;
            Tracker.Expected = form is 7 or 8 ? Root : Buffer.Unwrap();
        }
        public void Dispose()
        {
            if (LifetimeOwner.ReferenceCount > 0) LifetimeOwner.Release();
            foreach (ByteBuf b in _components) if (b.ReferenceCount > 0) b.Release();
            if (Peer.ReferenceCount > 0) Peer.Release();
            foreach (ByteBuf b in Batch) if (b.ReferenceCount > 0) b.Release();
            Stream.Dispose(); Socket.Dispose(); File.Dispose(); Cancellation.Dispose();
            Assert.Equal(0, Native.ReservedBytes);
        }
    }
    private sealed class Wrapper(ByteBuf b) : WrappedByteBuf(b) { }
    private sealed class Probe(IByteBufAllocator allocator, bool direct, ByteBuf[] components)
        : CompositeByteBuf(allocator, direct, 16, components)
    {
        internal Context C;
        internal readonly DispatchFailure Failure = new();
        internal readonly List<string> Calls = [];
        private void Visit(string method) { Calls.Add(method); }
    public override Memory<byte> AsMemory(int index, int length)
    {
        Visit("AsMemory(int,int)");
        Assert.Equal(3, index);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override Span<byte> AsSpan(int index, int length)
    {
        Visit("AsSpan(int,int)");
        Assert.Equal(3, index);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override byte GetByte(int index)
    {
        Visit("GetByte(int)");
        Assert.Equal(3, index);
        throw Failure;
    }
    public override bool GetBoolean(int index)
    {
        Visit("GetBoolean(int)");
        Assert.Equal(3, index);
        throw Failure;
    }
    public override ByteBuf SetByte(int index, int value)
    {
        Visit("SetByte(int,int)");
        Assert.Equal(3, index);
        Assert.Equal(305419896, value);
        throw Failure;
    }
    public override ByteBuf SetBoolean(int index, bool value)
    {
        Visit("SetBoolean(int,bool)");
        Assert.Equal(3, index);
        Assert.True(value);
        throw Failure;
    }
    public override short GetShort(int index)
    {
        Visit("GetShort(int)");
        Assert.Equal(3, index);
        throw Failure;
    }
    public override ByteBuf SetShort(int index, int value)
    {
        Visit("SetShort(int,int)");
        Assert.Equal(3, index);
        Assert.Equal(305419896, value);
        throw Failure;
    }
    public override short GetShortLE(int index)
    {
        Visit("GetShortLE(int)");
        Assert.Equal(3, index);
        throw Failure;
    }
    public override ByteBuf SetShortLE(int index, int value)
    {
        Visit("SetShortLE(int,int)");
        Assert.Equal(3, index);
        Assert.Equal(305419896, value);
        throw Failure;
    }
    public override int GetInt(int index)
    {
        Visit("GetInt(int)");
        Assert.Equal(3, index);
        throw Failure;
    }
    public override ByteBuf SetInt(int index, int value)
    {
        Visit("SetInt(int,int)");
        Assert.Equal(3, index);
        Assert.Equal(305419896, value);
        throw Failure;
    }
    public override int GetIntLE(int index)
    {
        Visit("GetIntLE(int)");
        Assert.Equal(3, index);
        throw Failure;
    }
    public override ByteBuf SetIntLE(int index, int value)
    {
        Visit("SetIntLE(int,int)");
        Assert.Equal(3, index);
        Assert.Equal(305419896, value);
        throw Failure;
    }
    public override long GetLong(int index)
    {
        Visit("GetLong(int)");
        Assert.Equal(3, index);
        throw Failure;
    }
    public override ByteBuf SetLong(int index, long value)
    {
        Visit("SetLong(int,long)");
        Assert.Equal(3, index);
        Assert.Equal(81985529216486895L, value);
        throw Failure;
    }
    public override long GetLongLE(int index)
    {
        Visit("GetLongLE(int)");
        Assert.Equal(3, index);
        throw Failure;
    }
    public override ByteBuf SetLongLE(int index, long value)
    {
        Visit("SetLongLE(int,long)");
        Assert.Equal(3, index);
        Assert.Equal(81985529216486895L, value);
        throw Failure;
    }
    public override int GetUnsignedMedium(int index)
    {
        Visit("GetUnsignedMedium(int)");
        Assert.Equal(3, index);
        throw Failure;
    }
    public override int GetUnsignedMediumLE(int index)
    {
        Visit("GetUnsignedMediumLE(int)");
        Assert.Equal(3, index);
        throw Failure;
    }
    public override int GetMedium(int index)
    {
        Visit("GetMedium(int)");
        Assert.Equal(3, index);
        throw Failure;
    }
    public override int GetMediumLE(int index)
    {
        Visit("GetMediumLE(int)");
        Assert.Equal(3, index);
        throw Failure;
    }
    public override ByteBuf SetMedium(int index, int value)
    {
        Visit("SetMedium(int,int)");
        Assert.Equal(3, index);
        Assert.Equal(305419896, value);
        throw Failure;
    }
    public override ByteBuf SetMediumLE(int index, int value)
    {
        Visit("SetMediumLE(int,int)");
        Assert.Equal(3, index);
        Assert.Equal(305419896, value);
        throw Failure;
    }
    public override ushort GetUnsignedShort(int index)
    {
        Visit("GetUnsignedShort(int)");
        Assert.Equal(3, index);
        throw Failure;
    }
    public override ushort GetUnsignedShortLE(int index)
    {
        Visit("GetUnsignedShortLE(int)");
        Assert.Equal(3, index);
        throw Failure;
    }
    public override uint GetUnsignedInt(int index)
    {
        Visit("GetUnsignedInt(int)");
        Assert.Equal(3, index);
        throw Failure;
    }
    public override uint GetUnsignedIntLE(int index)
    {
        Visit("GetUnsignedIntLE(int)");
        Assert.Equal(3, index);
        throw Failure;
    }
    public override char GetChar(int index)
    {
        Visit("GetChar(int)");
        Assert.Equal(3, index);
        throw Failure;
    }
    public override ByteBuf SetChar(int index, char value)
    {
        Visit("SetChar(int,char)");
        Assert.Equal(3, index);
        Assert.Equal('\u00E9', value);
        throw Failure;
    }
    public override float GetFloat(int index)
    {
        Visit("GetFloat(int)");
        Assert.Equal(3, index);
        throw Failure;
    }
    public override float GetFloatLE(int index)
    {
        Visit("GetFloatLE(int)");
        Assert.Equal(3, index);
        throw Failure;
    }
    public override double GetDouble(int index)
    {
        Visit("GetDouble(int)");
        Assert.Equal(3, index);
        throw Failure;
    }
    public override double GetDoubleLE(int index)
    {
        Visit("GetDoubleLE(int)");
        Assert.Equal(3, index);
        throw Failure;
    }
    public override ByteBuf SetFloat(int index, float value)
    {
        Visit("SetFloat(int,float)");
        Assert.Equal(3, index);
        Assert.Equal(1.25f, value);
        throw Failure;
    }
    public override ByteBuf SetFloatLE(int index, float value)
    {
        Visit("SetFloatLE(int,float)");
        Assert.Equal(3, index);
        Assert.Equal(1.25f, value);
        throw Failure;
    }
    public override ByteBuf SetDouble(int index, double value)
    {
        Visit("SetDouble(int,double)");
        Assert.Equal(3, index);
        Assert.Equal(1.25d, value);
        throw Failure;
    }
    public override ByteBuf SetDoubleLE(int index, double value)
    {
        Visit("SetDoubleLE(int,double)");
        Assert.Equal(3, index);
        Assert.Equal(1.25d, value);
        throw Failure;
    }
    public override byte ReadByte()
    {
        Visit("ReadByte()");
        throw Failure;
    }
    public override ByteBuf WriteByte(int value)
    {
        Visit("WriteByte(int)");
        Assert.Equal(305419896, value);
        throw Failure;
    }
    public override bool ReadBoolean()
    {
        Visit("ReadBoolean()");
        throw Failure;
    }
    public override ByteBuf WriteBoolean(bool value)
    {
        Visit("WriteBoolean(bool)");
        Assert.True(value);
        throw Failure;
    }
    public override short ReadShort()
    {
        Visit("ReadShort()");
        throw Failure;
    }
    public override ByteBuf WriteShort(int value)
    {
        Visit("WriteShort(int)");
        Assert.Equal(305419896, value);
        throw Failure;
    }
    public override short ReadShortLE()
    {
        Visit("ReadShortLE()");
        throw Failure;
    }
    public override ByteBuf WriteShortLE(int value)
    {
        Visit("WriteShortLE(int)");
        Assert.Equal(305419896, value);
        throw Failure;
    }
    public override int ReadMedium()
    {
        Visit("ReadMedium()");
        throw Failure;
    }
    public override ByteBuf WriteMedium(int value)
    {
        Visit("WriteMedium(int)");
        Assert.Equal(305419896, value);
        throw Failure;
    }
    public override int ReadMediumLE()
    {
        Visit("ReadMediumLE()");
        throw Failure;
    }
    public override ByteBuf WriteMediumLE(int value)
    {
        Visit("WriteMediumLE(int)");
        Assert.Equal(305419896, value);
        throw Failure;
    }
    public override int ReadInt()
    {
        Visit("ReadInt()");
        throw Failure;
    }
    public override ByteBuf WriteInt(int value)
    {
        Visit("WriteInt(int)");
        Assert.Equal(305419896, value);
        throw Failure;
    }
    public override int ReadIntLE()
    {
        Visit("ReadIntLE()");
        throw Failure;
    }
    public override ByteBuf WriteIntLE(int value)
    {
        Visit("WriteIntLE(int)");
        Assert.Equal(305419896, value);
        throw Failure;
    }
    public override long ReadLong()
    {
        Visit("ReadLong()");
        throw Failure;
    }
    public override ByteBuf WriteLong(long value)
    {
        Visit("WriteLong(long)");
        Assert.Equal(81985529216486895L, value);
        throw Failure;
    }
    public override long ReadLongLE()
    {
        Visit("ReadLongLE()");
        throw Failure;
    }
    public override ByteBuf WriteLongLE(long value)
    {
        Visit("WriteLongLE(long)");
        Assert.Equal(81985529216486895L, value);
        throw Failure;
    }
    public override float ReadFloat()
    {
        Visit("ReadFloat()");
        throw Failure;
    }
    public override ByteBuf WriteFloat(float value)
    {
        Visit("WriteFloat(float)");
        Assert.Equal(1.25f, value);
        throw Failure;
    }
    public override float ReadFloatLE()
    {
        Visit("ReadFloatLE()");
        throw Failure;
    }
    public override ByteBuf WriteFloatLE(float value)
    {
        Visit("WriteFloatLE(float)");
        Assert.Equal(1.25f, value);
        throw Failure;
    }
    public override double ReadDouble()
    {
        Visit("ReadDouble()");
        throw Failure;
    }
    public override ByteBuf WriteDouble(double value)
    {
        Visit("WriteDouble(double)");
        Assert.Equal(1.25d, value);
        throw Failure;
    }
    public override double ReadDoubleLE()
    {
        Visit("ReadDoubleLE()");
        throw Failure;
    }
    public override ByteBuf WriteDoubleLE(double value)
    {
        Visit("WriteDoubleLE(double)");
        Assert.Equal(1.25d, value);
        throw Failure;
    }
    public override ushort ReadUnsignedShort()
    {
        Visit("ReadUnsignedShort()");
        throw Failure;
    }
    public override ushort ReadUnsignedShortLE()
    {
        Visit("ReadUnsignedShortLE()");
        throw Failure;
    }
    public override uint ReadUnsignedInt()
    {
        Visit("ReadUnsignedInt()");
        throw Failure;
    }
    public override uint ReadUnsignedIntLE()
    {
        Visit("ReadUnsignedIntLE()");
        throw Failure;
    }
    public override int ReadUnsignedMedium()
    {
        Visit("ReadUnsignedMedium()");
        throw Failure;
    }
    public override int ReadUnsignedMediumLE()
    {
        Visit("ReadUnsignedMediumLE()");
        throw Failure;
    }
    public override char ReadChar()
    {
        Visit("ReadChar()");
        throw Failure;
    }
    public override ByteBuf WriteChar(char value)
    {
        Visit("WriteChar(char)");
        Assert.Equal('\u00E9', value);
        throw Failure;
    }
    public override ByteBuf SkipBytes(int length)
    {
        Visit("SkipBytes(int)");
        Assert.Equal(4, length);
        throw Failure;
    }
    public override ByteBuf GetBytes(int index, Span<byte> destination)
    {
        Visit("GetBytes(int,Span<byte>)");
        Assert.Equal(3, index);
        Assert.Equal(4, destination.Length); Assert.True(destination.Overlaps(C.Bytes.AsSpan(2, 4)));
        throw Failure;
    }
    public override ByteBuf SetBytes(int index, ReadOnlySpan<byte> source)
    {
        Visit("SetBytes(int,ReadOnlySpan<byte>)");
        Assert.Equal(3, index);
        Assert.Equal(4, source.Length); Assert.True(source.Overlaps(C.Bytes.AsSpan(2, 4)));
        throw Failure;
    }
    public override ByteBuf ReadBytes(Span<byte> destination)
    {
        Visit("ReadBytes(Span<byte>)");
        Assert.Equal(4, destination.Length); Assert.True(destination.Overlaps(C.Bytes.AsSpan(2, 4)));
        throw Failure;
    }
    public override ByteBuf WriteBytes(ReadOnlySpan<byte> source)
    {
        Visit("WriteBytes(ReadOnlySpan<byte>)");
        Assert.Equal(4, source.Length); Assert.True(source.Overlaps(C.Bytes.AsSpan(2, 4)));
        throw Failure;
    }
    public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
    {
        Visit("GetBytes(int,ByteBuf,int,int)");
        Assert.Equal(3, index);
        Assert.Same(C.Peer, destination);
        Assert.Equal(5, destinationIndex);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
    {
        Visit("SetBytes(int,ByteBuf,int,int)");
        Assert.Equal(3, index);
        Assert.Same(C.Peer, source);
        Assert.Equal(2, sourceIndex);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override ByteBuf ReadBytes(ByteBuf destination, int length)
    {
        Visit("ReadBytes(ByteBuf,int)");
        Assert.Same(C.Peer, destination);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override ByteBuf WriteBytes(ByteBuf source, int length)
    {
        Visit("WriteBytes(ByteBuf,int)");
        Assert.Same(C.Peer, source);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override int GetBytes(int index, SafeFileHandle destination, long position, int length)
    {
        Visit("GetBytes(int,SafeFileHandle,long,int)");
        Assert.Equal(3, index);
        Assert.Same(C.File, destination);
        Assert.Equal(11L, position);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override int SetBytes(int index, SafeFileHandle source, long position, int length)
    {
        Visit("SetBytes(int,SafeFileHandle,long,int)");
        Assert.Equal(3, index);
        Assert.Same(C.File, source);
        Assert.Equal(11L, position);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override int ReadBytes(SafeFileHandle destination, long position, int length)
    {
        Visit("ReadBytes(SafeFileHandle,long,int)");
        Assert.Same(C.File, destination);
        Assert.Equal(11L, position);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override int WriteBytes(SafeFileHandle source, long position, int length)
    {
        Visit("WriteBytes(SafeFileHandle,long,int)");
        Assert.Same(C.File, source);
        Assert.Equal(11L, position);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override ValueTask<int> GetBytesAsync(int index, SafeFileHandle destination, long position, int length,
        CancellationToken cancellationToken = default)
    {
        Visit("GetBytesAsync(int,SafeFileHandle,long,int,CancellationToken)");
        Assert.Equal(3, index);
        Assert.Same(C.File, destination);
        Assert.Equal(11L, position);
        Assert.Equal(4, length);
        Assert.Equal(C.Cancellation.Token, cancellationToken);
        throw Failure;
    }
    public override ValueTask<int> SetBytesAsync(int index, SafeFileHandle source, long position, int length,
        CancellationToken cancellationToken = default)
    {
        Visit("SetBytesAsync(int,SafeFileHandle,long,int,CancellationToken)");
        Assert.Equal(3, index);
        Assert.Same(C.File, source);
        Assert.Equal(11L, position);
        Assert.Equal(4, length);
        Assert.Equal(C.Cancellation.Token, cancellationToken);
        throw Failure;
    }
    public override ValueTask<int> ReadBytesAsync(SafeFileHandle destination, long position, int length,
        CancellationToken cancellationToken = default)
    {
        Visit("ReadBytesAsync(SafeFileHandle,long,int,CancellationToken)");
        Assert.Same(C.File, destination);
        Assert.Equal(11L, position);
        Assert.Equal(4, length);
        Assert.Equal(C.Cancellation.Token, cancellationToken);
        throw Failure;
    }
    public override ValueTask<int> WriteBytesAsync(SafeFileHandle source, long position, int length,
        CancellationToken cancellationToken = default)
    {
        Visit("WriteBytesAsync(SafeFileHandle,long,int,CancellationToken)");
        Assert.Same(C.File, source);
        Assert.Equal(11L, position);
        Assert.Equal(4, length);
        Assert.Equal(C.Cancellation.Token, cancellationToken);
        throw Failure;
    }
    public override ReadOnlyMemory<byte> AsReadOnlyMemory(int index, int length)
    {
        Visit("AsReadOnlyMemory(int,int)");
        Assert.Equal(3, index);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override ReadOnlySpan<byte> AsReadOnlySpan(int index, int length)
    {
        Visit("AsReadOnlySpan(int,int)");
        Assert.Equal(3, index);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override ReadOnlySequence<byte> AsReadOnlySequence(int index, int length)
    {
        Visit("AsReadOnlySequence(int,int)");
        Assert.Equal(3, index);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override int GetBytes(int index, Socket destination, int length)
    {
        Visit("GetBytes(int,Socket,int)");
        Assert.Equal(3, index);
        Assert.Same(C.Socket, destination);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override int SetBytes(int index, Socket source, int length)
    {
        Visit("SetBytes(int,Socket,int)");
        Assert.Equal(3, index);
        Assert.Same(C.Socket, source);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override int ReadBytes(Socket destination, int length)
    {
        Visit("ReadBytes(Socket,int)");
        Assert.Same(C.Socket, destination);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override int WriteBytes(Socket source, int length)
    {
        Visit("WriteBytes(Socket,int)");
        Assert.Same(C.Socket, source);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override ValueTask<int> GetBytesAsync(int index, Socket destination, int length,
        CancellationToken cancellationToken = default)
    {
        Visit("GetBytesAsync(int,Socket,int,CancellationToken)");
        Assert.Equal(3, index);
        Assert.Same(C.Socket, destination);
        Assert.Equal(4, length);
        Assert.Equal(C.Cancellation.Token, cancellationToken);
        throw Failure;
    }
    public override ValueTask<int> SetBytesAsync(int index, Socket source, int length,
        CancellationToken cancellationToken = default)
    {
        Visit("SetBytesAsync(int,Socket,int,CancellationToken)");
        Assert.Equal(3, index);
        Assert.Same(C.Socket, source);
        Assert.Equal(4, length);
        Assert.Equal(C.Cancellation.Token, cancellationToken);
        throw Failure;
    }
    public override ValueTask<int> ReadBytesAsync(Socket destination, int length,
        CancellationToken cancellationToken = default)
    {
        Visit("ReadBytesAsync(Socket,int,CancellationToken)");
        Assert.Same(C.Socket, destination);
        Assert.Equal(4, length);
        Assert.Equal(C.Cancellation.Token, cancellationToken);
        throw Failure;
    }
    public override ValueTask<int> WriteBytesAsync(Socket source, int length,
        CancellationToken cancellationToken = default)
    {
        Visit("WriteBytesAsync(Socket,int,CancellationToken)");
        Assert.Same(C.Socket, source);
        Assert.Equal(4, length);
        Assert.Equal(C.Cancellation.Token, cancellationToken);
        throw Failure;
    }
    public override ByteBuf GetBytes(int index, Stream destination, int length)
    {
        Visit("GetBytes(int,Stream,int)");
        Assert.Equal(3, index);
        Assert.Same(C.Stream, destination);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override int SetBytes(int index, Stream source, int length)
    {
        Visit("SetBytes(int,Stream,int)");
        Assert.Equal(3, index);
        Assert.Same(C.Stream, source);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override ByteBuf ReadBytes(Stream destination, int length)
    {
        Visit("ReadBytes(Stream,int)");
        Assert.Same(C.Stream, destination);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override int WriteBytes(Stream source, int length)
    {
        Visit("WriteBytes(Stream,int)");
        Assert.Same(C.Stream, source);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override ValueTask GetBytesAsync(int index, Stream destination, int length,
        CancellationToken cancellationToken = default)
    {
        Visit("GetBytesAsync(int,Stream,int,CancellationToken)");
        Assert.Equal(3, index);
        Assert.Same(C.Stream, destination);
        Assert.Equal(4, length);
        Assert.Equal(C.Cancellation.Token, cancellationToken);
        throw Failure;
    }
    public override ValueTask<int> SetBytesAsync(int index, Stream source, int length,
        CancellationToken cancellationToken = default)
    {
        Visit("SetBytesAsync(int,Stream,int,CancellationToken)");
        Assert.Equal(3, index);
        Assert.Same(C.Stream, source);
        Assert.Equal(4, length);
        Assert.Equal(C.Cancellation.Token, cancellationToken);
        throw Failure;
    }
    public override ValueTask ReadBytesAsync(Stream destination, int length,
        CancellationToken cancellationToken = default)
    {
        Visit("ReadBytesAsync(Stream,int,CancellationToken)");
        Assert.Same(C.Stream, destination);
        Assert.Equal(4, length);
        Assert.Equal(C.Cancellation.Token, cancellationToken);
        throw Failure;
    }
    public override ValueTask<int> WriteBytesAsync(Stream source, int length,
        CancellationToken cancellationToken = default)
    {
        Visit("WriteBytesAsync(Stream,int,CancellationToken)");
        Assert.Same(C.Stream, source);
        Assert.Equal(4, length);
        Assert.Equal(C.Cancellation.Token, cancellationToken);
        throw Failure;
    }
    public override ByteBuf GetBytes(int index, ByteBuf destination)
    {
        Visit("GetBytes(int,ByteBuf)");
        Assert.Equal(3, index);
        Assert.Same(C.Peer, destination);
        throw Failure;
    }
    public override ByteBuf GetBytes(int index, ByteBuf destination, int length)
    {
        Visit("GetBytes(int,ByteBuf,int)");
        Assert.Equal(3, index);
        Assert.Same(C.Peer, destination);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override ByteBuf SetBytes(int index, ByteBuf source)
    {
        Visit("SetBytes(int,ByteBuf)");
        Assert.Equal(3, index);
        Assert.Same(C.Peer, source);
        throw Failure;
    }
    public override ByteBuf SetBytes(int index, ByteBuf source, int length)
    {
        Visit("SetBytes(int,ByteBuf,int)");
        Assert.Equal(3, index);
        Assert.Same(C.Peer, source);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override ByteBuf ReadBytes(ByteBuf destination)
    {
        Visit("ReadBytes(ByteBuf)");
        Assert.Same(C.Peer, destination);
        throw Failure;
    }
    public override ByteBuf ReadBytes(ByteBuf destination, int destinationIndex, int length)
    {
        Visit("ReadBytes(ByteBuf,int,int)");
        Assert.Same(C.Peer, destination);
        Assert.Equal(5, destinationIndex);
        Assert.Equal(4, length);
        throw Failure;
    }
    public override ByteBuf WriteBytes(ByteBuf source)
    {
        Visit("WriteBytes(ByteBuf)");
        Assert.Same(C.Peer, source);
        throw Failure;
    }
    public override ByteBuf WriteBytes(ByteBuf source, int sourceIndex, int length)
    {
        Visit("WriteBytes(ByteBuf,int,int)");
        Assert.Same(C.Peer, source);
        Assert.Equal(2, sourceIndex);
        Assert.Equal(4, length);
        throw Failure;
    }
    }
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool direct in new[] { false, true })
        for (int form = 0; form < 10; ++form)
        for (int operation = 0; operation < 118; ++operation) yield return [direct, form, operation];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void PublicOperationsPreserveWrappedEntryArgumentsAndFailureWithoutChangingIndices(bool direct, int form, int operation)
    {
        using var c = new Context(direct, form);
        c.Root.MarkReaderIndex().MarkWriterIndex();
        Assert.Same(c.Root.Failure, Assert.Throws<DispatchFailure>(() => Request(c, operation)));
        string method = Assert.Single(c.Root.Calls);
        Assert.Equal(Signatures[operation], method);
        Assert.Equal((3, 40, 1), (c.Root.ReaderIndex, c.Root.WriterIndex, c.Root.ReferenceCount));
        c.Root.SetIndex(4, 41).ResetWriterIndex().ResetReaderIndex();
        Assert.Equal((3, 40), (c.Root.ReaderIndex, c.Root.WriterIndex));
        Assert.Equal(form is 4 or 7 or 8 ? 1 : 0, c.Tracker.Records); Assert.Equal(0, c.Tracker.Closes);
        c.Dispose(); Assert.Equal(form is 1 or 3 or 4 or 7 or 8 ? 1 : 0, c.Tracker.Closes);
    }
    private static readonly string[] Signatures =
    [
        "AsMemory(int,int)",
        "AsSpan(int,int)",
        "GetByte(int)",
        "GetBoolean(int)",
        "SetByte(int,int)",
        "SetBoolean(int,bool)",
        "GetShort(int)",
        "SetShort(int,int)",
        "GetShortLE(int)",
        "SetShortLE(int,int)",
        "GetInt(int)",
        "SetInt(int,int)",
        "GetIntLE(int)",
        "SetIntLE(int,int)",
        "GetLong(int)",
        "SetLong(int,long)",
        "GetLongLE(int)",
        "SetLongLE(int,long)",
        "GetUnsignedMedium(int)",
        "GetUnsignedMediumLE(int)",
        "GetMedium(int)",
        "GetMediumLE(int)",
        "SetMedium(int,int)",
        "SetMediumLE(int,int)",
        "GetUnsignedShort(int)",
        "GetUnsignedShortLE(int)",
        "GetUnsignedInt(int)",
        "GetUnsignedIntLE(int)",
        "GetChar(int)",
        "SetChar(int,char)",
        "GetFloat(int)",
        "GetFloatLE(int)",
        "GetDouble(int)",
        "GetDoubleLE(int)",
        "SetFloat(int,float)",
        "SetFloatLE(int,float)",
        "SetDouble(int,double)",
        "SetDoubleLE(int,double)",
        "ReadByte()",
        "WriteByte(int)",
        "ReadBoolean()",
        "WriteBoolean(bool)",
        "ReadShort()",
        "WriteShort(int)",
        "ReadShortLE()",
        "WriteShortLE(int)",
        "ReadMedium()",
        "WriteMedium(int)",
        "ReadMediumLE()",
        "WriteMediumLE(int)",
        "ReadInt()",
        "WriteInt(int)",
        "ReadIntLE()",
        "WriteIntLE(int)",
        "ReadLong()",
        "WriteLong(long)",
        "ReadLongLE()",
        "WriteLongLE(long)",
        "ReadFloat()",
        "WriteFloat(float)",
        "ReadFloatLE()",
        "WriteFloatLE(float)",
        "ReadDouble()",
        "WriteDouble(double)",
        "ReadDoubleLE()",
        "WriteDoubleLE(double)",
        "ReadUnsignedShort()",
        "ReadUnsignedShortLE()",
        "ReadUnsignedInt()",
        "ReadUnsignedIntLE()",
        "ReadUnsignedMedium()",
        "ReadUnsignedMediumLE()",
        "ReadChar()",
        "WriteChar(char)",
        "SkipBytes(int)",
        "GetBytes(int,Span<byte>)",
        "SetBytes(int,ReadOnlySpan<byte>)",
        "ReadBytes(Span<byte>)",
        "WriteBytes(ReadOnlySpan<byte>)",
        "GetBytes(int,ByteBuf,int,int)",
        "SetBytes(int,ByteBuf,int,int)",
        "ReadBytes(ByteBuf,int)",
        "WriteBytes(ByteBuf,int)",
        "GetBytes(int,SafeFileHandle,long,int)",
        "SetBytes(int,SafeFileHandle,long,int)",
        "ReadBytes(SafeFileHandle,long,int)",
        "WriteBytes(SafeFileHandle,long,int)",
        "GetBytesAsync(int,SafeFileHandle,long,int,CancellationToken)",
        "SetBytesAsync(int,SafeFileHandle,long,int,CancellationToken)",
        "ReadBytesAsync(SafeFileHandle,long,int,CancellationToken)",
        "WriteBytesAsync(SafeFileHandle,long,int,CancellationToken)",
        "AsReadOnlyMemory(int,int)",
        "AsReadOnlySpan(int,int)",
        "AsReadOnlySequence(int,int)",
        "GetBytes(int,Socket,int)",
        "SetBytes(int,Socket,int)",
        "ReadBytes(Socket,int)",
        "WriteBytes(Socket,int)",
        "GetBytesAsync(int,Socket,int,CancellationToken)",
        "SetBytesAsync(int,Socket,int,CancellationToken)",
        "ReadBytesAsync(Socket,int,CancellationToken)",
        "WriteBytesAsync(Socket,int,CancellationToken)",
        "GetBytes(int,Stream,int)",
        "SetBytes(int,Stream,int)",
        "ReadBytes(Stream,int)",
        "WriteBytes(Stream,int)",
        "GetBytesAsync(int,Stream,int,CancellationToken)",
        "SetBytesAsync(int,Stream,int,CancellationToken)",
        "ReadBytesAsync(Stream,int,CancellationToken)",
        "WriteBytesAsync(Stream,int,CancellationToken)",
        "GetBytes(int,ByteBuf)",
        "GetBytes(int,ByteBuf,int)",
        "SetBytes(int,ByteBuf)",
        "SetBytes(int,ByteBuf,int)",
        "ReadBytes(ByteBuf)",
        "ReadBytes(ByteBuf,int,int)",
        "WriteBytes(ByteBuf)",
        "WriteBytes(ByteBuf,int,int)",
    ];
    private static void Request(Context c, int operation)
    {
        switch (operation)
        {
            case 0: c.Buffer.AsMemory(3, 4); break;
            case 1: c.Buffer.AsSpan(3, 4); break;
            case 2: c.Buffer.GetByte(3); break;
            case 3: c.Buffer.GetBoolean(3); break;
            case 4: c.Buffer.SetByte(3, 305419896); break;
            case 5: c.Buffer.SetBoolean(3, true); break;
            case 6: c.Buffer.GetShort(3); break;
            case 7: c.Buffer.SetShort(3, 305419896); break;
            case 8: c.Buffer.GetShortLE(3); break;
            case 9: c.Buffer.SetShortLE(3, 305419896); break;
            case 10: c.Buffer.GetInt(3); break;
            case 11: c.Buffer.SetInt(3, 305419896); break;
            case 12: c.Buffer.GetIntLE(3); break;
            case 13: c.Buffer.SetIntLE(3, 305419896); break;
            case 14: c.Buffer.GetLong(3); break;
            case 15: c.Buffer.SetLong(3, 81985529216486895L); break;
            case 16: c.Buffer.GetLongLE(3); break;
            case 17: c.Buffer.SetLongLE(3, 81985529216486895L); break;
            case 18: c.Buffer.GetUnsignedMedium(3); break;
            case 19: c.Buffer.GetUnsignedMediumLE(3); break;
            case 20: c.Buffer.GetMedium(3); break;
            case 21: c.Buffer.GetMediumLE(3); break;
            case 22: c.Buffer.SetMedium(3, 305419896); break;
            case 23: c.Buffer.SetMediumLE(3, 305419896); break;
            case 24: c.Buffer.GetUnsignedShort(3); break;
            case 25: c.Buffer.GetUnsignedShortLE(3); break;
            case 26: c.Buffer.GetUnsignedInt(3); break;
            case 27: c.Buffer.GetUnsignedIntLE(3); break;
            case 28: c.Buffer.GetChar(3); break;
            case 29: c.Buffer.SetChar(3, '\u00E9'); break;
            case 30: c.Buffer.GetFloat(3); break;
            case 31: c.Buffer.GetFloatLE(3); break;
            case 32: c.Buffer.GetDouble(3); break;
            case 33: c.Buffer.GetDoubleLE(3); break;
            case 34: c.Buffer.SetFloat(3, 1.25f); break;
            case 35: c.Buffer.SetFloatLE(3, 1.25f); break;
            case 36: c.Buffer.SetDouble(3, 1.25d); break;
            case 37: c.Buffer.SetDoubleLE(3, 1.25d); break;
            case 38: c.Buffer.ReadByte(); break;
            case 39: c.Buffer.WriteByte(305419896); break;
            case 40: c.Buffer.ReadBoolean(); break;
            case 41: c.Buffer.WriteBoolean(true); break;
            case 42: c.Buffer.ReadShort(); break;
            case 43: c.Buffer.WriteShort(305419896); break;
            case 44: c.Buffer.ReadShortLE(); break;
            case 45: c.Buffer.WriteShortLE(305419896); break;
            case 46: c.Buffer.ReadMedium(); break;
            case 47: c.Buffer.WriteMedium(305419896); break;
            case 48: c.Buffer.ReadMediumLE(); break;
            case 49: c.Buffer.WriteMediumLE(305419896); break;
            case 50: c.Buffer.ReadInt(); break;
            case 51: c.Buffer.WriteInt(305419896); break;
            case 52: c.Buffer.ReadIntLE(); break;
            case 53: c.Buffer.WriteIntLE(305419896); break;
            case 54: c.Buffer.ReadLong(); break;
            case 55: c.Buffer.WriteLong(81985529216486895L); break;
            case 56: c.Buffer.ReadLongLE(); break;
            case 57: c.Buffer.WriteLongLE(81985529216486895L); break;
            case 58: c.Buffer.ReadFloat(); break;
            case 59: c.Buffer.WriteFloat(1.25f); break;
            case 60: c.Buffer.ReadFloatLE(); break;
            case 61: c.Buffer.WriteFloatLE(1.25f); break;
            case 62: c.Buffer.ReadDouble(); break;
            case 63: c.Buffer.WriteDouble(1.25d); break;
            case 64: c.Buffer.ReadDoubleLE(); break;
            case 65: c.Buffer.WriteDoubleLE(1.25d); break;
            case 66: c.Buffer.ReadUnsignedShort(); break;
            case 67: c.Buffer.ReadUnsignedShortLE(); break;
            case 68: c.Buffer.ReadUnsignedInt(); break;
            case 69: c.Buffer.ReadUnsignedIntLE(); break;
            case 70: c.Buffer.ReadUnsignedMedium(); break;
            case 71: c.Buffer.ReadUnsignedMediumLE(); break;
            case 72: c.Buffer.ReadChar(); break;
            case 73: c.Buffer.WriteChar('\u00E9'); break;
            case 74: c.Buffer.SkipBytes(4); break;
            case 75: c.Buffer.GetBytes(3, c.Bytes.AsSpan(2, 4)); break;
            case 76: c.Buffer.SetBytes(3, c.Bytes.AsSpan(2, 4)); break;
            case 77: c.Buffer.ReadBytes(c.Bytes.AsSpan(2, 4)); break;
            case 78: c.Buffer.WriteBytes(c.Bytes.AsSpan(2, 4)); break;
            case 79: c.Buffer.GetBytes(3, c.Peer, 5, 4); break;
            case 80: c.Buffer.SetBytes(3, c.Peer, 2, 4); break;
            case 81: c.Buffer.ReadBytes(c.Peer, 4); break;
            case 82: c.Buffer.WriteBytes(c.Peer, 4); break;
            case 83: c.Buffer.GetBytes(3, c.File, 11L, 4); break;
            case 84: c.Buffer.SetBytes(3, c.File, 11L, 4); break;
            case 85: c.Buffer.ReadBytes(c.File, 11L, 4); break;
            case 86: c.Buffer.WriteBytes(c.File, 11L, 4); break;
            case 87: c.Buffer.GetBytesAsync(3, c.File, 11L, 4, c.Cancellation.Token).GetAwaiter().GetResult(); break;
            case 88: c.Buffer.SetBytesAsync(3, c.File, 11L, 4, c.Cancellation.Token).GetAwaiter().GetResult(); break;
            case 89: c.Buffer.ReadBytesAsync(c.File, 11L, 4, c.Cancellation.Token).GetAwaiter().GetResult(); break;
            case 90: c.Buffer.WriteBytesAsync(c.File, 11L, 4, c.Cancellation.Token).GetAwaiter().GetResult(); break;
            case 91: c.Buffer.AsReadOnlyMemory(3, 4); break;
            case 92: c.Buffer.AsReadOnlySpan(3, 4); break;
            case 93: c.Buffer.AsReadOnlySequence(3, 4); break;
            case 94: c.Buffer.GetBytes(3, c.Socket, 4); break;
            case 95: c.Buffer.SetBytes(3, c.Socket, 4); break;
            case 96: c.Buffer.ReadBytes(c.Socket, 4); break;
            case 97: c.Buffer.WriteBytes(c.Socket, 4); break;
            case 98: c.Buffer.GetBytesAsync(3, c.Socket, 4, c.Cancellation.Token).GetAwaiter().GetResult(); break;
            case 99: c.Buffer.SetBytesAsync(3, c.Socket, 4, c.Cancellation.Token).GetAwaiter().GetResult(); break;
            case 100: c.Buffer.ReadBytesAsync(c.Socket, 4, c.Cancellation.Token).GetAwaiter().GetResult(); break;
            case 101: c.Buffer.WriteBytesAsync(c.Socket, 4, c.Cancellation.Token).GetAwaiter().GetResult(); break;
            case 102: c.Buffer.GetBytes(3, c.Stream, 4); break;
            case 103: c.Buffer.SetBytes(3, c.Stream, 4); break;
            case 104: c.Buffer.ReadBytes(c.Stream, 4); break;
            case 105: c.Buffer.WriteBytes(c.Stream, 4); break;
            case 106: c.Buffer.GetBytesAsync(3, c.Stream, 4, c.Cancellation.Token).GetAwaiter().GetResult(); break;
            case 107: c.Buffer.SetBytesAsync(3, c.Stream, 4, c.Cancellation.Token).GetAwaiter().GetResult(); break;
            case 108: c.Buffer.ReadBytesAsync(c.Stream, 4, c.Cancellation.Token).GetAwaiter().GetResult(); break;
            case 109: c.Buffer.WriteBytesAsync(c.Stream, 4, c.Cancellation.Token).GetAwaiter().GetResult(); break;
            case 110: c.Buffer.GetBytes(3, c.Peer); break;
            case 111: c.Buffer.GetBytes(3, c.Peer, 4); break;
            case 112: c.Buffer.SetBytes(3, c.Peer); break;
            case 113: c.Buffer.SetBytes(3, c.Peer, 4); break;
            case 114: c.Buffer.ReadBytes(c.Peer); break;
            case 115: c.Buffer.ReadBytes(c.Peer, 5, 4); break;
            case 116: c.Buffer.WriteBytes(c.Peer); break;
            case 117: c.Buffer.WriteBytes(c.Peer, 2, 4); break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }
}
