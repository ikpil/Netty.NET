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
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Buffer;

// CLR: operation forwarding records once before delegating, including failed accesses.
// Shared views use SimpleLeakAwareByteBuf's tracker/close diagnostics. Span/Memory/
// sequence and async I/O replace Java NIO/channel surfaces; no byte-order facade is added.
internal sealed class AdvancedLeakAwareByteBuf : SimpleLeakAwareByteBuf
{
    // If set to true we will only record stacktraces for touch(...), release(...) and retain(...) calls.
    private const string AcquireAndReleaseOnlyProperty = "io.netty.leakDetection.acquireAndReleaseOnly";
    private static readonly bool AcquireAndReleaseOnly;

    static AdvancedLeakAwareByteBuf()
    {
        AcquireAndReleaseOnly = SystemPropertyUtil.GetBoolean(AcquireAndReleaseOnlyProperty, false);
        IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(AdvancedLeakAwareByteBuf));
        if (logger.IsDebugEnabled()) logger.Debug("-D{}: {}", AcquireAndReleaseOnlyProperty, AcquireAndReleaseOnly);
        ResourceLeakDetector.AddExclusions(typeof(AdvancedLeakAwareByteBuf), nameof(Touch), nameof(RecordLeakNonRefCountingOperation));
    }

    internal AdvancedLeakAwareByteBuf(ByteBuf wrapped, IResourceLeakTracker<ByteBuf> leak) : base(wrapped, leak) { }
    internal AdvancedLeakAwareByteBuf(ByteBuf wrapped, ByteBuf trackedByteBuf, IResourceLeakTracker<ByteBuf> leak)
        : base(wrapped, trackedByteBuf, leak) { }

    internal static void RecordLeakNonRefCountingOperation(IResourceLeakTracker<ByteBuf> leak)
    {
        if (!AcquireAndReleaseOnly) leak.Record();
    }

    public override int Capacity
    {
        get => Buffer.Capacity;
        set { RecordLeakNonRefCountingOperation(_leak); Buffer.Capacity = value; }
    }

    public override ByteBuf Retain(int increment = 1) { _leak.Record(); return base.Retain(increment); }
    public override bool Release(int decrement = 1) { _leak.Record(); return base.Release(decrement); }
    public override ByteBuf Touch(object hint = null)
    {
        // CLR's optional hint combines Java's two overloads; null has the same no-hint record semantics.
        if (hint == null) _leak.Record(); else _leak.Record(hint);
        return this;
    }

    protected override SimpleLeakAwareByteBuf NewLeakAwareByteBuf(ByteBuf buffer, ByteBuf trackedByteBuf,
        IResourceLeakTracker<ByteBuf> leakTracker) => new AdvancedLeakAwareByteBuf(buffer, trackedByteBuf, leakTracker);

    public override ByteBuf DiscardReadBytes()
    { RecordLeakNonRefCountingOperation(_leak); Buffer.DiscardReadBytes(); return this; }

    public override ByteBuf DiscardSomeReadBytes()
    { RecordLeakNonRefCountingOperation(_leak); Buffer.DiscardSomeReadBytes(); return this; }

    public override ByteBuf EnsureWritable(int minimumWritableBytes)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.EnsureWritable(minimumWritableBytes); return this; }

    public override int EnsureWritable(int minimumWritableBytes, bool force)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.EnsureWritable(minimumWritableBytes, force); }

    public override Memory<byte> AsMemory(int index, int length)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.AsMemory(index, length); }

    public override Span<byte> AsSpan(int index, int length)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.AsSpan(index, length); }

    public override byte GetByte(int index)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetByte(index); }

    public override bool GetBoolean(int index)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetBoolean(index); }

    public override ByteBuf SetByte(int index, int value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.SetByte(index, value); return this; }

    public override ByteBuf SetBoolean(int index, bool value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.SetBoolean(index, value); return this; }

    public override short GetShort(int index)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetShort(index); }

    public override ByteBuf SetShort(int index, int value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.SetShort(index, value); return this; }

    public override short GetShortLE(int index)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetShortLE(index); }

    public override ByteBuf SetShortLE(int index, int value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.SetShortLE(index, value); return this; }

    public override int GetInt(int index)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetInt(index); }

    public override ByteBuf SetInt(int index, int value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.SetInt(index, value); return this; }

    public override int GetIntLE(int index)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetIntLE(index); }

    public override ByteBuf SetIntLE(int index, int value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.SetIntLE(index, value); return this; }

    public override long GetLong(int index)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetLong(index); }

    public override ByteBuf SetLong(int index, long value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.SetLong(index, value); return this; }

    public override long GetLongLE(int index)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetLongLE(index); }

    public override ByteBuf SetLongLE(int index, long value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.SetLongLE(index, value); return this; }

    public override int GetUnsignedMedium(int index)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetUnsignedMedium(index); }

    public override int GetUnsignedMediumLE(int index)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetUnsignedMediumLE(index); }

    public override int GetMedium(int index)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetMedium(index); }

    public override int GetMediumLE(int index)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetMediumLE(index); }

    public override ByteBuf SetMedium(int index, int value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.SetMedium(index, value); return this; }

    public override ByteBuf SetMediumLE(int index, int value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.SetMediumLE(index, value); return this; }

    public override ushort GetUnsignedShort(int index)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetUnsignedShort(index); }

    public override ushort GetUnsignedShortLE(int index)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetUnsignedShortLE(index); }

    public override uint GetUnsignedInt(int index)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetUnsignedInt(index); }

    public override uint GetUnsignedIntLE(int index)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetUnsignedIntLE(index); }

    public override char GetChar(int index)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetChar(index); }

    public override ByteBuf SetChar(int index, char value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.SetChar(index, value); return this; }

    public override float GetFloat(int index)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetFloat(index); }

    public override float GetFloatLE(int index)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetFloatLE(index); }

    public override double GetDouble(int index)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetDouble(index); }

    public override double GetDoubleLE(int index)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetDoubleLE(index); }

    public override ByteBuf SetFloat(int index, float value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.SetFloat(index, value); return this; }

    public override ByteBuf SetFloatLE(int index, float value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.SetFloatLE(index, value); return this; }

    public override ByteBuf SetDouble(int index, double value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.SetDouble(index, value); return this; }

    public override ByteBuf SetDoubleLE(int index, double value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.SetDoubleLE(index, value); return this; }

    public override byte ReadByte()
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadByte(); }

    public override ByteBuf WriteByte(int value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.WriteByte(value); return this; }

    public override bool ReadBoolean()
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadBoolean(); }

    public override ByteBuf WriteBoolean(bool value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.WriteBoolean(value); return this; }

    public override short ReadShort()
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadShort(); }

    public override ByteBuf WriteShort(int value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.WriteShort(value); return this; }

    public override short ReadShortLE()
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadShortLE(); }

    public override ByteBuf WriteShortLE(int value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.WriteShortLE(value); return this; }

    public override int ReadMedium()
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadMedium(); }

    public override ByteBuf WriteMedium(int value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.WriteMedium(value); return this; }

    public override int ReadMediumLE()
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadMediumLE(); }

    public override ByteBuf WriteMediumLE(int value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.WriteMediumLE(value); return this; }

    public override int ReadInt()
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadInt(); }

    public override ByteBuf WriteInt(int value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.WriteInt(value); return this; }

    public override int ReadIntLE()
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadIntLE(); }

    public override ByteBuf WriteIntLE(int value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.WriteIntLE(value); return this; }

    public override long ReadLong()
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadLong(); }

    public override ByteBuf WriteLong(long value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.WriteLong(value); return this; }

    public override long ReadLongLE()
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadLongLE(); }

    public override ByteBuf WriteLongLE(long value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.WriteLongLE(value); return this; }

    public override float ReadFloat()
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadFloat(); }

    public override ByteBuf WriteFloat(float value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.WriteFloat(value); return this; }

    public override float ReadFloatLE()
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadFloatLE(); }

    public override ByteBuf WriteFloatLE(float value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.WriteFloatLE(value); return this; }

    public override double ReadDouble()
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadDouble(); }

    public override ByteBuf WriteDouble(double value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.WriteDouble(value); return this; }

    public override double ReadDoubleLE()
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadDoubleLE(); }

    public override ByteBuf WriteDoubleLE(double value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.WriteDoubleLE(value); return this; }

    public override ushort ReadUnsignedShort()
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadUnsignedShort(); }

    public override ushort ReadUnsignedShortLE()
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadUnsignedShortLE(); }

    public override uint ReadUnsignedInt()
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadUnsignedInt(); }

    public override uint ReadUnsignedIntLE()
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadUnsignedIntLE(); }

    public override int ReadUnsignedMedium()
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadUnsignedMedium(); }

    public override int ReadUnsignedMediumLE()
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadUnsignedMediumLE(); }

    public override char ReadChar()
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadChar(); }

    public override ByteBuf WriteChar(char value)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.WriteChar(value); return this; }

    public override ByteBuf SkipBytes(int length)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.SkipBytes(length); return this; }

    public override ByteBuf GetBytes(int index, Span<byte> destination)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.GetBytes(index, destination); return this; }

    public override ByteBuf SetBytes(int index, ReadOnlySpan<byte> source)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.SetBytes(index, source); return this; }
    protected internal override void SetBytesFromIoCore(int index, ReadOnlySpan<byte> source)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.SetBytesFromIoCore(index, source); }

    public override ByteBuf ReadBytes(Span<byte> destination)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.ReadBytes(destination); return this; }

    public override ByteBuf WriteBytes(ReadOnlySpan<byte> source)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.WriteBytes(source); return this; }

    public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.GetBytes(index, destination, destinationIndex, length); return this; }

    public override ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.SetBytes(index, source, sourceIndex, length); return this; }

    public override ByteBuf ReadBytes(ByteBuf destination, int length)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.ReadBytes(destination, length); return this; }

    public override ByteBuf WriteBytes(ByteBuf source, int length)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.WriteBytes(source, length); return this; }

    public override ByteBuf SetZero(int index, int length)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.SetZero(index, length); return this; }

    public override ByteBuf WriteZero(int length)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.WriteZero(length); return this; }

    public override ByteBuf Copy()
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.Copy(); }

    public override ByteBuf Copy(int index, int length)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.Copy(index, length); }

    public override ByteBuf ReadBytes(int length)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadBytes(length); }

    public override ByteBuf Slice()
    { RecordLeakNonRefCountingOperation(_leak); return base.Slice(); }

    public override ByteBuf Slice(int index, int length)
    { RecordLeakNonRefCountingOperation(_leak); return base.Slice(index, length); }

    public override ByteBuf RetainedSlice()
    { RecordLeakNonRefCountingOperation(_leak); return base.RetainedSlice(); }

    public override ByteBuf RetainedSlice(int index, int length)
    { RecordLeakNonRefCountingOperation(_leak); return base.RetainedSlice(index, length); }

    public override ByteBuf Duplicate()
    { RecordLeakNonRefCountingOperation(_leak); return base.Duplicate(); }

    public override ByteBuf RetainedDuplicate()
    { RecordLeakNonRefCountingOperation(_leak); return base.RetainedDuplicate(); }

    public override ByteBuf ReadSlice(int length)
    { RecordLeakNonRefCountingOperation(_leak); return base.ReadSlice(length); }

    public override ByteBuf ReadRetainedSlice(int length)
    { RecordLeakNonRefCountingOperation(_leak); return base.ReadRetainedSlice(length); }

    public override int GetBytes(int index, Socket destination, int length)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetBytes(index, destination, length); }

    public override ValueTask<int> GetBytesAsync(int index, Socket destination, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetBytesAsync(index, destination, length, cancellationToken); }

    public override int SetBytes(int index, Socket source, int length)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.SetBytes(index, source, length); }

    public override ValueTask<int> SetBytesAsync(int index, Socket source, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.SetBytesAsync(index, source, length, cancellationToken); }

    public override int ReadBytes(Socket destination, int length)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadBytes(destination, length); }

    public override ValueTask<int> ReadBytesAsync(Socket destination, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadBytesAsync(destination, length, cancellationToken); }

    public override int WriteBytes(Socket source, int length)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.WriteBytes(source, length); }

    public override ValueTask<int> WriteBytesAsync(Socket source, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.WriteBytesAsync(source, length, cancellationToken); }

    public override int GetBytes(int index, SafeFileHandle destination, long position, int length)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetBytes(index, destination, position, length); }

    public override int SetBytes(int index, SafeFileHandle source, long position, int length)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.SetBytes(index, source, position, length); }

    public override int ReadBytes(SafeFileHandle destination, long position, int length)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadBytes(destination, position, length); }

    public override int WriteBytes(SafeFileHandle source, long position, int length)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.WriteBytes(source, position, length); }

    public override ValueTask<int> GetBytesAsync(int index, SafeFileHandle destination, long position, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetBytesAsync(index, destination, position, length, cancellationToken); }

    public override ValueTask<int> SetBytesAsync(int index, SafeFileHandle source, long position, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.SetBytesAsync(index, source, position, length, cancellationToken); }

    public override ValueTask<int> ReadBytesAsync(SafeFileHandle destination, long position, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadBytesAsync(destination, position, length, cancellationToken); }

    public override ValueTask<int> WriteBytesAsync(SafeFileHandle source, long position, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.WriteBytesAsync(source, position, length, cancellationToken); }

    public override ByteBuf AsReadOnly()
    { RecordLeakNonRefCountingOperation(_leak); return base.AsReadOnly(); }

    public override ReadOnlyMemory<byte> AsReadOnlyMemory(int index, int length)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.AsReadOnlyMemory(index, length); }

    public override ReadOnlySpan<byte> AsReadOnlySpan(int index, int length)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.AsReadOnlySpan(index, length); }

    public override ReadOnlySequence<byte> AsReadOnlySequence(int index, int length)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.AsReadOnlySequence(index, length); }

    public override ByteBuf GetBytes(int index, Stream destination, int length)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.GetBytes(index, destination, length); return this; }

    public override int SetBytes(int index, Stream source, int length)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.SetBytes(index, source, length); }

    public override ByteBuf ReadBytes(Stream destination, int length)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.ReadBytes(destination, length); return this; }

    public override int WriteBytes(Stream source, int length)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.WriteBytes(source, length); }

    public override ValueTask GetBytesAsync(int index, Stream destination, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetBytesAsync(index, destination, length, cancellationToken); }

    public override ValueTask<int> SetBytesAsync(int index, Stream source, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.SetBytesAsync(index, source, length, cancellationToken); }

    public override ValueTask ReadBytesAsync(Stream destination, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadBytesAsync(destination, length, cancellationToken); }

    public override ValueTask<int> WriteBytesAsync(Stream source, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.WriteBytesAsync(source, length, cancellationToken); }

    public override string GetString(Encoding encoding)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetString(encoding); }

    public override string GetString(int index, int length, Encoding encoding)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.GetString(index, length, encoding); }

    public override string ReadString(int length, Encoding encoding)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ReadString(length, encoding); }

    public override int SetString(int index, ReadOnlySpan<char> text, Encoding encoding)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.SetString(index, text, encoding); }

    public override int WriteString(ReadOnlySpan<char> text, Encoding encoding)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.WriteString(text, encoding); }

    public override int IndexOf(int fromIndex, int toIndex, byte value)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.IndexOf(fromIndex, toIndex, value); }

    public override int BytesBefore(byte value)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.BytesBefore(value); }

    public override int BytesBefore(int length, byte value)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.BytesBefore(length, value); }

    public override int BytesBefore(int index, int length, byte value)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.BytesBefore(index, length, value); }

    public override int ForEachByte(Func<byte, bool> processor)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ForEachByte(processor); }

    public override int ForEachByte(int index, int length, Func<byte, bool> processor)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ForEachByte(index, length, processor); }

    public override int ForEachByteDesc(Func<byte, bool> processor)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ForEachByteDesc(processor); }

    public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
    { RecordLeakNonRefCountingOperation(_leak); return Buffer.ForEachByteDesc(index, length, processor); }

    public override ByteBuf GetBytes(int index, ByteBuf destination)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.GetBytes(index, destination); return this; }

    public override ByteBuf GetBytes(int index, ByteBuf destination, int length)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.GetBytes(index, destination, length); return this; }

    public override ByteBuf SetBytes(int index, ByteBuf source)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.SetBytes(index, source); return this; }

    public override ByteBuf SetBytes(int index, ByteBuf source, int length)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.SetBytes(index, source, length); return this; }

    public override ByteBuf ReadBytes(ByteBuf destination)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.ReadBytes(destination); return this; }

    public override ByteBuf ReadBytes(ByteBuf destination, int destinationIndex, int length)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.ReadBytes(destination, destinationIndex, length); return this; }

    public override ByteBuf WriteBytes(ByteBuf source)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.WriteBytes(source); return this; }

    public override ByteBuf WriteBytes(ByteBuf source, int sourceIndex, int length)
    { RecordLeakNonRefCountingOperation(_leak); Buffer.WriteBytes(source, sourceIndex, length); return this; }

}
