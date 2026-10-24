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

public class CompositeByteBufStructuralDispatchTest
{
    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new();
        public readonly IByteBufAllocator Allocator;
        private readonly List<ByteBuf> _buffers = [];
        public Storage(bool pooled, bool direct)
            => Allocator = pooled
                ? new PooledByteBufAllocator(direct, maxOrder: 1, smallCacheSize: 0,
                    normalCacheSize: 0, nativeAllocator: Native)
                : new UnpooledByteBufAllocator(direct, Native);
        public T Own<T>(T buffer) where T : ByteBuf { _buffers.Add(buffer); return buffer; }
        public ByteBuf Data(params byte[] bytes)
            => Own(Allocator.Buffer(bytes.Length, bytes.Length).WriteBytes(bytes));
        public Trap Composite(bool direct, int maximum)
            => Own(new Trap(Allocator, direct, maximum));
        public void Dispose()
        {
            // Composites own their inserted sources; rejected inputs remain caller-owned.
            foreach (ByteBuf buffer in _buffers)
                if (buffer is CompositeByteBuf && buffer.ReferenceCount > 0) buffer.Release();
            foreach (ByteBuf buffer in _buffers)
            {
                if (buffer is Trap trap)
                    foreach (ByteBuf rejected in trap.RejectedInputs)
                        if (rejected.ReferenceCount > 0) rejected.Release();
                if (buffer.ReferenceCount > 0) buffer.Release();
                Assert.Equal(0, buffer.ReferenceCount);
            }
            (Allocator as IDisposable)?.Dispose();
            Assert.Equal(0, Native.ReservedBytes);
        }
    }

    private sealed class Trap : CompositeByteBuf
    {
        public bool RejectAdd, RejectAllConsolidation, RejectRangeConsolidation, RejectRemoval, RejectIndexedBatch, RejectArrayBatch;
        public readonly List<string> Calls = [];
        public readonly List<ByteBuf> RejectedInputs = [];
        public Trap(IByteBufAllocator allocator, bool direct, int maximum) : base(allocator, direct, maximum) { }
        private void Reject(string name) { Calls.Add(name); throw new InvalidOperationException(name); }
        public override CompositeByteBuf AddComponent(ByteBuf buffer, bool increaseWriterIndex = false)
        {
            if (RejectAdd) { RejectedInputs.Add(buffer); Reject("add"); }
            return base.AddComponent(buffer, increaseWriterIndex);
        }
        public override CompositeByteBuf AddComponents(int index, ByteBuf[] buffers, bool increaseWriterIndex = false)
        {
            if (RejectIndexedBatch) Reject("array-batch");
            return base.AddComponents(index, buffers, increaseWriterIndex);
        }
        public override CompositeByteBuf AddComponents(ByteBuf[] buffers, bool increaseWriterIndex)
        {
            if (RejectArrayBatch) Reject("array-forward");
            return base.AddComponents(buffers, increaseWriterIndex);
        }
        public override CompositeByteBuf AddComponents(int index, IEnumerable<ByteBuf> buffers, bool increaseWriterIndex = false)
        {
            if (RejectIndexedBatch) Reject("enumerable-batch");
            return base.AddComponents(index, buffers, increaseWriterIndex);
        }
        public override CompositeByteBuf Consolidate()
        {
            if (RejectAllConsolidation) Reject("consolidate");
            return base.Consolidate();
        }
        public override CompositeByteBuf Consolidate(int index, int count)
        {
            if (RejectRangeConsolidation) Reject("range-consolidate");
            return base.Consolidate(index, count);
        }
        public override CompositeByteBuf RemoveComponents(int index, int count)
        {
            if (RejectRemoval) Reject("remove");
            return base.RemoveComponents(index, count);
        }
    }

    private sealed class ConstructorTrap : CompositeByteBuf
    {
        public ConstructorTrap(IByteBufAllocator allocator, bool direct, ByteBuf[] buffers)
            : base(allocator, direct, 4, buffers) { }
        public ConstructorTrap(IByteBufAllocator allocator, bool direct, IEnumerable<ByteBuf> buffers)
            : base(allocator, direct, 4, buffers) { }
        public override CompositeByteBuf AddComponents(int index, IEnumerable<ByteBuf> buffers, bool increaseWriterIndex = false)
            => throw new InvalidOperationException("Virtual batch call before derived construction completed.");
    }

    public static IEnumerable<object[]> Providers()
    {
        foreach (bool pooled in new[] { false, true })
        foreach (bool direct in new[] { false, true }) yield return [pooled, direct];
    }
    public static IEnumerable<object[]> AutomaticCases()
    {
        foreach (object[] provider in Providers())
        for (int operation = 0; operation < 4; ++operation) yield return [provider[0], provider[1], operation];
    }
    public static IEnumerable<object[]> RemovalCases()
    {
        foreach (object[] provider in Providers())
        for (int operation = 0; operation < 3; ++operation) yield return [provider[0], provider[1], operation];
    }
    public static IEnumerable<object[]> BatchCases()
    {
        foreach (object[] provider in Providers())
        foreach (bool enumerable in new[] { false, true })
        foreach (bool increase in new[] { false, true }) yield return [provider[0], provider[1], enumerable, increase];
    }
    public static IEnumerable<object[]> ConstructorCases()
    {
        foreach (object[] provider in Providers())
        foreach (bool enumerable in new[] { false, true }) yield return [provider[0], provider[1], enumerable];
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void CapacityGrowthUsesPrivateInsertionAndAutomaticConsolidation(bool pooled, bool direct)
    {
        using var s = new Storage(pooled, direct);
        Trap buffer = s.Composite(direct, 2);
        ByteBuf first = s.Data(1, 2), second = s.Data(3, 4);
        buffer.AddComponents([first, second], true).SetIndex(1, 3).MarkReaderIndex().MarkWriterIndex();
        buffer.RejectAdd = buffer.RejectAllConsolidation = buffer.RejectRangeConsolidation = true;
        buffer.Capacity = 8;
        Assert.Equal(8, buffer.Capacity); Assert.Equal(1, buffer.NumComponents);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, ByteBufUtil.GetBytes(buffer, 0, 4));
        Assert.Equal(direct, buffer.IsDirect); Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, second.ReferenceCount);
        buffer.SetIndex(0, 8).ResetWriterIndex().ResetReaderIndex();
        Assert.Equal(1, buffer.ReaderIndex); Assert.Equal(3, buffer.WriterIndex);
        Assert.Empty(buffer.Calls);
    }

    [Theory]
    [MemberData(nameof(AutomaticCases))]
    public void AutomaticConsolidationUsesThePrivateKernel(bool pooled, bool direct, int operation)
    {
        using var s = new Storage(pooled, direct);
        Trap buffer = s.Composite(direct, 1);
        ByteBuf first = s.Data(1, 2), second = s.Data(3, 4);
        buffer.AddComponent(first, true);
        buffer.RejectAllConsolidation = buffer.RejectRangeConsolidation = true;
        if (operation == 0) buffer.AddComponent(second, true);
        else if (operation == 1) buffer.AddComponents([second], true);
        else if (operation == 2) buffer.AddComponents((IEnumerable<ByteBuf>)new[] { second }, true);
        else
        {
            CompositeByteBuf source = s.Own(s.Allocator.CompositeBuffer(2).AddComponent(second, true));
            buffer.AddFlattenedComponents(source, true);
            Assert.Equal(0, source.ReferenceCount);
        }
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, ByteBufUtil.GetBytes(buffer));
        Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(4, buffer.WriterIndex);
        Assert.Equal(1, buffer.NumComponents); Assert.Equal(direct, buffer.IsDirect);
        Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, second.ReferenceCount);
        Assert.Empty(buffer.Calls);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void WholeConsolidationDoesNotCallThePublicRangeOverload(bool pooled, bool direct)
    {
        using var s = new Storage(pooled, direct);
        Trap buffer = s.Composite(direct, 3);
        buffer.AddComponents([s.Data(1, 2), s.Data(3, 4)], true);
        buffer.RejectRangeConsolidation = true;
        Assert.Same(buffer, buffer.Consolidate());
        Assert.Equal(1, buffer.NumComponents); Assert.Equal(new byte[] { 1, 2, 3, 4 }, ByteBufUtil.GetBytes(buffer));
        Assert.Empty(buffer.Calls);
    }

    [Theory]
    [MemberData(nameof(RemovalCases))]
    public void SingleRemovalAndDiscardUsePrivateComponentRemoval(bool pooled, bool direct, int operation)
    {
        using var s = new Storage(pooled, direct);
        Trap buffer = s.Composite(direct, 4);
        ByteBuf first = s.Data(1, 2), second = s.Data(3, 4), third = s.Data(5, 6);
        buffer.AddComponents([first, second, third], operation != 0);
        buffer.RejectRemoval = true;
        if (operation == 0) buffer.RemoveComponent(0);
        else
        {
            buffer.SetIndex(3, 6).MarkReaderIndex().MarkWriterIndex();
            if (operation == 1) buffer.DiscardReadComponents();
            else buffer.DiscardReadBytes();
        }
        byte[] expected = operation == 2 ? [4, 5, 6] : [3, 4, 5, 6];
        Assert.Equal(expected, ByteBufUtil.GetBytes(buffer, 0, buffer.Capacity));
        int reader = operation == 1 ? 1 : 0, writer = operation == 0 ? 0 : expected.Length;
        Assert.Equal(reader, buffer.ReaderIndex); Assert.Equal(writer, buffer.WriterIndex);
        if (operation != 0)
        {
            buffer.SetIndex(0, buffer.Capacity).ResetWriterIndex().ResetReaderIndex();
            Assert.Equal(reader, buffer.ReaderIndex); Assert.Equal(writer, buffer.WriterIndex);
        }
        Assert.Equal(2, buffer.NumComponents); Assert.Equal(0, first.ReferenceCount);
        Assert.Equal(1, second.ReferenceCount); Assert.Equal(1, third.ReferenceCount); Assert.Empty(buffer.Calls);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void NonCompositeFlatteningDoesNotCallPublicInsertion(bool pooled, bool direct)
    {
        using var s = new Storage(pooled, direct);
        Trap buffer = s.Composite(direct, 2);
        buffer.RejectAdd = true;
        ByteBuf source = s.Data(1, 2, 3, 4);
        Assert.Same(buffer, buffer.AddFlattenedComponents(source, true));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, ByteBufUtil.GetBytes(buffer));
        Assert.Equal(1, source.ReferenceCount); Assert.Equal(1, buffer.NumComponents); Assert.Empty(buffer.Calls);
    }

    [Theory]
    [MemberData(nameof(BatchCases))]
    public void BatchConvenienceMethodsUsePrivateInsertion(bool pooled, bool direct, bool enumerable, bool increase)
    {
        using var s = new Storage(pooled, direct);
        Trap buffer = s.Composite(direct, 4);
        ByteBuf[] sources = [s.Data(1, 2), s.Data(3, 4)];
        buffer.RejectIndexedBatch = true;
        if (enumerable) buffer.AddComponents((IEnumerable<ByteBuf>)sources, increase);
        else if (increase) buffer.AddComponents(sources, true);
        else buffer.AddComponents(sources);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, ByteBufUtil.GetBytes(buffer, 0, 4));
        Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(increase ? 4 : 0, buffer.WriterIndex);
        Assert.Equal(2, buffer.NumComponents); Assert.Empty(buffer.Calls);
    }

    [Theory]
    [MemberData(nameof(ConstructorCases))]
    public void ConstructorsDoNotCallOverridableBatchMethods(bool pooled, bool direct, bool enumerable)
    {
        using var s = new Storage(pooled, direct);
        ByteBuf[] sources = [s.Data(1, 2), s.Data(3, 4)];
        ConstructorTrap buffer = s.Own(enumerable
            ? new ConstructorTrap(s.Allocator, direct, (IEnumerable<ByteBuf>)sources)
            : new ConstructorTrap(s.Allocator, direct, sources));
        Assert.Equal(2, buffer.NumComponents); Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(4, buffer.WriterIndex);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, ByteBufUtil.GetBytes(buffer));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void ExplicitPublicRequestsStillReachOverrides(bool pooled, bool direct)
    {
        using var s = new Storage(pooled, direct);
        Trap buffer = s.Composite(direct, 3);
        buffer.AddComponents([s.Data(1, 2), s.Data(3, 4)], true);
        buffer.RejectAdd = buffer.RejectAllConsolidation = buffer.RejectRangeConsolidation = buffer.RejectRemoval = true;
        Assert.Throws<InvalidOperationException>(() => buffer.AddComponent(s.Data(5), true));
        Assert.Throws<InvalidOperationException>(() => buffer.Consolidate());
        Assert.Throws<InvalidOperationException>(() => buffer.Consolidate(0, 2));
        Assert.Throws<InvalidOperationException>(() => buffer.RemoveComponents(0, 1));
        buffer.RejectArrayBatch = true;
        Assert.Throws<InvalidOperationException>(() => buffer.AddComponents(s.Data(6)));
        Assert.Equal(new[] { "add", "consolidate", "range-consolidate", "remove", "array-forward" }, buffer.Calls);
        Assert.Equal(2, buffer.NumComponents); Assert.Equal(new byte[] { 1, 2, 3, 4 }, ByteBufUtil.GetBytes(buffer));
    }
}
