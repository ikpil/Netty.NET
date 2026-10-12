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
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class SimpleLeakAwareCompositeByteBufTest
{
    public static IEnumerable<object[]> OperationCases()
    {
        for (int kind = 0; kind < 3; ++kind)
            for (int wrapper = 0; wrapper < 2; ++wrapper)
                for (int operation = 0; operation < 123; ++operation) yield return [kind, wrapper != 0, operation];
    }

    [Theory]
    [MemberData(nameof(OperationCases))]
    public void WrappedOperationsKeepCompositeStorageIndicesFailuresAndOriginalTracker(int kind, bool leakAware, int operation)
    {
        CompositeByteBuf expected = Owner(kind), origin = Owner(kind);
        var tracker = new Tracker(origin);
        CompositeByteBuf buffer = leakAware ? new SimpleLeakAwareCompositeByteBuf(origin, tracker) : new WrappedCompositeByteBuf(origin);
        ByteBuf expectedView = null, view = null;
        string expectedResult = "", result = "";
        try
        {
            Exception expectedFailure = Record.Exception(() => expectedResult = Execute(expected, operation, out expectedView));
            Exception failure = Record.Exception(() => result = Execute(buffer, operation, out view));
            Assert.Equal(expectedFailure?.GetType(), failure?.GetType()); Assert.Equal(expectedResult, result);
            Assert.Equal(Snapshot(expected), Snapshot(origin)); Assert.Equal(origin.NumComponents, buffer.NumComponents);
            Assert.Same(origin, buffer.Unwrap()); Assert.Equal(0, tracker.Records); Assert.Equal(0, tracker.Closes);
            if (leakAware && view != null && operation is >= 84 and <= 92) Assert.IsType<SimpleLeakAwareByteBuf>(view);
        }
        finally
        {
            CleanupView(expectedView, operation); CleanupView(view, operation);
            expected.Release(expected.ReferenceCount); buffer.Release(origin.ReferenceCount);
        }
        Assert.Equal(leakAware ? 1 : 0, tracker.Closes);
    }

    internal static CompositeByteBuf Owner(int kind, IByteBufAllocator allocator = null)
    {
        allocator ??= UnpooledByteBufAllocator.Default;
        var b = new CompositeByteBuf(allocator, kind == 1);
        int offset = 0;
        int[] lengths = [7, 0, 17, 40];
        for (int i = 0; i < lengths.Length; ++i)
        {
            int length = lengths[i];
            ByteBuf component = kind == 1 || kind == 2 && i % 2 == 1
                ? allocator.DirectBuffer(length, Math.Max(1, length)) : allocator.HeapBuffer(length, Math.Max(1, length));
            for (int j = 0; j < length; ++j) component.WriteByte(offset++);
            if (kind == 2 && i == 2) component = component.AsReadOnly();
            b.AddComponent(component, true);
        }
        return (CompositeByteBuf)b.SetIndex(2, 32);
    }

    private static ByteBuf Raw(ByteBuf b) => b is SimpleLeakAwareByteBuf ? b.Unwrap() : b;
    internal static string Snapshot(ByteBuf b) => b.Capacity + "," + b.ReaderIndex + "," + b.WriterIndex + "," +
        b.ReferenceCount + "," + ByteBufUtil.HexDump(b, 0, Math.Min(64, b.Capacity));
    internal static void CleanupView(ByteBuf view, int op)
    {
        if (view != null && (op is >= 89 and <= 95)) view.Release();
    }

    internal static string Execute(ByteBuf b, int operation, out ByteBuf view)
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



    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalNestedLeakAwareCompositeForEachByteDoesNotThrowAndClosesBothOwners(bool direct)
    {
        var outer = new CompositeByteBuf(); var inner = new CompositeByteBuf();
        var outerTracker = new Tracker(outer); var innerTracker = new Tracker(inner);
        var b = new SimpleLeakAwareCompositeByteBuf(outer, outerTracker);
        var c = new SimpleLeakAwareCompositeByteBuf(inner, innerTracker);
        ByteBuf component = direct ? c.Allocator.DirectBuffer(1) : c.Allocator.HeapBuffer(1);
        c.AddComponent(component.WriteByte(0), true); b.AddComponent(c, true);
        Assert.Equal(-1, b.ForEachByte(static _ => true)); Assert.True(b.Release());
        Assert.Equal(1, outerTracker.Closes); Assert.Equal(1, innerTracker.Closes); Assert.Equal(0, component.ReferenceCount);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void StructuralChangesEnumerationAndComponentCacheAreSharedThroughRawAndWrappedAliases(bool leakAware)
    {
        var origin = new CompositeByteBuf(); var tracker = new Tracker(origin);
        CompositeByteBuf b = leakAware ? new SimpleLeakAwareCompositeByteBuf(origin, tracker) : new WrappedCompositeByteBuf(origin);
        ByteBuf first = Unpooled.WrappedBuffer(new byte[] { 0, 1, 2, 3 }).SetIndex(1, 3);
        ByteBuf second = Unpooled.WrappedBuffer(new byte[] { 4, 5 });
        try
        {
            Assert.Same(b, b.AddComponent(first, true)); origin.AddComponent(second, true);
            Assert.Equal(2, b.NumComponents); Assert.Equal("01020405", ByteBufUtil.HexDump(b));
            Assert.Equal(1, b.ToComponentIndex(3)); Assert.Equal(2, b.ToByteIndex(1));
            Assert.Same(origin.ComponentSlice(0), b.ComponentSlice(0));
            Assert.Same(origin.InternalComponent(0), b.InternalComponent(0));
            b.ReaderIndex = 1; b.MarkReaderIndex(); origin.ReaderIndex = 2; origin.ResetReaderIndex();
            Assert.Equal(1, b.ReaderIndex);
            using var iterator = b.GetEnumerator(); Assert.True(iterator.MoveNext());
            origin.AddComponent(Unpooled.WrappedBuffer(new byte[] { 6 }), true);
            Assert.Throws<InvalidOperationException>(() => iterator.MoveNext());
            Assert.Same(b, b.RemoveComponent(2)); Assert.Equal(2, origin.NumComponents);
            Assert.Same(b, b.Consolidate()); Assert.Equal(1, origin.NumComponents);
            Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, second.ReferenceCount);
            Assert.Equal(0, tracker.Records);
        }
        finally { b.Release(); }
        Assert.Equal(leakAware ? 1 : 0, tracker.Closes);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void FlatteningWrappedSourcesTransfersComponentReferencesAndClosesOnlyConsumedComposite(int layers)
    {
        CompositeByteBuf origin = Owner(0); origin.SetIndex(5, 30);
        CompositeByteBuf source = origin;
        for (int i = 0; i < layers; ++i) source = new WrappedCompositeByteBuf(source);
        var innerTracker = new Tracker(source); source = new SimpleLeakAwareCompositeByteBuf(source, innerTracker);
        // A tracker is associated with the exact immediate wrapped referent, even through aliases.
        var exactTracker = new Tracker(source);
        var outer = new SimpleLeakAwareCompositeByteBuf(source, exactTracker);
        var destination = new CompositeByteBuf();
        try
        {
            Assert.Same(destination, destination.AddFlattenedComponents(outer, true));
            Assert.Equal(0, origin.ReferenceCount); Assert.Equal(1, exactTracker.Closes); Assert.Equal(1, innerTracker.Closes);
            Assert.Equal(3, destination.NumComponents); Assert.Equal(25, destination.ReadableBytes);
            for (int i = 0; i < 25; ++i) Assert.Equal(i + 5, destination.GetByte(i));
        }
        finally { destination.Release(); if (outer.ReferenceCount > 0) outer.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void RawWrappedAndSiblingAliasesCannotIntroduceOwnershipCyclesOrConsumeReferences(int mutation)
    {
        CompositeByteBuf origin = Owner(0); var first = new WrappedCompositeByteBuf(origin);
        var sibling = new WrappedCompositeByteBuf(origin);
        try
        {
            if (mutation == 0) Assert.Throws<ArgumentException>(() => first.AddComponent(origin));
            else if (mutation == 1) Assert.Throws<ArgumentException>(() => origin.AddComponents(new ByteBuf[] { sibling }));
            else Assert.Throws<ArgumentException>(() => first.AddFlattenedComponents(sibling));
            Assert.Equal(1, origin.ReferenceCount); Assert.Equal(4, origin.NumComponents);
            Assert.Equal(1, first.ReferenceCount); Assert.Equal(1, sibling.ReferenceCount);
        }
        finally { first.Release(); }
    }

    [Fact]
    public void VirtualViewFactoryCanSelectAdvancedViewsWithoutClosingEitherTrackerTwice()
    {
        CompositeByteBuf origin = Owner(0); var tracker = new Tracker(origin);
        var b = new AdvancedViews(origin, tracker); ByteBuf view = b.RetainedSlice();
        Assert.IsType<AdvancedLeakAwareByteBuf>(view); Assert.False(b.Release());
        Assert.Equal(2, view.ReadByte()); Assert.Equal(1, tracker.Records);
        Assert.True(view.Release()); Assert.Equal(1, tracker.Closes); Assert.Equal(2, tracker.Records);
    }

    [Fact]
    public void SimpleCompositeReleaseDoesNotInventByteWrapperCloseDiagnostics()
    {
        var origin = new CompositeByteBuf(); var tracker = new Tracker(origin);
        var b = new SimpleLeakAwareCompositeByteBuf(origin, tracker); Assert.True(b.Release());
        Assert.Throws<IllegalReferenceCountException>(() => b.Release());
        Assert.Equal(1, tracker.Closes); Assert.Equal(0, tracker.Records);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void LittleEndianFluentWritesKeepTheLeakAwareReceiverForFinalRelease(int operation)
    {
        CompositeByteBuf origin = Owner(0); var tracker = new Tracker(origin);
        ByteBuf b = new SimpleLeakAwareCompositeByteBuf(origin, tracker);
        ByteBuf returned = operation switch
        {
            0 => b.SetShortLE(3, 42), 1 => b.SetMediumLE(3, 42),
            2 => b.SetIntLE(3, 42), 3 => b.SetLongLE(3, 42),
            4 => b.WriteShortLE(42), 5 => b.WriteMediumLE(42),
            6 => b.WriteIntLE(42), _ => b.WriteLongLE(42)
        };
        Assert.Same(b, returned); Assert.True(returned.Release());
        Assert.Equal(1, tracker.Closes); Assert.Equal(0, tracker.Records);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativePinsAndInternalLeasesOutliveLogicalCompositeClose(bool internalLease)
    {
        var native = new NativeMemoryAllocator(); var allocator = new UnpooledByteBufAllocator(true, native);
        ByteBuf component = allocator.DirectBuffer(8, 8).WriteLong(42);
        var origin = new CompositeByteBuf(allocator, true).AddComponent(component, true);
        var tracker = new Tracker(origin); var b = new SimpleLeakAwareCompositeByteBuf(origin, tracker);
        var pin = internalLease ? default : b.AsMemory(0, 8).Pin();
        var lease = internalLease ? b.AcquireReadLease() : default;
        try
        {
            Assert.True(b.Release()); Assert.Equal(1, tracker.Closes); Assert.Equal(0, component.ReferenceCount);
            Assert.Equal(8, native.ReservedBytes);
        }
        finally { lease.Dispose(); pin.Dispose(); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ClrAsyncStreamCallsShareIndicesAndLeaveSimpleRecordingDisabled(bool direct)
    {
        CompositeByteBuf origin = Owner(direct ? 1 : 0); var tracker = new Tracker(origin);
        var b = new SimpleLeakAwareCompositeByteBuf(origin, tracker); using var stream = new MemoryStream(new byte[16]);
        try
        {
            await b.ReadBytesAsync(stream, 8, TestContext.Current.CancellationToken); Assert.Equal(10, origin.ReaderIndex);
            stream.Position = 0; Assert.Equal(8, await b.WriteBytesAsync(stream, 8, TestContext.Current.CancellationToken));
            Assert.Equal(40, origin.WriterIndex); Assert.Equal(0, tracker.Records); Assert.Equal(0, tracker.Closes);
        }
        finally { b.Release(); }
        Assert.Equal(1, tracker.Closes);
    }

    [Fact]
    public void InvalidConstructionLeavesOriginalCallerOwnershipUntouched()
    {
        Assert.Throws<ArgumentNullException>(() => new WrappedCompositeByteBuf(null));
        var origin = new CompositeByteBuf();
        try { Assert.Throws<ArgumentNullException>(() => new SimpleLeakAwareCompositeByteBuf(origin, null)); Assert.Equal(1, origin.ReferenceCount); }
        finally { origin.Release(); }
    }

    private sealed class AdvancedViews(CompositeByteBuf wrapped, IResourceLeakTracker<ByteBuf> tracker)
        : SimpleLeakAwareCompositeByteBuf(wrapped, tracker)
    {
        protected override SimpleLeakAwareByteBuf NewLeakAwareByteBuf(ByteBuf wrapped, ByteBuf tracked,
            IResourceLeakTracker<ByteBuf> leak) => new AdvancedLeakAwareByteBuf(wrapped, tracked, leak);
    }
    private sealed class Tracker(ByteBuf expected) : IResourceLeakTracker<ByteBuf>
    {
        internal int Records, Closes;
        public void Record() => Records++;
        public void Record(object hint) => Records++;
        public bool Close(ByteBuf b) { Assert.Same(expected, b); Assert.Equal(0, b.ReferenceCount); return ++Closes == 1; }
    }
}
