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
using System.Linq;
using System.Buffers;
using Netty.NET.Common;
using Netty.NET.Buffer.Search;

namespace Netty.NET.Buffer.Tests;

public class CompositeByteBufIterationKernelTest
{
    private sealed class Observation { public int Gets, Iterations; }
    private sealed class HeapProbe(Observation observation) : UnpooledHeapByteBuf(32, 64)
    {
        public override byte GetByte(int index)
        {
            ++observation.Gets;
            return (byte)(base.GetByte(index) ^ 0x80);
        }

        public override int ForEachByte(int index, int length, Func<byte, bool> processor)
        {
            ++observation.Iterations;
            throw new InvalidOperationException("component public iteration");
        }

        public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
        {
            ++observation.Iterations;
            throw new InvalidOperationException("component public iteration");
        }
    }
    private sealed class NativeProbe(Observation observation, NativeMemoryAllocator native) : UnpooledDirectByteBuf(32, 64, native)
    {
        public override byte GetByte(int index)
        {
            ++observation.Gets;
            return (byte)(base.GetByte(index) ^ 0x80);
        }

        public override int ForEachByte(int index, int length, Func<byte, bool> processor)
        {
            ++observation.Iterations;
            throw new InvalidOperationException("component public iteration");
        }

        public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
        {
            ++observation.Iterations;
            throw new InvalidOperationException("component public iteration");
        }
    }
    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new();
        public readonly Observation Observation = new();
        public readonly ByteBuf First, Second, Owner, View, Retained;
        public readonly int Offset;
        public readonly bool Fixed;
        public Storage(bool direct, int form)
        {
            First = direct ? new NativeProbe(Observation, Native) : new HeapProbe(Observation);
            Second = direct ? new NativeProbe(Observation, Native) : new HeapProbe(Observation);
            for (int i = 0; i < 32; ++i)
            {
                First.WriteByte(i + 1);
                Second.WriteByte(i + 65);
            }
            First.SetIndex(5, 21);
            Second.SetIndex(3, 19);
            Fixed = form == 4;
            Owner = Fixed ? Unpooled.WrappedUnmodifiableBuffer(First, Second)
                : new CompositeByteBuf().AddComponent(First, true).AddComponent(Second, true);
            Offset = form is 1 or 5 ? 4 : 0;
            View = form switch
            {
                1 => Owner.Slice(4, 24),
                2 => Owner.Duplicate(),
                3 => Owner.AsReadOnly(),
                5 => PooledDerivedByteBuf.NewSlice(Owner, Owner, 4, 24),
                6 => PooledDerivedByteBuf.NewDuplicate(Owner, Owner, 0, 32),
                _ => Owner
            };
            if (form >= 5)
                Retained = View;
        }

        public byte[] Expected(int rootIndex, int length, bool logical)
        {
            return Enumerable.Range(rootIndex, length).Select(i => (byte)((i < 16 ? i + (Fixed ? 1 : 6)
                : i - 16 + (Fixed ? 65 : 68)) ^ (logical ? 0x80 : 0))).ToArray();
        }

