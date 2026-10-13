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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer.Tests;

public class AdvancedLeakAwareByteBufTest
{
    // Operation/cursor/byte behavior is compared with the same unwrapped CLR buffer.
    // The pinned Java differential probe additionally checks its recording policy.
    public static IEnumerable<object[]> OperationCases()
    {
        for (int kind = 0; kind < 6; ++kind)
            for (int operation = 0; operation < 123; ++operation) yield return [kind, operation];
    }

    [Theory]
    [MemberData(nameof(OperationCases))]
    public void AccessesRecordOnceAndPreserveStorageCursorResultAndFailureContracts(int kind, int operation)
    {
        ByteBuf expected = Owner(kind), origin = Owner(kind);
        var tracker = new Tracker(origin);
        ByteBuf buffer = new AdvancedLeakAwareByteBuf(origin, tracker);
        ByteBuf expectedView = null, view = null;
        string expectedResult = "", result = "";
        try
        {
            Exception expectedFailure = Record.Exception(() => expectedResult = Execute(expected, operation, out expectedView));
            Exception failure = Record.Exception(() => result = Execute(buffer, operation, out view));
            Assert.Equal(expectedFailure?.GetType(), failure?.GetType());
            Assert.Equal(expectedResult, result);
            Assert.Equal(Snapshot(expected), Snapshot(origin));
            Assert.Equal(1, tracker.Records);
            Assert.Equal(0, tracker.Closes);
            if (view != null && expectedView.Unwrap() != null && IsSharedView(operation))
                Assert.IsType<AdvancedLeakAwareByteBuf>(view);
        }
        finally
        {
            CleanupView(expectedView, operation);
            CleanupView(view, operation);
            expected.Release(expected.ReferenceCount);
            buffer.Release(origin.ReferenceCount);
        }
        Assert.Equal(1, tracker.Closes); Assert.Same(origin, tracker.Closed);
    }

    private static ByteBuf Owner(int kind)
    {
        ByteBuf b = kind switch
        {
            0 => Unpooled.Buffer(64, 128),
            1 => Unpooled.DirectBuffer(64, 128),
            2 => Unpooled.Buffer(64, 128),
            3 => Unpooled.CompositeBuffer().AddComponent(Unpooled.Buffer(31, 31).WriteZero(31), true)
                .AddComponent(Unpooled.Buffer(33, 33).WriteZero(33), true),
            4 => Unpooled.Buffer(66, 66).Slice(1, 64),
            _ => Unpooled.Buffer(64, 128).Duplicate()
        };
        for (int i = 0; i < 64; ++i) b.SetByte(i, i);
        b.SetIndex(2, 32);
        return kind == 2 ? b.AsReadOnly() : b;
    }

    private static string Snapshot(ByteBuf b) => b.Capacity + "," + b.ReaderIndex + "," + b.WriterIndex + "," +
        b.ReferenceCount + "," + ByteBufUtil.HexDump(b, 0, Math.Min(64, b.Capacity));
    private static ByteBuf Raw(ByteBuf b) => b is AdvancedLeakAwareByteBuf ? b.Unwrap() : b;
    private static bool IsSharedView(int op) => op is 84 or 85 or 86 or 87 or 88 or 89 or 90 or 91 or 92;
    private static void CleanupView(ByteBuf view, int op)
    {
        if (view != null && (op is 89 or 90 or 91 or 92 || op is 93 or 94 or 95)) view.Release();
    }

