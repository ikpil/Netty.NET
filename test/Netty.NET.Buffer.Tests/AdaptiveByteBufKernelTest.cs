/*
 * Copyright 2022 The Netty Project
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
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public partial class AdaptiveByteBufKernelTest
{
    private sealed class KernelFailure : Exception { }
    private sealed class Observation
    {
        public bool Enabled, Fail = true;
        public int? IterationResult;
        public readonly KernelFailure Failure = new();
        public readonly List<(string Method, int Index, int Width, object Peer)> Calls = [];
        public void Visit(string method, int index, int width, object peer = null)
        {
            if (!Enabled) return;
            Calls.Add((method, index, width, peer));
            if (Fail) throw Failure;
        }
    }
    private sealed class HeapProbe(Observation o, int size, int max) : UnpooledHeapByteBuf(size, max)
    {
        public override bool IsContiguous => false;
        public override byte GetByte(int index) { o.Visit("PublicByte", index, 1); return base.GetByte(index); }
        internal override byte ReadByteCore(int index) { o.Visit("Byte", index, 1); return base.ReadByteCore(index); }
        internal override void WriteByteCore(int index, int value) { o.Visit("SetByte", index, 1); base.WriteByteCore(index, value); }
        internal override ulong ReadWord(int index, int width, bool little) { o.Visit("Word", index, width); return base.ReadWord(index, width, little); }
        internal override ByteBuf SetWord(int index, ulong value, int width, bool little) { o.Visit("SetWord", index, width); return base.SetWord(index, value, width, little); }
        public override ByteBuf GetBytes(int index, Span<byte> destination)
        { o.Visit("Span", index, destination.Length); return base.GetBytes(index, destination); }
        public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
        { o.Visit("Buffer", index, length, destination); return base.GetBytes(index, destination, destinationIndex, length); }
        public override int ForEachByte(int index, int length, Func<byte, bool> processor)
        { o.Visit("Asc", index, length, processor); return o.IterationResult ?? base.ForEachByte(index, length, processor); }
        public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
        { o.Visit("Desc", index, length, processor); return o.IterationResult ?? base.ForEachByteDesc(index, length, processor); }
        public override ByteBuf SetZero(int index, int length)
        { o.Visit("Zero", index, length); return base.SetZero(index, length); }
    }
    private sealed class NativeProbe(Observation o, int size, int max, NativeMemoryAllocator native) : UnpooledDirectByteBuf(size, max, native)
    {
        public override bool IsContiguous => false;
        public override byte GetByte(int index) { o.Visit("PublicByte", index, 1); return base.GetByte(index); }
        internal override byte ReadByteCore(int index) { o.Visit("Byte", index, 1); return base.ReadByteCore(index); }
        internal override void WriteByteCore(int index, int value) { o.Visit("SetByte", index, 1); base.WriteByteCore(index, value); }
        internal override ulong ReadWord(int index, int width, bool little) { o.Visit("Word", index, width); return base.ReadWord(index, width, little); }
        internal override ByteBuf SetWord(int index, ulong value, int width, bool little) { o.Visit("SetWord", index, width); return base.SetWord(index, value, width, little); }
        public override ByteBuf GetBytes(int index, Span<byte> destination)
        { o.Visit("Span", index, destination.Length); return base.GetBytes(index, destination); }
        public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
        { o.Visit("Buffer", index, length, destination); return base.GetBytes(index, destination, destinationIndex, length); }
        public override int ForEachByte(int index, int length, Func<byte, bool> processor)
        { o.Visit("Asc", index, length, processor); return o.IterationResult ?? base.ForEachByte(index, length, processor); }
        public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
        { o.Visit("Desc", index, length, processor); return o.IterationResult ?? base.ForEachByteDesc(index, length, processor); }
        public override ByteBuf SetZero(int index, int length)
        { o.Visit("Zero", index, length); return base.SetZero(index, length); }
    }
    private sealed class Storage : IDisposable
    {
        public readonly Observation Observation = new();
        public readonly AdaptiveBufferTestAllocator Allocator;
        public readonly ByteBuf Guard;
        public readonly AdaptiveByteBuf Buffer;
        public readonly List<ByteBuf> Raw = [];
        public Storage(bool direct, int profile)
        {
            Allocator = new AdaptiveBufferTestAllocator(profile == 4, profile == 1);
            Allocator.StorageFactory = (_, size, max) =>
            {
                ByteBuf root = direct ? new NativeProbe(Observation, size, max, Allocator.Native) : new HeapProbe(Observation, size, max);
                byte[] bytes = new byte[size]; for (int i = 0; i < bytes.Length; ++i) bytes[i] = (byte)(i % 251);
                root.SetBytes(0, bytes.AsSpan()); Raw.Add(root); return root;
            };
            int size = profile is 2 or 4 ? 17000 : profile == 3 ? 1048577 : profile == 1 ? 513 : 13;
            int max = profile == 3 ? 1048640 : 65536;
            Guard = direct ? Allocator.DirectBuffer(size, max) : Allocator.HeapBuffer(size, max);
            Buffer = (AdaptiveByteBuf)(direct ? Allocator.DirectBuffer(size, max) : Allocator.HeapBuffer(size, max));
        }
        public void Dispose()
        {
            Observation.Enabled = false;
            if (Buffer.ReferenceCount > 0) Buffer.Release(); if (Guard.ReferenceCount > 0) Guard.Release();
            Allocator.Dispose(); Assert.Equal(0, Allocator.Native.ReservedBytes);
        }
    }
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool direct in new[] { false, true })
        for (int profile = 0; profile < 5; ++profile)
        for (int operation = 0; operation < 14; ++operation) yield return [direct, profile, operation];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void RootKernelsReceiveCapturedCoordinatesAndRelativeFailureDoesNotPublishIndices(bool direct, int profile, int operation)
    {
        using var s = new Storage(direct, profile); ByteBuf b = s.Buffer;
        int offset = s.Buffer.Allocation.Offset;
        ByteBuf peer = new UnpooledHeapByteBuf(8); Func<byte, bool> visitor = _ => true;
        if (operation is 9 or 13) b.SetIndex(1, 3);
        else if (operation >= 10) b.SetIndex(3, 7);
        else b.SetIndex(1, 2);
        int reader = b.ReaderIndex, writer = b.WriterIndex; b.MarkReaderIndex().MarkWriterIndex();
        s.Observation.Enabled = true;
        try
        {
            void Request()
            {
                switch (operation)
                {
                    case 0: b.GetByte(3); break; case 1: b.GetLongLE(3); break;
                    case 2: b.SetByte(3, 17); break; case 3: b.SetIntLE(3, 17); break;
                    case 4: b.GetBytes(3, new byte[4].AsSpan()); break; case 5: b.GetBytes(3, peer, 1, 4); break;
                    case 6: b.ForEachByte(3, 4, visitor); break; case 7: b.ForEachByteDesc(3, 4, visitor); break;
                    case 8: b.SetZero(3, 4); break; case 9: b.WriteZero(4); break;
                    case 10: b.ReadBytes(new byte[4].AsSpan()); break; case 11: b.ReadBytes(peer, 1, 4); break;
                    case 12: b.ReadByte(); break; case 13: b.WriteLongLE(17); break;
                }
            }
            Assert.Same(s.Observation.Failure, Assert.Throws<KernelFailure>(() => Request()));
            var call = Assert.Single(s.Observation.Calls);
            string expected = operation switch { 0 or 12 => "Byte", 1 => "Word", 2 => "SetByte", 3 or 13 => "SetWord", 4 or 10 => "Span", 5 or 11 => "Buffer", 6 => "Asc", 7 => "Desc", _ => "Zero" };
            Assert.Equal(expected, call.Method); Assert.Equal(offset + 3, call.Index);
            Assert.Equal(operation is 0 or 2 or 12 ? 1 : operation is 1 or 13 ? 8 : 4, call.Width);
            if (operation is 5 or 11) Assert.Same(peer, call.Peer);
            if (operation is 6 or 7) Assert.Same(visitor, call.Peer);
            Assert.Equal(reader, b.ReaderIndex); Assert.Equal(writer, b.WriterIndex);
            Assert.Equal(0, peer.ReaderIndex); Assert.Equal(0, peer.WriterIndex);
            Assert.Equal(1, b.ReferenceCount); Assert.Equal(1, s.Guard.ReferenceCount);
            Assert.Equal(1, s.Raw[^1].ReferenceCount);
        }
        finally { s.Observation.Enabled = false; peer.Release(); }
    }
}
