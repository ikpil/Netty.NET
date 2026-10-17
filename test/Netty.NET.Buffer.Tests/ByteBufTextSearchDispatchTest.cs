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
using System.Collections.Generic;
using System.Text;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// Source contracts: WrappedByteBuf/WrappedCompositeByteBuf text and search delegates,
// including the forwarding inherited by simple leak and unreleasable facades.
public class ByteBufTextSearchDispatchTest
{
    private sealed class Wrapped(ByteBuf buffer) : WrappedByteBuf(buffer);
    private sealed class Tracker : IResourceLeakTracker<ByteBuf>
    {
        public int Records, Closes;
        public void Record() => ++Records;
        public void Record(object hint) => ++Records;
        public bool Close(ByteBuf value) { ++Closes; return true; }
    }

    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new();
        public readonly Tracker Inner = new(), Outer = new();
        public readonly ByteBuf Root, Facade;
        private readonly ByteBuf _owner;
        private readonly bool _outerTracked;

        public Storage(int backing, int facade)
        {
            byte[] bytes = [99, 65, 195, 169, 66, 67, 68, 99];
            Root = backing switch
            {
                0 => new UnpooledHeapByteBuf(8, 64).WriteBytes(bytes),
                1 => new UnpooledDirectByteBuf(8, 64, Native).WriteBytes(bytes),
                _ => new CompositeByteBuf(16, false, Native)
                    .AddComponent(Unpooled.WrappedBuffer(bytes[..4]), true)
                    .AddComponent(new UnpooledDirectByteBuf(4, 4, Native).WriteBytes(bytes[4..]), true)
            };
            Root.SetIndex(1, 7).MarkReaderIndex().MarkWriterIndex();
            ByteBuf tracked = backing == 2
                ? new AdvancedLeakAwareCompositeByteBuf((CompositeByteBuf)Root, Inner)
                : new AdvancedLeakAwareByteBuf(Root, Inner);
            _outerTracked = facade is 1 or 4;
            Facade = facade switch
            {
                0 => new Wrapped(tracked),
                1 => new SimpleLeakAwareByteBuf(tracked, Outer),
                2 => Unpooled.UnreleasableBuffer(tracked),
                3 => new WrappedCompositeByteBuf((CompositeByteBuf)tracked),
                _ => new SimpleLeakAwareCompositeByteBuf((CompositeByteBuf)tracked, Outer)
            };
            _owner = facade == 2 ? tracked : Facade;
        }