    private static string Execute(ByteBuf b, int operation, out ByteBuf view)
    {
        view = null; string result = "ok";
        switch (operation)
        {
            case 0: // GetBoolean
                { result = ((b.GetBoolean(3) ? 1 : 0)).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 1: // ReadBoolean
                { result = ((b.ReadBoolean() ? 1 : 0)).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 2: // GetByte
                { result = (b.GetByte(3)).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 3: // ReadByte
                { result = (b.ReadByte()).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 4: // GetShort
                { result = (b.GetShort(3)).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 5: // ReadShort
                { result = (b.ReadShort()).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 6: // GetUnsignedShort
                { result = (b.GetUnsignedShort(3)).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 7: // ReadUnsignedShort
                { result = (b.ReadUnsignedShort()).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 8: // GetMedium
                { result = (b.GetMedium(3)).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 9: // ReadMedium
                { result = (b.ReadMedium()).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 10: // GetUnsignedMedium
                { result = (b.GetUnsignedMedium(3)).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 11: // ReadUnsignedMedium
                { result = (b.ReadUnsignedMedium()).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 12: // GetInt
                { result = (b.GetInt(3)).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 13: // ReadInt
                { result = (b.ReadInt()).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 14: // GetUnsignedInt
                { result = (b.GetUnsignedInt(3)).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 15: // ReadUnsignedInt
                { result = (b.ReadUnsignedInt()).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 16: // GetLong
                { result = (b.GetLong(3)).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 17: // ReadLong
                { result = (b.ReadLong()).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 18: // GetChar
                { result = ((int)b.GetChar(3)).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 19: // ReadChar
                { result = ((int)b.ReadChar()).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 20: // GetFloat
                { result = (BitConverter.SingleToInt32Bits(b.GetFloat(3))).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 21: // ReadFloat
                { result = (BitConverter.SingleToInt32Bits(b.ReadFloat())).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 22: // GetDouble
                { result = (BitConverter.DoubleToInt64Bits(b.GetDouble(3))).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 23: // ReadDouble
                { result = (BitConverter.DoubleToInt64Bits(b.ReadDouble())).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 24: // GetShortLE
                { result = (b.GetShortLE(3)).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 25: // ReadShortLE
                { result = (b.ReadShortLE()).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 26: // GetUnsignedShortLE
                { result = (b.GetUnsignedShortLE(3)).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 27: // ReadUnsignedShortLE
                { result = (b.ReadUnsignedShortLE()).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 28: // GetMediumLE
                { result = (b.GetMediumLE(3)).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 29: // ReadMediumLE
                { result = (b.ReadMediumLE()).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 30: // GetUnsignedMediumLE
                { result = (b.GetUnsignedMediumLE(3)).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 31: // ReadUnsignedMediumLE
                { result = (b.ReadUnsignedMediumLE()).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 32: // GetIntLE
                { result = (b.GetIntLE(3)).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 33: // ReadIntLE
                { result = (b.ReadIntLE()).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 34: // GetUnsignedIntLE
                { result = (b.GetUnsignedIntLE(3)).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 35: // ReadUnsignedIntLE
                { result = (b.ReadUnsignedIntLE()).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 36: // GetLongLE
                { result = (b.GetLongLE(3)).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 37: // ReadLongLE
                { result = (b.ReadLongLE()).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case 38: // SetBoolean
                { result = ReferenceEquals(b.SetBoolean(3, true), b) ? "self" : "other"; break; }
            case 39: // WriteBoolean
                { result = ReferenceEquals(b.WriteBoolean(true), b) ? "self" : "other"; break; }
            case 40: // SetByte
                { result = ReferenceEquals(b.SetByte(3, 173), b) ? "self" : "other"; break; }
            case 41: // WriteByte
                { result = ReferenceEquals(b.WriteByte(173), b) ? "self" : "other"; break; }
            case 42: // SetShort
                { result = ReferenceEquals(b.SetShort(3, -12345), b) ? "self" : "other"; break; }
            case 43: // WriteShort
                { result = ReferenceEquals(b.WriteShort(-12345), b) ? "self" : "other"; break; }
            case 44: // SetMedium
                { result = ReferenceEquals(b.SetMedium(3, -543210), b) ? "self" : "other"; break; }
            case 45: // WriteMedium
                { result = ReferenceEquals(b.WriteMedium(-543210), b) ? "self" : "other"; break; }
            case 46: // SetInt
                { result = ReferenceEquals(b.SetInt(3, -123456789), b) ? "self" : "other"; break; }
            case 47: // WriteInt
                { result = ReferenceEquals(b.WriteInt(-123456789), b) ? "self" : "other"; break; }
            case 48: // SetLong
                { result = ReferenceEquals(b.SetLong(3, -9876543210L), b) ? "self" : "other"; break; }
            case 49: // WriteLong
                { result = ReferenceEquals(b.WriteLong(-9876543210L), b) ? "self" : "other"; break; }
            case 50: // SetChar
                { result = ReferenceEquals(b.SetChar(3, 'Z'), b) ? "self" : "other"; break; }
            case 51: // WriteChar
                { result = ReferenceEquals(b.WriteChar('Z'), b) ? "self" : "other"; break; }
            case 52: // SetFloat
                { result = ReferenceEquals(b.SetFloat(3, 1.25f), b) ? "self" : "other"; break; }
            case 53: // WriteFloat
                { result = ReferenceEquals(b.WriteFloat(1.25f), b) ? "self" : "other"; break; }
            case 54: // SetDouble
                { result = ReferenceEquals(b.SetDouble(3, -2.5), b) ? "self" : "other"; break; }
            case 55: // WriteDouble
                { result = ReferenceEquals(b.WriteDouble(-2.5), b) ? "self" : "other"; break; }
            case 56: // SetShortLE
                { result = ReferenceEquals(b.SetShortLE(3, -12345), b) ? "self" : "other"; break; }
            case 57: // WriteShortLE
                { result = ReferenceEquals(b.WriteShortLE(-12345), b) ? "self" : "other"; break; }
            case 58: // SetMediumLE
                { result = ReferenceEquals(b.SetMediumLE(3, -543210), b) ? "self" : "other"; break; }
            case 59: // WriteMediumLE
                { result = ReferenceEquals(b.WriteMediumLE(-543210), b) ? "self" : "other"; break; }
            case 60: // SetIntLE
                { result = ReferenceEquals(b.SetIntLE(3, -123456789), b) ? "self" : "other"; break; }
            case 61: // WriteIntLE
                { result = ReferenceEquals(b.WriteIntLE(-123456789), b) ? "self" : "other"; break; }
            case 62: // SetLongLE
                { result = ReferenceEquals(b.SetLongLE(3, -9876543210L), b) ? "self" : "other"; break; }
            case 63: // WriteLongLE
                { result = ReferenceEquals(b.WriteLongLE(-9876543210L), b) ? "self" : "other"; break; }
            case 64: // EnsureWritable
                { result = ReferenceEquals(b.EnsureWritable(2), b) ? "self" : "other"; break; }
            case 65: // DiscardReadBytes
                { result = ReferenceEquals(b.DiscardReadBytes(), b) ? "self" : "other"; break; }
            case 66: // DiscardSomeReadBytes
                { result = ReferenceEquals(b.DiscardSomeReadBytes(), b) ? "self" : "other"; break; }
            case 67: // SkipBytes
                { result = ReferenceEquals(b.SkipBytes(3), b) ? "self" : "other"; break; }
            case 68: // SetZero
                { result = ReferenceEquals(b.SetZero(3, 7), b) ? "self" : "other"; break; }
            case 69: // WriteZero
                { result = ReferenceEquals(b.WriteZero(7), b) ? "self" : "other"; break; }
            case 70: // EnsureWritableForce
                { result = b.EnsureWritable(40, true).ToString(); break; }
            case 71: // IndexOf
                { result = b.IndexOf(2, 25, 12).ToString(); break; }
            case 72: // BytesBefore
                { result = b.BytesBefore(12).ToString(); break; }
            case 73: // BytesBeforeLength
                { result = b.BytesBefore(20, 12).ToString(); break; }
            case 74: // BytesBeforeRange
                { result = b.BytesBefore(2, 20, 12).ToString(); break; }
            case 75: // ForEachByte
                { result = b.ForEachByte(v => v != 12).ToString(); break; }
            case 76: // ForEachByteRange
                { result = b.ForEachByte(2, 20, v => v != 12).ToString(); break; }
            case 77: // ForEachByteDesc
                { result = b.ForEachByteDesc(v => v != 12).ToString(); break; }
            case 78: // ForEachByteDescRange
                { result = b.ForEachByteDesc(2, 20, v => v != 12).ToString(); break; }
            case 79: // SetString
                { result = b.SetString(3, "netty", Encoding.ASCII).ToString(); break; }
            case 80: // WriteString
                { result = b.WriteString("netty", Encoding.ASCII).ToString(); break; }
            case 81: // GetString
                { result = Convert.ToHexStringLower(Encoding.ASCII.GetBytes(b.GetString(Encoding.ASCII))); break; }
            case 82: // GetStringRange
                { result = Convert.ToHexStringLower(Encoding.ASCII.GetBytes(b.GetString(3, 8, Encoding.ASCII))); break; }
            case 83: // ReadString
                { result = Convert.ToHexStringLower(Encoding.ASCII.GetBytes(b.ReadString(8, Encoding.ASCII))); break; }
            case 84: // Slice
                { view = b.Slice(); result = Raw(view).Capacity+","+Raw(view).ReaderIndex+","+Raw(view).WriterIndex+","+ByteBufUtil.HexDump(Raw(view)); break; }
            case 85: // SliceRange
                { view = b.Slice(3,8); result = Raw(view).Capacity+","+Raw(view).ReaderIndex+","+Raw(view).WriterIndex+","+ByteBufUtil.HexDump(Raw(view)); break; }
            case 86: // Duplicate
                { view = b.Duplicate(); result = Raw(view).Capacity+","+Raw(view).ReaderIndex+","+Raw(view).WriterIndex+","+ByteBufUtil.HexDump(Raw(view)); break; }
            case 87: // ReadSlice
                { view = b.ReadSlice(8); result = Raw(view).Capacity+","+Raw(view).ReaderIndex+","+Raw(view).WriterIndex+","+ByteBufUtil.HexDump(Raw(view)); break; }
            case 88: // ReadOnly
                { view = b.AsReadOnly(); result = Raw(view).Capacity+","+Raw(view).ReaderIndex+","+Raw(view).WriterIndex+","+ByteBufUtil.HexDump(Raw(view)); break; }
            case 89: // RetainedSlice
                { view = b.RetainedSlice(); result = Raw(view).Capacity+","+Raw(view).ReaderIndex+","+Raw(view).WriterIndex+","+ByteBufUtil.HexDump(Raw(view)); break; }
            case 90: // RetainedSliceRange
                { view = b.RetainedSlice(3,8); result = Raw(view).Capacity+","+Raw(view).ReaderIndex+","+Raw(view).WriterIndex+","+ByteBufUtil.HexDump(Raw(view)); break; }
            case 91: // RetainedDuplicate
                { view = b.RetainedDuplicate(); result = Raw(view).Capacity+","+Raw(view).ReaderIndex+","+Raw(view).WriterIndex+","+ByteBufUtil.HexDump(Raw(view)); break; }
            case 92: // ReadRetainedSlice
                { view = b.ReadRetainedSlice(8); result = Raw(view).Capacity+","+Raw(view).ReaderIndex+","+Raw(view).WriterIndex+","+ByteBufUtil.HexDump(Raw(view)); break; }
            case 93: // Copy
                { view = b.Copy(); result = Raw(view).Capacity+","+Raw(view).ReaderIndex+","+Raw(view).WriterIndex+","+ByteBufUtil.HexDump(Raw(view)); break; }
            case 94: // CopyRange
                { view = b.Copy(3,8); result = Raw(view).Capacity+","+Raw(view).ReaderIndex+","+Raw(view).WriterIndex+","+ByteBufUtil.HexDump(Raw(view)); break; }
            case 95: // ReadAllocated
                { view = b.ReadBytes(8); result = Raw(view).Capacity+","+Raw(view).ReaderIndex+","+Raw(view).WriterIndex+","+ByteBufUtil.HexDump(Raw(view)); break; }
            case 96: // GetBytesArray
                { byte[] data=new byte[8]; b.GetBytes(3,data.AsSpan()); result=Convert.ToHexStringLower(data); break; }
            case 97: // ReadBytesArray
                { byte[] data=new byte[8]; b.ReadBytes(data.AsSpan()); result=Convert.ToHexStringLower(data); break; }
            case 98: // SetBytesArray
                { result=ReferenceEquals(b.SetBytes(3,new byte[]{90,91,92}.AsSpan()),b)?"self":"other"; break; }
            case 99: // WriteBytesArray
                { result=ReferenceEquals(b.WriteBytes(new byte[]{90,91,92}.AsSpan()),b)?"self":"other"; break; }
            case 100: // GetBytes0
                { ByteBuf other=Unpooled.Buffer(8,8).SetZero(0,8).WriteBytes(new byte[]{90,91,92,93}); try { b.GetBytes(3,other); result=other.ReaderIndex+","+other.WriterIndex+","+ByteBufUtil.HexDump(other,0,8); } finally { other.Release(); } break; }
            case 101: // GetBytes1
                { ByteBuf other=Unpooled.Buffer(8,8).SetZero(0,8).WriteBytes(new byte[]{90,91,92,93}); try { b.GetBytes(3,other,4); result=other.ReaderIndex+","+other.WriterIndex+","+ByteBufUtil.HexDump(other,0,8); } finally { other.Release(); } break; }
            case 102: // GetBytes2
                { ByteBuf other=Unpooled.Buffer(8,8).SetZero(0,8).WriteBytes(new byte[]{90,91,92,93}); try { b.GetBytes(3,other,2,4); result=other.ReaderIndex+","+other.WriterIndex+","+ByteBufUtil.HexDump(other,0,8); } finally { other.Release(); } break; }
            case 103: // ReadBytes0
                { ByteBuf other=Unpooled.Buffer(8,8).SetZero(0,8).WriteBytes(new byte[]{90,91,92,93}); try { b.ReadBytes(other); result=other.ReaderIndex+","+other.WriterIndex+","+ByteBufUtil.HexDump(other,0,8); } finally { other.Release(); } break; }
            case 104: // ReadBytes1
                { ByteBuf other=Unpooled.Buffer(8,8).SetZero(0,8).WriteBytes(new byte[]{90,91,92,93}); try { b.ReadBytes(other,4); result=other.ReaderIndex+","+other.WriterIndex+","+ByteBufUtil.HexDump(other,0,8); } finally { other.Release(); } break; }
            case 105: // ReadBytes2
                { ByteBuf other=Unpooled.Buffer(8,8).SetZero(0,8).WriteBytes(new byte[]{90,91,92,93}); try { b.ReadBytes(other,2,4); result=other.ReaderIndex+","+other.WriterIndex+","+ByteBufUtil.HexDump(other,0,8); } finally { other.Release(); } break; }
            case 106: // SetBytes0
                { ByteBuf other=Unpooled.Buffer(8,8).SetZero(0,8).WriteBytes(new byte[]{90,91,92,93}); try { b.SetBytes(3,other); result=other.ReaderIndex+","+other.WriterIndex+","+ByteBufUtil.HexDump(other,0,8); } finally { other.Release(); } break; }
            case 107: // SetBytes1
                { ByteBuf other=Unpooled.Buffer(8,8).SetZero(0,8).WriteBytes(new byte[]{90,91,92,93}); try { b.SetBytes(3,other,4); result=other.ReaderIndex+","+other.WriterIndex+","+ByteBufUtil.HexDump(other,0,8); } finally { other.Release(); } break; }
            case 108: // SetBytes2
                { ByteBuf other=Unpooled.Buffer(8,8).SetZero(0,8).WriteBytes(new byte[]{90,91,92,93}); try { b.SetBytes(3,other,2,4); result=other.ReaderIndex+","+other.WriterIndex+","+ByteBufUtil.HexDump(other,0,8); } finally { other.Release(); } break; }
            case 109: // WriteBytes0
                { ByteBuf other=Unpooled.Buffer(8,8).SetZero(0,8).WriteBytes(new byte[]{90,91,92,93}); try { b.WriteBytes(other); result=other.ReaderIndex+","+other.WriterIndex+","+ByteBufUtil.HexDump(other,0,8); } finally { other.Release(); } break; }
            case 110: // WriteBytes1
                { ByteBuf other=Unpooled.Buffer(8,8).SetZero(0,8).WriteBytes(new byte[]{90,91,92,93}); try { b.WriteBytes(other,4); result=other.ReaderIndex+","+other.WriterIndex+","+ByteBufUtil.HexDump(other,0,8); } finally { other.Release(); } break; }
            case 111: // WriteBytes2
                { ByteBuf other=Unpooled.Buffer(8,8).SetZero(0,8).WriteBytes(new byte[]{90,91,92,93}); try { b.WriteBytes(other,2,4); result=other.ReaderIndex+","+other.WriterIndex+","+ByteBufUtil.HexDump(other,0,8); } finally { other.Release(); } break; }
            case 112: // Capacity
                { b.Capacity=48; result="ok"; break; }
            case 113: // InvalidGet
                { b.GetInt(63); break; }
            case 114: // InvalidSet
                { b.SetLong(-1,3); break; }
            case 115: // InvalidRead
                { b.ReadBytes(100); break; }
            case 116: // InvalidSlice
                { b.Slice(-1,2); break; }
            case 117: // InvalidEnsure
                { b.EnsureWritable(-1); break; }
            case 118: // Retain
                { b.Retain(); result="ok"; break; }
            case 119: // Touch
                { b.Touch("hint"); result="ok"; break; }
            case 120: // TouchNull
                { b.Touch(); result="ok"; break; }
            case 121: // InvalidRetain
                { b.Retain(0); break; }
            case 122: // InvalidRelease
                { b.Release(0); break; }
        }
        return result;
    }

    [Fact]
    public void OriginalAddComponentWithLeakAwareByteBufPreservesSlicedContentAndClosesTracker()
    {
        ByteBuf origin = Unpooled.WrappedBuffer(Encoding.ASCII.GetBytes("hello world")).Slice(6, 5);
        var tracker = new Tracker(origin);
        ByteBuf buffer = new AdvancedLeakAwareByteBuf(origin, tracker);
        var composite = Unpooled.CompositeBuffer().AddComponent(buffer, true);
        try
        {
            byte[] result = new byte[5]; composite.Component(0).ReadBytes(result);
            Assert.Equal("world", Encoding.ASCII.GetString(result));
            Assert.Equal(0, tracker.Closes);
        }
        finally { composite.Release(); }
        Assert.Equal(1, tracker.Closes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PooledRetainedViewsTrackIndependentlyAndKeepParentReservationAlive(bool direct)
    {
        var native = new NativeMemoryAllocator();
        using var allocator = new PooledByteBufAllocator(nativeAllocator: native, smallCacheSize: 4);
        ByteBuf origin = direct ? new PooledDirectByteBuf((PoolDirectArena)allocator.DirectArenas[0], 16, 64)
            : new PooledHeapByteBuf((PoolHeapArena)allocator.HeapArenas[0], 16, 64);
        origin.WriteInt(42);
        var tracker = new Tracker(origin);
        ByteBuf buffer = new AdvancedLeakAwareByteBuf(origin, tracker);
        ByteBuf view = buffer.RetainedSlice();
        Assert.IsType<AdvancedLeakAwareByteBuf>(view); Assert.Equal(1, tracker.Records);
        Assert.NotSame(tracker, AllocatorLeakDetectionTest.Tracker(view)); Assert.Equal(1, view.ReferenceCount);
        Assert.False(buffer.Release()); Assert.Equal(0, tracker.Closes);
        Assert.Equal(42, view.ReadInt()); Assert.Equal(2, tracker.Records);
        allocator.Dispose(); Assert.Equal(42, view.GetInt(0));
        Assert.True(view.Release()); Assert.Equal(3, tracker.Records); Assert.Equal(1, tracker.Closes); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void ClrMemoryAndLittleEndianFloatingPointAccessesRecordOnce(int operation)
    {
        ByteBuf origin = Owner(0); var tracker = new Tracker(origin);
        ByteBuf b = new AdvancedLeakAwareByteBuf(origin, tracker);
        try
        {
            switch (operation)
            {
                case 0: Assert.Equal(3, b.AsMemory(3, 8).Span[0]); break;
                case 1: Assert.Equal(3, b.AsSpan(3, 8)[0]); break;
                case 2: Assert.Equal(3, b.AsReadOnlyMemory(3, 8).Span[0]); break;
                case 3: Assert.Equal(3, b.AsReadOnlySpan(3, 8)[0]); break;
                case 4: Assert.Equal(8, b.AsReadOnlySequence(3, 8).Length); break;
                case 5: b.SetFloatLE(3, 1.25f); Assert.Equal(1.25f, origin.GetFloatLE(3)); break;
                case 6: b.WriteDoubleLE(2.5); Assert.Equal(2.5, origin.GetDoubleLE(32)); break;
                case 7: origin.SetFloatLE(2, 1.25f); Assert.Equal(1.25f, b.ReadFloatLE()); break;
            }
            Assert.Equal(1, tracker.Records);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MetadataIndexMarksAndTouchKeepOriginalRecordingAndFluentPolicy(bool explicitHint)
    {
        ByteBuf origin = Owner(0); var tracker = new Tracker(origin); var b = new AdvancedLeakAwareByteBuf(origin, tracker);
        try
        {
            _ = b.Capacity; _ = b.Allocator; _ = b.IsDirect; _ = b.IsReadOnly; _ = b.ReadableBytes; _ = b.ReferenceCount;
            Assert.Equal(origin.GetHashCode(), b.GetHashCode());
            Assert.Equal(0, b.CompareTo(origin)); Assert.True(b.Equals(origin)); Assert.True(b.Equals((object)origin));
            b.SetIndex(1, 6).MarkReaderIndex().MarkWriterIndex().Clear().ResetWriterIndex().ResetReaderIndex();
            Assert.Equal(0, tracker.Records); Assert.Equal(1, origin.ReaderIndex); Assert.Equal(6, origin.WriterIndex);
            object hint = explicitHint ? new object() : null;
            Assert.Same(b, b.Touch(hint)); Assert.Equal(1, tracker.Records); Assert.Same(hint, tracker.Hint);
            Assert.Same(b, b.WriteInt(42)); Assert.Equal(2, tracker.Records);
        }
        finally { b.Release(); }
        Assert.True(b.Equals(b)); Assert.True(b.Equals((object)b)); Assert.Equal(0, b.CompareTo(b));
        Assert.Equal(3, tracker.Records);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void FailedReferenceOperationsRecordBeforeAttachingOriginalCloseDiagnostics(int operation)
    {
        ByteBuf origin = Unpooled.Buffer(8); var tracker = new Tracker(origin) { Trace = new Exception("first close") };
        ByteBuf b = new AdvancedLeakAwareByteBuf(origin, tracker); b.Release();
        Exception failure = Record.Exception(() =>
        {
            switch (operation)
            {
                case 0: b.Release(); break;
                case 1: b.Retain(); break;
                case 2: b.RetainedSlice(); break;
                case 3: b.ReadRetainedSlice(1); break;
            }
        });
        Assert.IsType<IllegalReferenceCountException>(failure);
        Assert.Equal(2, tracker.Records); Assert.Equal(1, tracker.Closes);
        Assert.Same(tracker.Trace, Assert.Single(ThrowableUtil.GetSuppressed(failure)));
    }

    [Fact]
    public void OriginalCloseCallSurvivesAdvancedWrappingAndHelperFramesAreExcluded()
    {
        ByteBuf origin = Unpooled.Buffer(8); var detector = new ResourceLeakDetector<ByteBuf>(typeof(ByteBuf), 1);
        IResourceLeakTracker<ByteBuf> tracker = detector.TrackForcibly(origin);
        ByteBuf b = new AdvancedLeakAwareByteBuf(origin, tracker);
        b.Touch("last access");
        _ = b.GetByte(0);
        string access = tracker.ToString();
        Assert.Contains("last access", access); Assert.DoesNotContain("RecordLeakNonRefCountingOperation", access);
        OriginalCloseCall(b);
        var failure = Assert.Throws<IllegalReferenceCountException>(() => b.Release());
        Assert.Contains(nameof(OriginalCloseCall), Assert.Single(ThrowableUtil.GetSuppressed(failure)).ToString());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OriginalCloseCall(ByteBuf b) => b.Release();

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SynchronousStreamTransfersRecordOnceAndRetainOuterFluentIdentity(bool direct)
    {
        ByteBuf origin = Owner(direct ? 1 : 0); var tracker = new Tracker(origin);
        ByteBuf b = new AdvancedLeakAwareByteBuf(origin, tracker);
        using var stream = new MemoryStream();
        try
        {
            Assert.Same(b, b.GetBytes(3, stream, 8)); Assert.Equal(1, tracker.Records);
            Assert.Equal("030405060708090a", Convert.ToHexStringLower(stream.ToArray()));
            Assert.Same(b, b.ReadBytes(stream, 8)); Assert.Equal(2, tracker.Records);
            stream.Position = 0;
            Assert.Equal(8, b.SetBytes(3, stream, 8)); Assert.Equal(3, tracker.Records);
            Assert.Equal(8, b.WriteBytes(stream, 8)); Assert.Equal(4, tracker.Records);
            Assert.Equal(10, origin.ReaderIndex); Assert.Equal(40, origin.WriterIndex);
            Assert.Throws<ArgumentNullException>(() => b.GetBytes(3, (Stream)null, 8));
            Assert.Equal(5, tracker.Records);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task AsyncStreamTransfersAndCancellationRecordOnce(bool cancel)
    {
        ByteBuf origin = Owner(1); var tracker = new Tracker(origin); ByteBuf b = new AdvancedLeakAwareByteBuf(origin, tracker);
        using var stream = new MemoryStream(new byte[64]);
        try
        {
            CancellationToken token = cancel ? new CancellationToken(true) : TestContext.Current.CancellationToken;
            async Task Run(ValueTask pending)
            {
                if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
                else await pending;
            }
            await Run(b.GetBytesAsync(3, stream, 8, token)); Assert.Equal(1, tracker.Records);
            await Run(b.ReadBytesAsync(stream, 8, token)); Assert.Equal(2, tracker.Records);
            if (cancel) Assert.Equal(2, b.ReaderIndex); else Assert.Equal(10, b.ReaderIndex);
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await b.SetBytesAsync(3, stream, 8, token));
            else { stream.Position = 0; Assert.Equal(8, await b.SetBytesAsync(3, stream, 8, token)); }
            Assert.Equal(3, tracker.Records);
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await b.WriteBytesAsync(stream, 8, token));
            else { stream.Position = 0; Assert.Equal(8, await b.WriteBytesAsync(stream, 8, token)); }
            Assert.Equal(4, tracker.Records); Assert.Equal(cancel ? 32 : 40, b.WriterIndex);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task PositionedFilesRecordEachSyncAndAsyncTransferOnce(bool asyncIo)
    {
        string path = Path.GetTempFileName(); ByteBuf origin = Owner(1); var tracker = new Tracker(origin);
        ByteBuf b = new AdvancedLeakAwareByteBuf(origin, tracker);
        try
        {
            using var file = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read,
                asyncIo ? FileOptions.Asynchronous : FileOptions.None);
            var token = TestContext.Current.CancellationToken;
            if (asyncIo)
            {
                Assert.Equal(8, await b.GetBytesAsync(3, file, 0, 8, token)); Assert.Equal(1, tracker.Records);
                Assert.Equal(8, await b.ReadBytesAsync(file, 8, 8, token)); Assert.Equal(2, tracker.Records);
                Assert.Equal(8, await b.SetBytesAsync(3, file, 0, 8, token)); Assert.Equal(3, tracker.Records);
                Assert.Equal(8, await b.WriteBytesAsync(file, 8, 8, token)); Assert.Equal(4, tracker.Records);
            }
            else
            {
                Assert.Equal(8, b.GetBytes(3, file, 0, 8)); Assert.Equal(1, tracker.Records);
                Assert.Equal(8, b.ReadBytes(file, 8, 8)); Assert.Equal(2, tracker.Records);
                Assert.Equal(8, b.SetBytes(3, file, 0, 8)); Assert.Equal(3, tracker.Records);
                Assert.Equal(8, b.WriteBytes(file, 8, 8)); Assert.Equal(4, tracker.Records);
            }
            Assert.Equal(10, origin.ReaderIndex); Assert.Equal(40, origin.WriterIndex);
        }
        finally { b.Release(); File.Delete(path); }
    }

    private sealed class Tracker(ByteBuf expected) : IResourceLeakTracker<ByteBuf>
    {
        internal int Records, Closes;
        internal object Hint;
        internal ByteBuf Closed;
        internal Exception Trace;
        public void Record() => Records++;
        public void Record(object hint) { Hint = hint; Records++; }
        public bool Close(ByteBuf trackedObject)
        {
            Assert.Same(expected, trackedObject); Assert.Equal(0, trackedObject.ReferenceCount);
            Closed = trackedObject; return ++Closes == 1;
        }
        public Exception GetCloseStackTraceIfAny() => Closes == 0 ? null : Trace;
    }
}
