/*
 * Copyright 2015 The Netty Project
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
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class DefaultByteBufHolderTest
{
    [Fact]
    public void TestToString()
    {
        IByteBufHolder holder = new DefaultByteBufHolder(Unpooled.Buffer());
        Assert.Equal(1, holder.ReferenceCount);
        Assert.NotNull(holder.ToString());
        Assert.True(holder.Release());
        Assert.NotNull(holder.ToString());
    }

    [Fact]
    public void TestEqualsAndHashCode()
    {
        IByteBufHolder holder = new DefaultByteBufHolder(Unpooled.EmptyBuffer);
        IByteBufHolder copy = holder.Copy();
        try
        {
            Assert.Equal(holder, copy);
            Assert.Equal(holder.GetHashCode(), copy.GetHashCode());
        }
        finally { holder.Release(); copy.Release(); }
    }

    [Fact]
    public void TestDifferentClassesAreNotEqual()
    {
        // all objects here have EMPTY_BUFFER data but are instances of different classes
        // so we want to check that none of them are equal to another.
        IByteBufHolder dflt = new DefaultByteBufHolder(Unpooled.EmptyBuffer);
        IByteBufHolder other = new OtherByteBufHolder(Unpooled.EmptyBuffer, 123);
        IByteBufHolder constant1 = new FirstConstantHolder(Unpooled.EmptyBuffer);
        IByteBufHolder constant2 = new SecondConstantHolder(Unpooled.EmptyBuffer);
        try
        {
            // not using 'assertNotEquals' to be explicit about which object we are calling .equals() on
            Assert.False(dflt.Equals(other));
            Assert.False(dflt.Equals(constant1));
            Assert.False(constant1.Equals(dflt));
            Assert.False(constant1.Equals(other));
            Assert.False(constant1.Equals(constant2));
        }
        finally { dflt.Release(); other.Release(); constant1.Release(); constant2.Release(); }
    }

    private sealed class FirstConstantHolder(ByteBuf data) : DefaultByteBufHolder(data)
    {
        // intentionally empty
    }

    private sealed class SecondConstantHolder(ByteBuf data) : DefaultByteBufHolder(data)
    {
        // intentionally empty
    }

    private class OtherByteBufHolder(ByteBuf data, int extraField) : DefaultByteBufHolder(data)
    {
        private readonly int _extraField = extraField;

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj is null || GetType() != obj.GetType()) return false;
            if (!base.Equals(obj)) return false;
            return _extraField == ((OtherByteBufHolder)obj)._extraField;
        }

        public override int GetHashCode() => unchecked(31 * base.GetHashCode() + _extraField);
    }

    private static ByteBuf Owner(int kind)
    {
        byte[] bytes = [10, 20, 30, 40, 50, 60];
        return kind switch
        {
            0 => Unpooled.WrappedBuffer(bytes),
            1 => Unpooled.DirectBuffer(6, 6).WriteBytes(bytes),
            2 => Unpooled.WrappedBuffer(bytes).AsReadOnly(),
            3 => Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(bytes[..3]), true)
                .AddComponent(Unpooled.WrappedBuffer(bytes[3..]), true),
            4 => Unpooled.WrappedUnmodifiableBuffer(Unpooled.WrappedBuffer(bytes[..3]), Unpooled.WrappedBuffer(bytes[3..])),
            5 => Unpooled.WrappedBuffer(new byte[] { 0, 10, 20, 30, 40, 50, 60, 0 }).Slice(1, 6),
            6 => Unpooled.WrappedBuffer(bytes).Duplicate(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void CopyOwnsIndependentReadableBytesAndUsesVirtualReplacement(int kind)
    {
        ByteBuf data = Owner(kind);
        data.SetIndex(1, 5).MarkReaderIndex().MarkWriterIndex();
        var holder = new ReplacingHolder(data, 123);
        IByteBufHolder copy = holder.Copy();
        try
        {
            var derived = Assert.IsType<ReplacingHolder>(copy);
            Assert.Equal(123, derived.Tag);
            Assert.Equal(1, holder.Replacements);
            Assert.NotSame(data, copy.Content);
            Assert.Equal(0, copy.Content.ReaderIndex);
            Assert.Equal(4, copy.Content.WriterIndex);
            Assert.Equal("141e2832", ByteBufUtil.HexDump(copy.Content));
            Assert.Equal(holder.GetHashCode(), copy.GetHashCode());
            Assert.True(holder.Equals(copy));
            Assert.True(copy.Equals(holder));
            Assert.False(copy.Content.IsReadOnly);
            Assert.Same(data.Allocator, copy.Content.Allocator);
            copy.Content.SetByte(0, 99);
            Assert.Equal(20, data.GetByte(1));
            Assert.False(holder.Equals(copy));
            data.SetIndex(2, 4).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(1, data.ReaderIndex);
            Assert.Equal(5, data.WriterIndex);
            Assert.True(holder.Release());
            Assert.Equal(1, copy.ReferenceCount);
            Assert.Equal(99, copy.Content.GetByte(0));
        }
        finally
        {
            if (holder.ReferenceCount > 0) holder.Release();
            copy.Release();
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void DuplicateSharesLifetimeAndBytesButHasIndependentIndices(int kind)
    {
        ByteBuf data = Owner(kind);
        data.SetIndex(1, 5).MarkReaderIndex().MarkWriterIndex();
        var holder = new ReplacingHolder(data, 7);
        IByteBufHolder duplicate = holder.Duplicate();
        try
        {
            Assert.Equal(7, Assert.IsType<ReplacingHolder>(duplicate).Tag);
            Assert.Equal(1, holder.Replacements);
            Assert.Equal(1, holder.ReferenceCount);
            Assert.Equal(1, duplicate.ReferenceCount);
            Assert.Equal(data.IsReadOnly, duplicate.Content.IsReadOnly);
            Assert.Equal("141e2832", ByteBufUtil.HexDump(duplicate.Content));
            duplicate.Content.SetIndex(2, 4);
            Assert.Equal(1, data.ReaderIndex);
            Assert.Equal(5, data.WriterIndex);
            if (!data.IsReadOnly)
            {
                duplicate.Content.SetByte(2, 99);
                Assert.Equal(99, data.GetByte(2));
            }
            Assert.True(duplicate.Release());
            Assert.Equal(0, holder.ReferenceCount);
            Assert.Throws<IllegalReferenceCountException>(() => holder.Content);
            Assert.Throws<IllegalReferenceCountException>(() => duplicate.Content);
            Assert.True(holder.Equals(holder));
            Assert.False(holder.Equals(new DefaultByteBufHolder(Unpooled.EmptyBuffer)));
            Assert.NotNull(holder.ToString());
        }
        finally { if (holder.ReferenceCount > 0) holder.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void RetainedDuplicateKeepsContentAliveAfterOriginalRelease(int kind)
    {
        ByteBuf data = Owner(kind);
        var holder = new ReplacingHolder(data, 11);
        IByteBufHolder retained = holder.RetainedDuplicate();
        try
        {
            Assert.Equal(11, Assert.IsType<ReplacingHolder>(retained).Tag);
            Assert.Equal(1, holder.Replacements);
            Assert.Equal(2, data.ReferenceCount);
            Assert.Equal(2, retained.ReferenceCount);
            Assert.False(holder.Release());
            Assert.Same(data, holder.Content);
            Assert.Equal("0a141e28323c", ByteBufUtil.HexDump(retained.Content));
            Assert.True(retained.Release());
            Assert.Equal(0, data.ReferenceCount);
        }
        finally { if (data.ReferenceCount > 0) data.Release(data.ReferenceCount); }
    }

    [Fact]
    public void ReplaceAdoptsOnlyNewContentAndWorksAfterOldContentRelease()
    {
        var holder = new ReplacingHolder(Unpooled.Buffer(1, 1).WriteByte(1), 5);
        ByteBuf next = Unpooled.Buffer(1, 1).WriteByte(2);
        IByteBufHolder replacement = holder.Replace(next);
        try
        {
            Assert.Equal(5, Assert.IsType<ReplacingHolder>(replacement).Tag);
            Assert.Same(next, replacement.Content);
            Assert.Equal(1, next.ReferenceCount);
            Assert.True(holder.Release());
            Assert.Equal(2, replacement.Content.GetByte(0));
            IByteBufHolder afterRelease = holder.Replace(Unpooled.EmptyBuffer);
            Assert.Same(Unpooled.EmptyBuffer, afterRelease.Content);
            Assert.False(afterRelease.Release());
        }
        finally { if (holder.ReferenceCount > 0) holder.Release(); replacement.Release(); }
    }

    [Fact]
    public void ConstructorAndNullReplacementDoNotChangeExistingOwnership()
    {
        Assert.Throws<ArgumentNullException>(() => new DefaultByteBufHolder(null));
        ByteBuf data = Unpooled.Buffer(1, 1);
        var holder = new DefaultByteBufHolder(data);
        try
        {
            Assert.Same(data, holder.Content);
            Assert.Equal(1, data.ReferenceCount);
            Assert.Throws<ArgumentNullException>(() => holder.Replace(null));
            Assert.Equal(1, data.ReferenceCount);
        }
        finally { holder.Release(); }
        Assert.Throws<IllegalReferenceCountException>(() => holder.Content);
        Assert.Throws<IllegalReferenceCountException>(() => holder.Retain());
        Assert.Throws<IllegalReferenceCountException>(() => holder.Release());
    }

    [Fact]
    public void ReferenceCountAndTouchDelegateThroughBothInterfacesAndCommonUtilities()
    {
        var data = new TouchBuffer();
        IByteBufHolder holder = new DefaultByteBufHolder(data);
        IReferenceCounted counted = holder;
        object hint = new();
        try
        {
            Assert.Same(holder, holder.Retain());
            Assert.Same(holder, holder.Retain(2));
            Assert.Same(holder, counted.Retain());
            Assert.Same(holder, counted.Retain(2));
            Assert.Same(holder, ReferenceCountUtil.Retain(holder));
            Assert.Same(holder, ReferenceCountUtil.Retain(holder, 2));
            Assert.Equal(10, holder.ReferenceCount);
            Assert.Same(holder, holder.Touch());
            Assert.Null(data.Hint);
            Assert.Same(holder, holder.Touch(hint));
            Assert.Same(hint, data.Hint);
            Assert.Same(holder, counted.Touch());
            Assert.Same(holder, counted.Touch(hint));
            Assert.Same(holder, ReferenceCountUtil.Touch(holder));
            Assert.Same(holder, ReferenceCountUtil.Touch(holder, hint));
            Assert.Equal(6, data.Touches);
            Assert.Equal(10, data.ReferenceCount);
            Assert.Throws<ArgumentException>(() => holder.Retain(0));
            Assert.Throws<ArgumentException>(() => holder.Release(0));
            Assert.Throws<IllegalReferenceCountException>(() => holder.Release(11));
            Assert.Equal(10, data.ReferenceCount);
            Assert.False(holder.Release());
            Assert.False(counted.Release(3));
            Assert.False(ReferenceCountUtil.Release(holder, 5));
            Assert.True(counted.Release());
            Assert.Equal(0, data.ReferenceCount);
        }
        finally { if (data.ReferenceCount > 0) data.Release(data.ReferenceCount); }
    }

    [Fact]
    public void DerivedEqualityAddsFieldsWhileDefaultEqualityUsesCurrentReadableBytes()
    {
        var a = new OtherByteBufHolder(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }), 7);
        var b = new OtherByteBufHolder(Unpooled.WrappedBuffer(new byte[] { 9, 1, 2, 3 }), 7);
        var c = new OtherByteBufHolder(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }), 8);
        var plain = new DefaultByteBufHolder(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }));
        try
        {
            b.Content.ReaderIndex = 1;
            Assert.True(a.Equals(b)); Assert.True(b.Equals(a));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.False(a.Equals(c)); Assert.False(c.Equals(a));
            Assert.False(a.Equals(plain)); Assert.False(plain.Equals(a));
            Assert.False(plain.Equals(null)); Assert.False(plain.Equals(new object()));
            Assert.Equal(plain.Content.GetHashCode(), plain.GetHashCode());
            b.Content.SetByte(2, 4);
            Assert.False(a.Equals(b));
            Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
        }
        finally { a.Release(); b.Release(); c.Release(); plain.Release(); }
    }

    [Fact]
    public void ProtectedDiagnosticDoesNotUseOverriddenContentAccessor()
    {
        var holder = new ReplacingHolder(Unpooled.WrappedBuffer(new byte[] { 1, 2 }), 4);
        holder.HideContent = true;
        IByteBufHolder copy = holder.Copy(), duplicate = holder.Duplicate(), retained = holder.RetainedDuplicate();
        try
        {
            Assert.Equal("0102", ByteBufUtil.HexDump(copy.Content));
            Assert.Equal("0102", ByteBufUtil.HexDump(duplicate.Content));
            Assert.Equal("0102", ByteBufUtil.HexDump(retained.Content));
            Assert.Contains("ReplacingHolder(", holder.ToString());
            Assert.NotNull(holder.Diagnostic());
            Assert.False(holder.Release());
            Assert.True(retained.Release());
            Assert.NotNull(holder.Diagnostic());
            Assert.NotNull(holder.ToString());
        }
        finally
        {
            copy.Release();
            if (holder.ReferenceCount > 0) holder.Release(holder.ReferenceCount);
        }
    }

    private sealed class ReplacingHolder(ByteBuf data, int tag) : DefaultByteBufHolder(data)
    {
        public int Tag { get; } = tag;
        public int Replacements { get; private set; }
        public bool HideContent { get; set; }
        public override ByteBuf Content => HideContent ? throw new InvalidOperationException() : base.Content;
        public override IByteBufHolder Replace(ByteBuf content)
        {
            Replacements++;
            return new ReplacingHolder(content, Tag);
        }
        public string Diagnostic() => ContentToString();
    }

    private sealed class TouchBuffer() : UnpooledHeapByteBuf(1, 1)
    {
        public int Touches { get; private set; }
        public object Hint { get; private set; }
        public override ByteBuf Touch(object hint = null)
        {
            Touches++;
            Hint = hint;
            return this;
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void ConvertibleReturnsItselfRepeatedlyWithoutRetainOrAccessibilityCheck(int kind)
    {
        ByteBuf data = Owner(kind);
        IByteBufConvertible convertible = data;
        try
        {
            Assert.Same(data, convertible.AsByteBuf());
            Assert.Same(data, data.AsByteBuf());
            Assert.Equal(1, data.ReferenceCount);
            Assert.True(data.Release());
            Assert.Same(data, convertible.AsByteBuf());
            Assert.Equal(0, data.ReferenceCount);
        }
        finally { if (data.ReferenceCount > 0) data.Release(); }
    }

    [Fact]
    public void EmptyAndUnreleasableConversionsPreserveTheirLifetimePolicy()
    {
        IByteBufHolder empty = new DefaultByteBufHolder(Unpooled.EmptyBuffer);
        IByteBufHolder retained = empty.RetainedDuplicate();
        Assert.Equal(1, empty.ReferenceCount);
        Assert.False(empty.Release()); Assert.False(retained.Release());
        Assert.Same(Unpooled.EmptyBuffer, ((IByteBufConvertible)Unpooled.EmptyBuffer).AsByteBuf());
        ByteBuf root = Unpooled.Buffer(1, 1).WriteByte(8);
        ByteBuf wrapper = Unpooled.UnreleasableBuffer(root);
        IByteBufHolder holder = new DefaultByteBufHolder(wrapper);
        try
        {
            Assert.Same(wrapper, ((IByteBufConvertible)wrapper).AsByteBuf());
            Assert.Same(wrapper, holder.Content);
            Assert.False(holder.Release());
            Assert.Equal(1, root.ReferenceCount);
        }
        finally { root.Release(); }
    }
}