        public void Dispose()
        {
            if (Root.ReferenceCount > 0) Assert.True(_owner.Release());
            Assert.Equal(0, Root.ReferenceCount);
            Assert.Equal(1, Inner.Closes);
            Assert.Equal(_outerTracked ? 1 : 0, Outer.Closes);
            Assert.Equal(0, Native.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> Cases()
    {
        for (int backing = 0; backing < 3; ++backing)
        for (int facade = 0; facade < (backing == 2 ? 5 : 3); ++facade)
        for (int operation = 0; operation < 9; ++operation)
            yield return [backing, facade, operation];
    }

    private static object Invoke(ByteBuf buffer, int operation, Encoding encoding) => operation switch
    {
        0 => buffer.IndexOf(1, 7, 66),
        1 => buffer.BytesBefore(66),
        2 => buffer.BytesBefore(3, 66),
        3 => buffer.BytesBefore(2, 4, 66),
        4 => buffer.GetString(encoding),
        5 => buffer.GetString(2, 3, encoding),
        6 => buffer.ReadString(3, encoding),
        7 => buffer.SetString(2, "한Z", encoding),
        _ => buffer.WriteString("한Z", encoding)
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void TextAndSearchPreserveParentPolicyIndicesAndSingleAccessRecording(int backing, int facade, int operation)
    {
        using var storage = new Storage(backing, facade);
        ByteBuf buffer = storage.Facade;
        object expected = operation switch
        {
            0 => 4, 1 => 3, 2 => -1, 3 => 2, 4 => "AéBCD", 5 => "éB", 6 => "Aé", _ => 4
        };
        Assert.Equal(expected, Invoke(buffer, operation, Encoding.UTF8));
        Assert.Equal(1, storage.Inner.Records);
        Assert.Equal(operation == 6 ? 4 : 1, storage.Root.ReaderIndex);
        Assert.Equal(operation == 8 ? 11 : 7, storage.Root.WriterIndex);
        byte[] bytes = new byte[operation == 8 ? 11 : 8];
        storage.Root.GetBytes(0, bytes);
        Assert.Equal(operation switch
        {
            7 => new byte[] { 99, 65, 237, 149, 156, 90, 68, 99 },
            8 => new byte[] { 99, 65, 195, 169, 66, 67, 68, 237, 149, 156, 90 },
            _ => new byte[] { 99, 65, 195, 169, 66, 67, 68, 99 }
        }, bytes);
        storage.Root.SetIndex(1, 7);

        int before = storage.Inner.Records;
        if (operation >= 4)
            Assert.Throws<ArgumentNullException>(() => Invoke(buffer, operation, null));
        else
        {
            switch (operation)
            {
                case 0: Assert.Throws<ArgumentOutOfRangeException>(() => buffer.IndexOf(0, 9, 66)); break;
                case 1:
                    Assert.Equal(-1, buffer.BytesBefore(255));
                    break;
                case 2: Assert.Throws<ArgumentOutOfRangeException>(() => buffer.BytesBefore(7, 66)); break;
                case 3: Assert.Throws<ArgumentOutOfRangeException>(() => buffer.BytesBefore(7, 2, 66)); break;
            }
        }
        Assert.Equal(before + 1, storage.Inner.Records);
        Assert.Equal(1, buffer.ReaderIndex); Assert.Equal(7, buffer.WriterIndex);
        // Decoding uses the supplied CLR fallback policy; failures must not advance the reader.
        if (operation is 4 or 5 or 6)
        {
            storage.Root.SetByte(2, 255);
            var strict = new UTF8Encoding(false, true);
            before = storage.Inner.Records;
            Assert.Throws<DecoderFallbackException>(() => Invoke(buffer, operation, strict));
            Assert.Equal(before + 1, storage.Inner.Records);
            Assert.Equal(1, buffer.ReaderIndex); Assert.Equal(7, buffer.WriterIndex);
        }
        else if (operation is 7 or 8)
        {
            var strict = new UTF8Encoding(false, true);
            before = storage.Inner.Records;
            Assert.Throws<EncoderFallbackException>(() =>
            {
                if (operation == 7) buffer.SetString(2, "\ud800", strict);
                else buffer.WriteString("\ud800", strict);
            });
            Assert.Equal(before + 1, storage.Inner.Records);
            Assert.Equal(1, buffer.ReaderIndex); Assert.Equal(7, buffer.WriterIndex);
        }
        Assert.Equal(0, storage.Outer.Records);
        Assert.Equal(0, storage.Inner.Closes);
        Assert.Equal(1, buffer.ReferenceCount);
        storage.Root.SetIndex(0, 8).ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(1, buffer.ReaderIndex); Assert.Equal(7, buffer.WriterIndex);
        storage.Dispose(); before = storage.Inner.Records;
        // IndexOf has an original zero-capacity fast path, including owners cleared by release.
        if (operation == 0 && storage.Root.Capacity == 0)
            Assert.Equal(-1, Invoke(buffer, operation, Encoding.UTF8));
        else
            Assert.Throws<IllegalReferenceCountException>(() => Invoke(buffer, operation, Encoding.UTF8));
        Assert.Equal(before + 1, storage.Inner.Records);
    }

    private sealed class Observation
    {
        public readonly int[] Calls = new int[9];
        public Encoding Encoding;
        public int Index, Length;
        public byte Value;
        public string Text;
        public int Visit(int operation, int index = -1, int length = -1, byte value = 0,
            Encoding encoding = null, ReadOnlySpan<char> text = default)
        {
            ++Calls[operation]; Index = index; Length = length; Value = value;
            Encoding = encoding; Text = text.ToString();
            return 40 + operation;
        }
    }

    private sealed class ObservedHeap(Observation o) : UnpooledHeapByteBuf(8, 8)
    {
        public override int IndexOf(int fromIndex, int toIndex, byte value) => o.Visit(0, fromIndex, toIndex, value);
        public override int BytesBefore(byte value) => o.Visit(1, value: value);
        public override int BytesBefore(int length, byte value) => o.Visit(2, length: length, value: value);
        public override int BytesBefore(int index, int length, byte value) => o.Visit(3, index, length, value);
        public override string GetString(Encoding encoding) => o.Visit(4, encoding: encoding).ToString();
        public override string GetString(int index, int length, Encoding encoding) => o.Visit(5, index, length, encoding: encoding).ToString();
        public override string ReadString(int length, Encoding encoding) => o.Visit(6, length: length, encoding: encoding).ToString();
        public override int SetString(int index, ReadOnlySpan<char> text, Encoding encoding) => o.Visit(7, index, encoding: encoding, text: text);
        public override int WriteString(ReadOnlySpan<char> text, Encoding encoding) => o.Visit(8, encoding: encoding, text: text);
    }

    private sealed class ObservedComposite(Observation o) : CompositeByteBuf
    {
        public override int IndexOf(int fromIndex, int toIndex, byte value) => o.Visit(0, fromIndex, toIndex, value);
        public override int BytesBefore(byte value) => o.Visit(1, value: value);
        public override int BytesBefore(int length, byte value) => o.Visit(2, length: length, value: value);
        public override int BytesBefore(int index, int length, byte value) => o.Visit(3, index, length, value);
        public override string GetString(Encoding encoding) => o.Visit(4, encoding: encoding).ToString();
        public override string GetString(int index, int length, Encoding encoding) => o.Visit(5, index, length, encoding: encoding).ToString();
        public override string ReadString(int length, Encoding encoding) => o.Visit(6, length: length, encoding: encoding).ToString();
        public override int SetString(int index, ReadOnlySpan<char> text, Encoding encoding) => o.Visit(7, index, encoding: encoding, text: text);
        public override int WriteString(ReadOnlySpan<char> text, Encoding encoding) => o.Visit(8, encoding: encoding, text: text);
    }

    public static IEnumerable<object[]> Overrides()
    {
        for (int facade = 0; facade < 3; ++facade)
        for (int operation = 0; operation < 9; ++operation) yield return [facade, operation];
    }

    [Theory]
    [MemberData(nameof(Overrides))]
    public void ParentOverrideReceivesTheOriginalOperationAndArguments(int facade, int operation)
    {
        var observation = new Observation();
        ByteBuf root = facade == 1 ? new ObservedComposite(observation) : new ObservedHeap(observation);
        ByteBuf buffer = facade switch
        {
            0 => new Wrapped(root),
            1 => new WrappedCompositeByteBuf((CompositeByteBuf)root),
            _ => Unpooled.UnreleasableBuffer(root)
        };
        try
        {
            object expected = operation is 4 or 5 or 6 ? (40 + operation).ToString() : 40 + operation;
            Assert.Equal(expected, Invoke(buffer, operation, Encoding.UTF8));
            Assert.Equal(operation switch { 0 => 1, 3 or 5 or 7 => 2, _ => -1 }, observation.Index);
            Assert.Equal(operation switch { 0 => 7, 2 or 5 or 6 => 3, 3 => 4, _ => -1 }, observation.Length);
            Assert.Equal(operation < 4 ? (byte)66 : (byte)0, observation.Value);
            Assert.Same(operation >= 4 ? Encoding.UTF8 : null, observation.Encoding);
            Assert.Equal(operation >= 7 ? "한Z" : "", observation.Text);
            for (int i = 0; i < 9; ++i) Assert.Equal(i == operation ? 1 : 0, observation.Calls[i]);
        }
        finally { root.Release(); }
    }
}