        public void Dispose()
        {
            if (Retained?.RefCnt > 0)
                Retained.Release();
            if (Owner.RefCnt > 0)
                Owner.Release();
            if (First.RefCnt > 0)
                First.Release();
            if (Second.RefCnt > 0)
                Second.Release();
            Assert.Equal(0, Native.ReservedBytes);
        }
    }
    public static IEnumerable<object[]> Iterations()
    {
        foreach (bool direct in new[] { false, true })
        for (int form = 0; form < 7; ++form)
        foreach (bool descending in new[] { false, true })
        foreach (int rootIndex in new[] { 8, 14, 20 })
        for (int mode = 0; mode < 3; ++mode)
                        {
                            yield return [direct, form, descending, rootIndex, mode];
                        }
    }

    [Theory]
    [MemberData(nameof(Iterations))]
    public void IterationPreservesStorageDispatchCoordinatesAndEarlyStop(bool direct, int form, bool descending, int rootIndex, int mode)
    {
        using var s = new Storage(direct, form);
        int index = rootIndex - s.Offset;
        s.View.SetIndex(index, index + 5).MarkReaderIndex().MarkWriterIndex();
        var visited = new List<byte>();
        Func<byte, bool> processor = value =>
        {
            visited.Add(value);
            return mode != 2 || visited.Count < 3;
        };
        int result = mode == 0 ? descending ? s.View.ForEachByteDesc(processor) : s.View.ForEachByte(processor)
            : descending ? s.View.ForEachByteDesc(index, 5, processor) : s.View.ForEachByte(index, 5, processor);
        Assert.Equal(mode == 2 ? index + 2 : -1, result);
        bool logical = s.Fixed || mode == 0 && form != 0;
        IEnumerable<byte> expected = s.Expected(rootIndex, 5, logical);
        if (descending)
            expected = expected.Reverse();
        Assert.Equal(expected.Take(mode == 2 ? 3 : 5), visited);
        Assert.Equal(logical ? visited.Count : 0, s.Observation.Gets);
        Assert.Equal(0, s.Observation.Iterations);
        Assert.Equal(index, s.View.ReaderIndex);
        Assert.Equal(index + 5, s.View.WriterIndex);
        s.View.SetIndex(0, s.View.Capacity).ResetWriterIndex().ResetReaderIndex();
        Assert.Equal(index, s.View.ReaderIndex);
        Assert.Equal(index + 5, s.View.WriterIndex);
        Assert.Equal(5, s.First.ReaderIndex);
        Assert.Equal(21, s.First.WriterIndex);
        Assert.Equal(3, s.Second.ReaderIndex);
        Assert.Equal(19, s.Second.WriterIndex);
    }

    public static IEnumerable<object[]> Searches()
    {
        foreach (bool direct in new[] { false, true })
        for (int form = 0; form < 7; ++form)
        foreach (bool descending in new[] { false, true })
        foreach (int rootIndex in new[] { 8, 14, 20 })
                    {
                        yield return [direct, form, descending, rootIndex];
                    }
    }

    [Theory]
    [MemberData(nameof(Searches))]
    public void ByteAndPatternSearchUseLogicalScalarBytes(bool direct, int form, bool descending, int rootIndex)
    {
        using var s = new Storage(direct, form);
        int index = rootIndex - s.Offset;
        byte[] logical = s.Expected(rootIndex, 5, true);
        s.View.SetIndex(index, index + 5).MarkReaderIndex().MarkWriterIndex();
        Assert.Equal(index + 2, descending ? s.View.IndexOf(index + 5, index, logical[2]) : s.View.IndexOf(index, index + 5, logical[2]));
        Assert.Equal(-1, descending ? s.View.IndexOf(index + 5, index, 0xFF) : s.View.IndexOf(index, index + 5, 0xFF));
        Assert.Equal(2, s.View.BytesBefore(logical[2]));
        Assert.Equal(2, s.View.BytesBefore(5, logical[2]));
        Assert.Equal(2, s.View.BytesBefore(index, 5, logical[2]));
        ByteBuf needle = Unpooled.WrappedBuffer(new byte[] { 0, logical[1], logical[2], 0 }).SetIndex(1, 3);
        try
        {
            Assert.Equal(index + 1, ByteBufUtil.IndexOf(needle, s.View));
            Assert.Equal(1, needle.ReaderIndex);
            Assert.Equal(3, needle.WriterIndex);
        }
        finally
        {
            needle.Release();
        }
        Assert.Equal(index, s.View.ReaderIndex);
        Assert.Equal(index + 5, s.View.WriterIndex);
        Assert.True(s.Observation.Gets > 0);
        Assert.Equal(0, s.Observation.Iterations);
    }

    private sealed class Transparent(ByteBuf buffer) : WrappedByteBuf(buffer);
    public static IEnumerable<object[]> NestedCases()
    {
        foreach (bool direct in new[] { false, true })
        for (int form = 0; form < 8; ++form)
        foreach (bool descending in new[] { false, true })
        for (int mode = 0; mode < 3; ++mode)
                    {
                        yield return [direct, form, descending, mode];
                    }
    }

    [Theory]
    [MemberData(nameof(NestedCases))]
    public void NestedComponentIterationPeelsTransparentViewsButKeepsReadOnlyStorageLoops(bool direct, int form, bool descending, int mode)
    {
        using var s = new Storage(direct, 0);
        ByteBuf source = form switch
        {
            1 => s.Owner.Slice(4, 24), 2 => s.Owner.Duplicate(), 3 => s.Owner.AsReadOnly(),
            4 => PooledDerivedByteBuf.NewSlice(s.Owner, s.Owner, 4, 24),
            5 => PooledDerivedByteBuf.NewDuplicate(s.Owner, s.Owner, 0, 32),
            6 => new Transparent(s.Owner), 7 => new WrappedCompositeByteBuf((CompositeByteBuf)s.Owner), _ => s.Owner
        };
        int adjustment = form is 1 or 4 ? 4 : 0;
        source.SetIndex(6 - adjustment, 26 - adjustment);
        if (form is not 4 and not 5)
        {
            source.Retain();
        }
        var outer = new CompositeByteBuf();
        outer.AddComponent(new UnpooledHeapByteBuf(0), true).AddComponent(source, true)
            .AddComponent(new UnpooledHeapByteBuf(0), true);
        try
        {
            outer.SetIndex(2, 18).MarkReaderIndex().MarkWriterIndex();
            var seen = new List<byte>();
            var failure = new InvalidOperationException("processor failure");
            Func<byte, bool> processor = value =>
            {
                seen.Add(value);
                if (mode == 2 && seen.Count == 4)
                {
                    throw failure;
                }
                return mode != 1 || seen.Count < 4;
            };

            int Visit()
            {
                return descending ? outer.ForEachByteDesc(processor) : outer.ForEachByte(processor);
            }

            if (mode == 2)
            {
                Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => Visit()));
            }
            else
            {
                Assert.Equal(mode == 0 ? -1 : descending ? 14 : 5, Visit());
            }
            IEnumerable<byte> expected = s.Expected(8, 16, form == 3);
            if (descending)
            {
                expected = expected.Reverse();
            }
            Assert.Equal(expected.Take(mode == 0 ? 16 : 4), seen);
            Assert.Equal(form == 3 ? seen.Count : 0, s.Observation.Gets);
            Assert.Equal(0, s.Observation.Iterations);
            Assert.Equal(2, outer.ReaderIndex);
            Assert.Equal(18, outer.WriterIndex);
            Assert.Equal(6 - adjustment, source.ReaderIndex);
            Assert.Equal(26 - adjustment, source.WriterIndex);
            Assert.Equal(2, s.Owner.RefCnt);
        }
        finally
        {
            outer.Release();
        }
        Assert.Equal(1, s.Owner.RefCnt);
    }

    public static IEnumerable<object[]> CallbackCases()
    {
        foreach (bool direct in new[] { false, true })
        foreach (bool fixedComposite in new[] { false, true })
        foreach (bool descending in new[] { false, true })
        for (int action = 0; action < 4; ++action)
                    {
                        yield return [direct, fixedComposite, descending, action];
                    }
    }

    [Theory]
    [MemberData(nameof(CallbackCases))]
    public void ProcessorFailureResizeAndReleasePreserveLifetimeAndCursorRules(bool direct, bool fixedComposite, bool descending, int action)
    {
        using var s = new Storage(direct, fixedComposite ? 4 : 0);
        s.Owner.SetIndex(2, 6).MarkReaderIndex().MarkWriterIndex();
        var failure = new InvalidOperationException("processor failure");
        var seen = new List<byte>();
        Func<byte, bool> processor = value =>
        {
            seen.Add(value);
            if (action == 0 && seen.Count == 2)
                throw failure;
            if (seen.Count == 1)
            {
                if (action == 1)
                {
                    s.First.Capacity = 64;
                    int next = (descending ? 4 : 3) + (fixedComposite ? 0 : 5);
                    s.First.SetByte(next, 0x33);
                }
                else if (action >= 2)
                    s.Owner.Release();
            }
            return action != 2 && (action != 1 || seen.Count < 2);
        };

        int Visit()
        {
            return descending ? s.Owner.ForEachByteDesc(processor) : s.Owner.ForEachByte(processor);
        }

        if (action == 0)
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => Visit()));
        else if (action == 3)
            Assert.Throws<IllegalReferenceCountException>(() => Visit());
        else
            Assert.Equal(action == 2 ? descending ? 5 : 2 : descending ? 4 : 3, Visit());
        Assert.Equal(action >= 2 ? 1 : 2, seen.Count);
        Assert.Equal(s.Expected(descending ? 5 : 2, 1, fixedComposite)[0], seen[0]);
        if (action == 1)
            Assert.Equal((byte)(0x33 ^ (fixedComposite ? 0x80 : 0)), seen[1]);
        if (action < 2)
        {
            Assert.Equal(2, s.Owner.ReaderIndex);
            Assert.Equal(6, s.Owner.WriterIndex);
        }
        else
        {
            Assert.Equal(0, s.Owner.RefCnt);
            Assert.Equal(0, s.Native.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> SearchSessions()
    {
        foreach (bool direct in new[] { false, true })
        foreach (bool fixedComposite in new[] { false, true })
        for (int algorithm = 0; algorithm < 3; ++algorithm)
                {
                    yield return [direct, fixedComposite, algorithm];
                }
    }

    [Theory]
    [MemberData(nameof(SearchSessions))]
    public void StatefulProcessorsContinueAcrossComponentBoundaries(bool direct, bool fixedComposite, int algorithm)
    {
        using var s = new Storage(direct, fixedComposite ? 4 : 0);
        byte[] pattern = s.Expected(14, 4, fixedComposite);
        ISearchProcessor processor = algorithm switch
        {
            0 => AbstractSearchProcessorFactory.NewKmpSearchProcessorFactory(pattern).NewSearchProcessor(),
            1 => AbstractSearchProcessorFactory.NewBitapSearchProcessorFactory(pattern).NewSearchProcessor(),
            _ => AbstractMultiSearchProcessorFactory.NewAhoCorasicSearchProcessorFactory(pattern).NewSearchProcessor()
        };
        Assert.Equal(-1, s.Owner.ForEachByte(12, 4, processor.Process));
        Assert.Equal(17, s.Owner.ForEachByte(16, 4, processor.Process));
        processor.Reset();
        Assert.Equal(17, s.Owner.ForEachByte(12, 8, processor.Process));
        Assert.Equal(0, s.Owner.ReaderIndex);
        Assert.Equal(32, s.Owner.WriterIndex);
    }

    public static IEnumerable<object[]> PooledSegments()
    {
        foreach (bool direct in new[] { false, true })
        foreach (bool fixedComposite in new[] { false, true })
        foreach (bool slice in new[] { false, true })
        for (int operation = 0; operation < 6; ++operation)
                    {
                        yield return [direct, fixedComposite, slice, operation];
                    }
    }

    [Theory]
    [MemberData(nameof(PooledSegments))]
    public void IndependentlyCountedViewsKeepSegmentedBorrowWriteAndZeroContracts(bool direct, bool fixedComposite, bool slice, int operation)
    {
        using var s = new Storage(direct, fixedComposite ? 4 : 0);
        ByteBuf view = slice ? PooledDerivedByteBuf.NewSlice(s.Owner, s.Owner, 4, 24)
            : PooledDerivedByteBuf.NewDuplicate(s.Owner, s.Owner, 0, 32);
        int index = slice ? 10 : 14;
        view.SetIndex(1, operation >= 4 ? index : 2).MarkReaderIndex().MarkWriterIndex();
        byte[] first = s.First.AsReadOnlyMemory(0, 32).ToArray(), second = s.Second.AsReadOnlyMemory(0, 32).ToArray();
        byte[] input = [91, 92, 93, 94];
        try
        {
            void Request()
            {
                switch (operation)
                {
                    case 0:
                        Assert.False(view.TryGetReadOnlyMemory(index, 4, out _));
                        break;
                    case 1:
                        Assert.False(view.TryGetMemory(index, 4, out _));
                        break;
                    case 2:
                        view.SetBytes(index, input.AsSpan());
                        break;
                    case 3:
                        view.SetZero(index, 4);
                        break;
                    case 4:
                        view.WriteBytes(input.AsSpan());
                        break;
                    case 5:
                        view.WriteZero(4);
                        break;
                }
            }

            if (fixedComposite && operation != 0)
            {
                Assert.Throws<NotSupportedException>(() => Request());
            }
            else
            {
                Request();
                if (operation >= 2)
                {
                    bool zero = operation is 3 or 5;
                    first[19] = zero ? (byte)0 : input[0];
                    first[20] = zero ? (byte)0 : input[1];
                    second[3] = zero ? (byte)0 : input[2];
                    second[4] = zero ? (byte)0 : input[3];
                }
            }
            Assert.Equal(first, s.First.AsReadOnlyMemory(0, 32).ToArray());
            Assert.Equal(second, s.Second.AsReadOnlyMemory(0, 32).ToArray());
            Assert.Equal(1, view.ReaderIndex);
            Assert.Equal(operation >= 4 ? index + (fixedComposite ? 0 : 4) : 2, view.WriterIndex);
            Assert.Equal(2, s.Owner.RefCnt);
            Assert.Equal(1, view.RefCnt);
        }
        finally
        {
            view.Release();
        }
        Assert.Equal(1, s.Owner.RefCnt);
    }

    public static IEnumerable<object[]> PatternPairs()
    {
        foreach (bool direct in new[] { false, true })
        for (int needle = 0; needle < 7; ++needle)
        for (int haystack = 0; haystack < 7; ++haystack)
                {
                    yield return [direct, needle, haystack];
                }
    }

    private static void PutLogical(Storage storage, int index, byte value)
    {
        ByteBuf component = index < 16 ? storage.First : storage.Second;
        int local = index < 16 ? index + (storage.Fixed ? 0 : 5) : index - 16 + (storage.Fixed ? 0 : 3);
        component.SetByte(local, value ^ 0x80);
    }

    [Theory]
    [MemberData(nameof(PatternPairs))]
    public void SegmentedNeedlesAndHaystacksUseLogicalBytesAndIndependentReadableRanges(bool direct, int needleForm, int haystackForm)
    {
        using var needle = new Storage(direct, needleForm);
        using var haystack = new Storage(direct, haystackForm);
        for (int i = 0; i < 4; ++i)
        {
            PutLogical(needle, 14 + i, (byte)(0xC1 + i));
            PutLogical(haystack, 14 + i, (byte)(0xC1 + i));
        }
        needle.View.SetIndex(14 - needle.Offset, 18 - needle.Offset);
        haystack.View.SetIndex(8 - haystack.Offset, 24 - haystack.Offset);
        Assert.Equal(14 - haystack.Offset, ByteBufUtil.IndexOf(needle.View, haystack.View));
        PutLogical(needle, 14, 0xEE);
        Assert.Equal(-1, ByteBufUtil.IndexOf(needle.View, haystack.View));
        Assert.Equal(14 - needle.Offset, needle.View.ReaderIndex);
        Assert.Equal(18 - needle.Offset, needle.View.WriterIndex);
        Assert.Equal(8 - haystack.Offset, haystack.View.ReaderIndex);
        Assert.Equal(24 - haystack.Offset, haystack.View.WriterIndex);
    }

    private sealed class HighCoordinateStorage : AbstractReferenceCountedByteBuf
    {
        private readonly byte[] _bytes = [1, 2, 3, 4, 5, 6, 7, 8];
        public const int Start = int.MaxValue - 8;
        public HighCoordinateStorage() : base(int.MaxValue)
        {
            SetIndex(Start, int.MaxValue);
        }
        public override int Capacity { get => int.MaxValue; set => throw new NotSupportedException(); }
        public override bool IsDirect => false;
        protected override Memory<byte> GetMemoryCore(int index, int length)
        {
            return _bytes.AsMemory(index - Start, length);
        }

        protected override void Deallocate()
        {
        }
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StopIndicesTranslateFromHighComponentCoordinatesWithoutIntermediateOverflow(bool descending, bool last)
    {
        ByteBuf source = new HighCoordinateStorage();
        var composite = new CompositeByteBuf().AddComponent(new UnpooledHeapByteBuf(32).SetIndex(0, 32), true)
            .AddComponent(source, true);
        try
        {
            byte stop = last ? descending ? (byte)1 : (byte)8 : (byte)4;
            int result = descending ? composite.ForEachByteDesc(32, 8, value => value != stop)
                : composite.ForEachByte(32, 8, value => value != stop);
            Assert.Equal(32 + stop - 1, result);
            Assert.Equal(HighCoordinateStorage.Start, source.ReaderIndex);
            Assert.Equal(int.MaxValue, source.WriterIndex);
            Assert.Equal(0, composite.ReaderIndex);
            Assert.Equal(40, composite.WriterIndex);
        }
        finally
        {
            composite.Release();
        }
        Assert.Equal(0, source.RefCnt);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void ReleasedCompositeCannotKeepScanningAnExternallyRetainedComponent(bool direct, bool descending, bool last)
    {
        using var s = new Storage(direct, 0);
        s.First.Retain();
        int calls = 0;
        Func<byte, bool> processor = _ => {
            if (++calls == (last ? 4 : 1))
            {
                s.Owner.Release();
            }
            return true; };

        int Visit()
        {
            return descending ? s.Owner.ForEachByteDesc(2, 4, processor) : s.Owner.ForEachByte(2, 4, processor);
        }

        if (last)
            Assert.Equal(-1, Visit());
        else
            Assert.Throws<IllegalReferenceCountException>(() => Visit());
        Assert.Equal(last ? 4 : 1, calls);
        Assert.Equal(0, s.Owner.RefCnt);
        Assert.Equal(1, s.First.RefCnt);
        Assert.Equal(0, s.Second.RefCnt);
    }

    public static IEnumerable<object[]> HighCoordinateOperations()
    {
        for (int operation = 0; operation < 17; ++operation)
            yield return [operation];
    }

    [Theory]
    [MemberData(nameof(HighCoordinateOperations))]
    public void CompositeOperationsTranslateHighSourceCoordinatesBeforeAddingOffsets(int operation)
    {
        ByteBuf source = new HighCoordinateStorage();
        var composite = new CompositeByteBuf().AddComponent(new UnpooledHeapByteBuf(32).SetIndex(0, 32), true)
            .AddComponent(source, true);
        byte[] expected = [1, 2, 3, 4, 5, 6, 7, 8];
        byte[] input = [91, 92, 93, 94];
        ByteBuf peer = Unpooled.WrappedBuffer(new byte[8]);
        try
        {
            switch (operation)
            {
                case 0:
                    Assert.Equal(4, composite.GetByte(35));
                    break;
                case 1:
                    Assert.Equal(0x0102, composite.GetShort(32));
                    break;
                case 2:
                    Assert.Equal(0x01020304, composite.GetInt(32));
                    break;
                case 3:
                    Assert.Equal(0x0102030405060708L, composite.GetLong(32));
                    break;
                case 4:
                    Assert.Equal(expected, composite.AsReadOnlyMemory(32, 8).ToArray());
                    break;
                case 5:
                    Assert.Equal(expected, composite.AsReadOnlySequence(32, 8).ToArray());
                    break;
                case 6:
                    byte[] result = new byte[8];
                    composite.GetBytes(32, result.AsSpan());
                    Assert.Equal(expected, result);
                    break;
                case 7:
                    composite.GetBytes(32, peer, 0, 8);
                    Assert.Equal(expected, peer.AsReadOnlyMemory(0, 8).ToArray());
                    break;
                case 8:
                    composite.SetBytes(34, input.AsSpan());
                    input.CopyTo(expected, 2);
                    break;
                case 9:
                    peer.SetBytes(0, input.AsSpan());
                    composite.SetBytes(34, peer, 0, 4);
                    input.CopyTo(expected, 2);
                    break;
                case 10:
                    composite.SetZero(34, 4);
                    Array.Clear(expected, 2, 4);
                    break;
                case 11:
                    ByteBuf copy = composite.Copy(32, 8);
                    try
                    {
                        Assert.Equal(expected, copy.AsReadOnlyMemory(0, 8).ToArray());
                    }
                    finally
                    {
                        copy.Release();
                    }
                    break;
                case 12:
                    Assert.Equal(expected, composite.Decompose(32, 8)[0].AsReadOnlyMemory(0, 8).ToArray());
                    break;
                case 13:
                    Assert.Equal(expected, composite.ComponentSlice(1).AsReadOnlyMemory(0, 8).ToArray());
                    break;
                case 14:
                    composite.SetByte(35, 0xAB);
                    expected[3] = 0xAB;
                    break;
                case 15:
                    composite.SetLong(32, 0x0102030405060708L);
                    break;
                case 16:
                    composite.SetIndex(32, 40);
                    var flat = new CompositeByteBuf();
                    try
                    {
                        flat.AddFlattenedComponents(composite.Retain(), true);
                        Assert.Equal(expected, flat.AsReadOnlyMemory(0, 8).ToArray());
                    }
                    finally
                    {
                        flat.Release();
                        composite.ReaderIndex = 0;
                    }
                    break;
            }
            Assert.Equal(expected, source.AsReadOnlyMemory(HighCoordinateStorage.Start, 8).ToArray());
            Assert.Equal(HighCoordinateStorage.Start, source.ReaderIndex);
            Assert.Equal(int.MaxValue, source.WriterIndex);
            Assert.Equal(0, composite.ReaderIndex);
            Assert.Equal(40, composite.WriterIndex);
        }
        finally
        {
            peer.Release();
            composite.Release();
        }
        Assert.Equal(0, source.RefCnt);
    }
}
