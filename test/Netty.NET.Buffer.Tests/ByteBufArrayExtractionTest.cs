/*
 * Copyright 2014 The Netty Project
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
using System.Linq;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteBufArrayExtractionTest
{
    private static ByteBuf Owner(int kind, byte[] data)
    {
        int split = Math.Min(2, data.Length);
        return kind switch
        {
            0 => Unpooled.WrappedBuffer(data),
            1 => Unpooled.DirectBuffer(data.Length, data.Length).WriteBytes(data),
            2 => Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(data[..split]), true)
                .AddComponent(Unpooled.EmptyBuffer).AddComponent(Unpooled.WrappedBuffer(data[split..]), true),
            3 => Unpooled.WrappedUnmodifiableBuffer(Unpooled.WrappedBuffer(data[..split]), Unpooled.WrappedBuffer(data[split..])),
            4 => Unpooled.WrappedBuffer(new byte[] { 9 }.Concat(data).Append((byte)9).ToArray()).Slice(1, data.Length),
            5 => Unpooled.WrappedBuffer(data).AsReadOnly(),
            6 => Unpooled.WrappedBuffer(data).Duplicate(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void OriginalGetBytesScenarios(int kind)
    {
        ByteBuf b = Owner(kind, new byte[] { 1, 2, 3, 4 });
        try { CheckOriginalRanges(b); }
        finally { b.Release(); }
    }

    private static void CheckOriginalRanges(ByteBuf b)
    {
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, ByteBufUtil.GetBytes(b));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, ByteBufUtil.GetBytes(b, 0, b.ReadableBytes, false));
        foreach (bool copy in new[] { true, false })
        {
            Assert.Equal(new byte[] { 1, 2, 3 }, ByteBufUtil.GetBytes(b, 0, 3, copy));
            Assert.Equal(new byte[] { 2, 3, 4 }, ByteBufUtil.GetBytes(b, 1, 3, copy));
            Assert.Equal(new byte[] { 2, 3 }, ByteBufUtil.GetBytes(b, 1, 2, copy));
        }
        Assert.Equal(new byte[] { 1, 2, 3 }, ByteBufUtil.GetBytes(b, 0, 3));
        Assert.Equal(new byte[] { 2, 3, 4 }, ByteBufUtil.GetBytes(b, 1, 3));
        Assert.Equal(new byte[] { 2, 3 }, ByteBufUtil.GetBytes(b, 1, 2));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void OriginalNonzeroArrayOffsetAndArrayLongerThanSliceNeverLeakOtherBytes(int offset)
    {
        byte[] storage = { 5, 0, 0, 0, 5 }; ByteBuf owner = Unpooled.WrappedBuffer(storage);
        try
        {
            ByteBuf slice = owner.Slice(offset, 4); slice.WriterIndex = 0; slice.WriteInt(0x01020304);
            CheckOriginalRanges(slice);
            byte[] extracted = ByteBufUtil.GetBytes(slice, 0, 4, false);
            Assert.NotSame(storage, extracted); Assert.Equal(4, extracted.Length);
            extracted[0] = 99; Assert.Equal(1, slice.GetByte(0));
            Assert.Equal(5, storage[offset == 0 ? 4 : 0]);
        }
        finally { owner.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void DefaultCopiesReadableBytesAndAbsoluteRangesMayPassWriterIndex(int kind)
    {
        byte[] data = Enumerable.Range(0, 32).Select(i => unchecked((byte)(i * 17))).ToArray();
        ByteBuf b = Owner(kind, data);
        try
        {
            b.SetIndex(3, 20).MarkReaderIndex().MarkWriterIndex();
            byte[] readable = ByteBufUtil.GetBytes(b); Assert.Equal(data[3..20], readable);
            readable[0] ^= 128; Assert.Equal(data[3], b.GetByte(3));
            byte[] copy = ByteBufUtil.GetBytes(b, 0, 32); Assert.Equal(data, copy); Assert.NotSame(data, copy);
            copy[0] ^= 128; Assert.Equal(data[0], b.GetByte(0));
            byte[] partial = ByteBufUtil.GetBytes(b, 1, 30, false); Assert.Equal(data[1..31], partial);
            partial[0] ^= 128; Assert.Equal(data[1], b.GetByte(1));
            Assert.Equal(data[20..], ByteBufUtil.GetBytes(b, 20, 12));
            Assert.Equal(data[20..], ByteBufUtil.GetBytes(b, 20, 12, false));
            b.SetIndex(4, 21).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(3, b.ReaderIndex); Assert.Equal(20, b.WriterIndex); Assert.Equal(1, b.ReferenceCount);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, true)]
    [InlineData(3, false)] [InlineData(4, false)] [InlineData(5, true)]
    [InlineData(6, false)] [InlineData(7, true)] [InlineData(8, false)]
    [InlineData(9, true)] [InlineData(10, true)] [InlineData(11, false)]
    [InlineData(12, false)] [InlineData(13, false)]
    public void SharingRequiresEntireWritableManagedArrayMapping(int kind, bool shared)
    {
        byte[] storage = Enumerable.Range(1, 8).Select(i => (byte)i).ToArray();
        ByteBuf root = kind == 12 ? Unpooled.DirectBuffer(8, 8).WriteBytes(storage) : Unpooled.WrappedBuffer(storage);
        ByteBuf owner = root, b = root;
        switch (kind)
        {
            case 1: b = root.Duplicate(); break;
            case 2: b = root.Slice(0, 8); break;
            case 3: b = root.Slice(1, 7); break;
            case 4: b = root.Slice(0, 7); break;
            case 5: b = Unpooled.UnreleasableBuffer(root); break;
            case 6: b = root.AsReadOnly(); break;
            case 7: owner = b = Unpooled.CompositeBuffer().AddComponent(root, true); break;
            case 8: owner = b = Unpooled.CompositeBuffer().AddComponent(root.AsReadOnly(), true); break;
            case 9:
                owner = Unpooled.CompositeBuffer().AddComponent(root, true).AddComponent(Unpooled.WrappedBuffer(new byte[2]), true);
                b = owner.Slice(0, 8); break;
            case 10: owner = b = Unpooled.CompositeBuffer().AddComponent(root, true).AddComponent(Unpooled.EmptyBuffer); break;
            case 11: owner = b = Unpooled.WrappedUnmodifiableBuffer(root, Unpooled.WrappedBuffer(new byte[2])); break;
            case 13: b = Unpooled.UnreleasableBuffer(root.AsReadOnly()); break;
        }
        try
        {
            byte first = b.GetByte(0); byte[] extracted = ByteBufUtil.GetBytes(b, 0, b.Capacity, false);
            Assert.Equal(shared, ReferenceEquals(storage, extracted)); Assert.Equal(b.Capacity, extracted.Length);
            extracted[0] ^= 128;
            Assert.Equal(shared ? extracted[0] : first, b.GetByte(0));
            if (shared) { b.SetByte(0, 42); Assert.Equal(42, extracted[0]); }
            byte[] copy = ByteBufUtil.GetBytes(b, 0, b.Capacity, true);
            Assert.NotSame(extracted, copy); Assert.Equal(extracted.Length, copy.Length);
            byte before = b.GetByte(0); copy[0] ^= 64; Assert.Equal(before, b.GetByte(0));
            byte[] defaultCopy = ByteBufUtil.GetBytes(b);
            Assert.NotSame(extracted, defaultCopy); defaultCopy[0] ^= 32; Assert.Equal(before, b.GetByte(0));
            Assert.Equal(1, owner.ReferenceCount);
        }
        finally { owner.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void EmptyBoundsAndReleasedRequestsAreAlwaysChecked(int kind)
    {
        ByteBuf b = Owner(kind, new byte[8]);
        foreach (bool copy in new[] { true, false })
        {
            Assert.Same(Array.Empty<byte>(), ByteBufUtil.GetBytes(b, 8, 0, copy));
            foreach (var (index, length) in new[] { (-1, 0), (0, -1), (9, 0), (8, 1), (1, 8), (0, int.MaxValue), (int.MaxValue, 0) })
                Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.GetBytes(b, index, length, copy));
        }
        b.Release();
        Assert.Throws<IllegalReferenceCountException>(() => ByteBufUtil.GetBytes(b));
        Assert.Throws<IllegalReferenceCountException>(() => ByteBufUtil.GetBytes(b, 0, 0));
        Assert.Throws<IllegalReferenceCountException>(() => ByteBufUtil.GetBytes(b, 0, 0, false));
    }

    [Fact]
    public void SharedUnpooledArraySurvivesReleaseAndDetachesOnResize()
    {
        ByteBuf b = Unpooled.Buffer(4, 8).WriteInt(0x01020304);
        byte[] shared = ByteBufUtil.GetBytes(b, 0, 4, false), snapshot = ByteBufUtil.GetBytes(b);
        b.Capacity = 8; b.SetByte(0, 42); Assert.Equal(1, shared[0]); Assert.Equal(1, snapshot[0]);
        shared[1] = 99; Assert.Equal(2, b.GetByte(1));
        b.Release(); Assert.Equal(new byte[] { 1, 99, 3, 4 }, shared); Assert.Equal(new byte[] { 1, 2, 3, 4 }, snapshot);
    }

    [Fact]
    public void NullAndSharedEmptyBufferContracts()
    {
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.GetBytes(null));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.GetBytes(null, 0, 0));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.GetBytes(null, 0, 0, false));
        Assert.Same(Array.Empty<byte>(), ByteBufUtil.GetBytes(Unpooled.EmptyBuffer));
        Assert.Same(Array.Empty<byte>(), ByteBufUtil.GetBytes(Unpooled.EmptyBuffer, 0, 0, false));
        Assert.Equal(1, Unpooled.EmptyBuffer.ReferenceCount);
    }
}
