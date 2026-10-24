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
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public partial class CompositeByteBufBulkKernelTest
{
    private sealed class ProbeFailure : Exception { }
    private sealed class Observation
    {
        public bool Fail = true;
        public int FailAt = 1;
        public readonly List<(string Method, int Index, int Length, ByteBuf Other)> Visits = [];
        public void Visit(string method, int index, int length, ByteBuf other = null)
        { Visits.Add((method, index, length, other)); if (Fail && Visits.Count >= FailAt) throw new ProbeFailure(); }
    }
    private sealed class HeapProbe : UnpooledHeapByteBuf
    {
        private readonly Observation _observation;
        public HeapProbe(Observation observation) : base(32) => _observation = observation;
        public override ByteBuf GetBytes(int index, Span<byte> destination)
        {
            _observation.Visit("GetSpan", index, destination.Length);
            base.GetBytes(index, destination);
            for (int i = 0; i < destination.Length; ++i) destination[i] ^= 0x5A;
            return this;
        }
        public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
        {
            _observation.Visit("GetBuffer", index, length, destination);
            byte[] bytes = AsReadOnlyMemory(index, length).ToArray();
            for (int i = 0; i < bytes.Length; ++i) bytes[i] ^= 0x5A;
            destination.SetBytes(destinationIndex, bytes); return this;
        }
        public override ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
        { _observation.Visit("SetBuffer", index, length, source); return base.SetBytes(index, source, sourceIndex, length); }
    }
    private sealed class NativeProbe : UnpooledDirectByteBuf
    {
        private readonly Observation _observation;
        public NativeProbe(Observation observation, NativeMemoryAllocator native) : base(32, 32, native) => _observation = observation;
        public override ByteBuf GetBytes(int index, Span<byte> destination)
        {
            _observation.Visit("GetSpan", index, destination.Length);
            base.GetBytes(index, destination);
            for (int i = 0; i < destination.Length; ++i) destination[i] ^= 0x5A;
            return this;
        }
        public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
        {
            _observation.Visit("GetBuffer", index, length, destination);
            byte[] bytes = AsReadOnlyMemory(index, length).ToArray();
            for (int i = 0; i < bytes.Length; ++i) bytes[i] ^= 0x5A;
            destination.SetBytes(destinationIndex, bytes); return this;
        }
        public override ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
        { _observation.Visit("SetBuffer", index, length, source); return base.SetBytes(index, source, sourceIndex, length); }
    }
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool direct in new[] { false, true })
        for (int form = 0; form < 7; ++form)
        for (int operation = 0; operation < 9; ++operation)
        foreach (bool boundary in new[] { false, true }) yield return [direct, form, operation, boundary];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void BulkRequestsReachTheSelectedComponentOverload(bool direct, int form, int operation, bool boundary)
    {
        var native = new NativeMemoryAllocator(); var observation = new Observation();
        ByteBuf first = direct ? new NativeProbe(observation, native) : new HeapProbe(observation);
        ByteBuf second = direct ? new NativeProbe(observation, native) : new HeapProbe(observation);
        ByteBuf owner = null, retained = null;
        ByteBuf external = new UnpooledHeapByteBuf(32);
        try
        {
            byte[] a = new byte[32], b = new byte[32];
            for (int i = 0; i < 32; ++i) { a[i] = (byte)(i + 1); b[i] = (byte)(i + 33); }
            first.SetBytes(0, a).SetIndex(5, 21); second.SetBytes(0, b).SetIndex(3, 19);
            external.SetIndex(2, 12);
            owner = form == 4 ? Unpooled.WrappedUnmodifiableBuffer(first, second)
                : new CompositeByteBuf().AddComponent(first, true).AddComponent(second, true);
            ByteBuf caller = form switch
            {
                1 => owner.Slice(4, 24), 2 => owner.Duplicate(), 3 => owner.AsReadOnly(),
                5 => PooledDerivedByteBuf.NewSlice(owner, owner, 4, 24),
                6 => PooledDerivedByteBuf.NewDuplicate(owner, owner, 0, 32), _ => owner
            };
            if (form >= 5) retained = caller;
            int rootIndex = boundary ? 15 : 8, index = rootIndex - (form is 1 or 5 ? 4 : 0);
            bool relativeRead = operation is 4 or 5, relativeWrite = operation == 6;
            if (relativeRead) caller.SetIndex(index, index + 4);
            else if (relativeWrite) caller.SetIndex(1, index);
            else caller.SetIndex(1, 2);
            caller.MarkReaderIndex().MarkWriterIndex();
            int reader = caller.ReaderIndex, writer = caller.WriterIndex;
            Action request = () =>
            {
                switch (operation)
                {
                    case 0: caller.GetBytes(index, new byte[4]); break;
                    case 1: caller.GetBytes(index, external, 3, 4); break;
                    case 2: caller.SetBytes(index, external, 2, 4); break;
                    case 3: external.SetBytes(3, caller, index, 4); break;
                    case 4: caller.ReadBytes(new byte[4]); break;
                    case 5: caller.ReadBytes(external, 3, 4); break;
                    case 6: caller.WriteBytes(external, 2, 4); break;
                    case 7: external.WriteBytes(caller, index, 4); break;
                    case 8: caller.Copy(index, 4).Release(); break;
                }
            };
            if ((operation is 2 or 6) && caller.IsReadOnly)
            { Assert.Throws<NotSupportedException>(request); Assert.Empty(observation.Visits); }
            else
            {
                Assert.Throws<ProbeFailure>(request);
                var visit = Assert.Single(observation.Visits);
                string method = operation is 1 or 5 || operation == 8 && form != 4 ? "GetBuffer"
                    : operation is 2 or 6 ? "SetBuffer" : "GetSpan";
                Assert.Equal(method, visit.Method); Assert.Equal(rootIndex + (form == 4 ? 0 : 5), visit.Index);
                Assert.Equal(boundary ? 1 : 4, visit.Length);
                if (operation is 1 or 2 or 5 or 6) Assert.Same(external, visit.Other);
                else if (operation == 8 && form != 4) Assert.Equal(0, visit.Other.ReferenceCount);
                else Assert.Null(visit.Other);
            }
            Assert.Equal(reader, caller.ReaderIndex); Assert.Equal(writer, caller.WriterIndex);
            caller.SetIndex(0, caller.Capacity).ResetWriterIndex().ResetReaderIndex();
            Assert.Equal(reader, caller.ReaderIndex); Assert.Equal(writer, caller.WriterIndex);
            Assert.Equal(2, external.ReaderIndex); Assert.Equal(12, external.WriterIndex);
            Assert.Equal(a, first.AsReadOnlyMemory(0, 32).ToArray());
            Assert.Equal(b, second.AsReadOnlyMemory(0, 32).ToArray());
            Assert.Equal(form >= 5 ? 2 : 1, owner.ReferenceCount);
            Assert.Equal(1, first.ReferenceCount); Assert.Equal(1, second.ReferenceCount);
        }
        finally
        {
            if (retained?.ReferenceCount > 0) retained.Release();
            if (owner?.ReferenceCount > 0) owner.Release();
            if (first.ReferenceCount > 0) first.Release();
            if (second.ReferenceCount > 0) second.Release();
            external.Release(); Assert.Equal(0, native.ReservedBytes);
        }
    }
}
