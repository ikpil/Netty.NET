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
using System.Linq;
using System.Text;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteProcessorTest
{
    private const string OriginalText = "abc\r\n\ndef\r\rghi\n\njkl\0\0mno  \t\tx";
    private static readonly Func<byte, bool>[] Processors =
    {
        ByteProcessor.FIND_NUL, ByteProcessor.FIND_NON_NUL, ByteProcessor.FIND_CR, ByteProcessor.FIND_NON_CR,
        ByteProcessor.FIND_LF, ByteProcessor.FIND_NON_LF, ByteProcessor.FIND_SEMI_COLON, ByteProcessor.FIND_COMMA,
        ByteProcessor.FIND_ASCII_SPACE, ByteProcessor.FIND_CRLF, ByteProcessor.FIND_NON_CRLF,
        ByteProcessor.FIND_LINEAR_WHITESPACE, ByteProcessor.FIND_NON_LINEAR_WHITESPACE
    };

    private static void WithBuffer(int kind, byte[] bytes, Action<ByteBuf> test)
    {
        var native = new NativeMemoryAllocator();
        var allocator = new UnpooledByteBufAllocator(false, native);
        ByteBuf Direct(byte[] content) => allocator.DirectBuffer(content.Length, content.Length).WriteBytes(content);
        ByteBuf buffer = kind switch
        {
            0 => Unpooled.WrappedBuffer(bytes),
            1 => Direct(bytes),
            2 => new CompositeByteBuf(allocator, false).AddComponent(Unpooled.WrappedBuffer(bytes[..3]), true)
                .AddComponent(Unpooled.EmptyBuffer).AddComponent(Direct(bytes[3..]), true),
            3 => Unpooled.WrappedUnmodifiableBuffer(Unpooled.WrappedBuffer(bytes[..3]), Direct(bytes[3..])),
            4 => Direct(new byte[] { 11, 22 }.Concat(bytes).Append((byte)33).ToArray()).Slice(2, bytes.Length),
            5 => Unpooled.WrappedBuffer(bytes).Duplicate(),
            6 => Direct(bytes).AsReadOnly(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        try { test(buffer); }
        finally { buffer.Release(); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void TestForward(int kind)
    {
        WithBuffer(kind, Encoding.Latin1.GetBytes(OriginalText), buf =>
        {
            int length = buf.ReadableBytes;
            Assert.Equal(3, buf.ForEachByte(0, length, ByteProcessor.FIND_CRLF));
            Assert.Equal(6, buf.ForEachByte(3, length - 3, ByteProcessor.FIND_NON_CRLF));
            Assert.Equal(9, buf.ForEachByte(6, length - 6, ByteProcessor.FIND_CR));
            Assert.Equal(11, buf.ForEachByte(9, length - 9, ByteProcessor.FIND_NON_CR));
            Assert.Equal(14, buf.ForEachByte(11, length - 11, ByteProcessor.FIND_LF));
            Assert.Equal(16, buf.ForEachByte(14, length - 14, ByteProcessor.FIND_NON_LF));
            Assert.Equal(19, buf.ForEachByte(16, length - 16, ByteProcessor.FIND_NUL));
            Assert.Equal(21, buf.ForEachByte(19, length - 19, ByteProcessor.FIND_NON_NUL));
            Assert.Equal(24, buf.ForEachByte(19, length - 19, ByteProcessor.FIND_ASCII_SPACE));
            Assert.Equal(24, buf.ForEachByte(21, length - 21, ByteProcessor.FIND_LINEAR_WHITESPACE));
            Assert.Equal(28, buf.ForEachByte(24, length - 24, ByteProcessor.FIND_NON_LINEAR_WHITESPACE));
            Assert.Equal(-1, buf.ForEachByte(28, length - 28, ByteProcessor.FIND_LINEAR_WHITESPACE));
            Assert.Equal(0, buf.ReaderIndex); Assert.Equal(length, buf.WriterIndex); Assert.Equal(1, buf.ReferenceCount);
        });
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void TestBackward(int kind)
    {
        WithBuffer(kind, Encoding.Latin1.GetBytes(OriginalText), buf =>
        {
            int length = buf.ReadableBytes;
            Assert.Equal(27, buf.ForEachByteDesc(0, length, ByteProcessor.FIND_LINEAR_WHITESPACE));
            Assert.Equal(25, buf.ForEachByteDesc(0, length, ByteProcessor.FIND_ASCII_SPACE));
            Assert.Equal(23, buf.ForEachByteDesc(0, 28, ByteProcessor.FIND_NON_LINEAR_WHITESPACE));
            Assert.Equal(20, buf.ForEachByteDesc(0, 24, ByteProcessor.FIND_NUL));
            Assert.Equal(18, buf.ForEachByteDesc(0, 21, ByteProcessor.FIND_NON_NUL));
            Assert.Equal(15, buf.ForEachByteDesc(0, 19, ByteProcessor.FIND_LF));
            Assert.Equal(13, buf.ForEachByteDesc(0, 16, ByteProcessor.FIND_NON_LF));
            Assert.Equal(10, buf.ForEachByteDesc(0, 14, ByteProcessor.FIND_CR));
            Assert.Equal(8, buf.ForEachByteDesc(0, 11, ByteProcessor.FIND_NON_CR));
            Assert.Equal(5, buf.ForEachByteDesc(0, 9, ByteProcessor.FIND_CRLF));
            Assert.Equal(2, buf.ForEachByteDesc(0, 6, ByteProcessor.FIND_NON_CRLF));
            Assert.Equal(-1, buf.ForEachByteDesc(0, 3, ByteProcessor.FIND_CRLF));
            Assert.Equal(0, buf.ReaderIndex); Assert.Equal(length, buf.WriterIndex); Assert.Equal(1, buf.ReferenceCount);
        });
    }

    private static bool Stops(int predicate, byte value) => predicate switch
    {
        0 => value == 0, 1 => value != 0, 2 => value == 13, 3 => value != 13,
        4 => value == 10, 5 => value != 10, 6 => value == 59, 7 => value == 44, 8 => value == 32,
        9 => value is 13 or 10, 10 => value is not (13 or 10),
        11 => value is 32 or 9, 12 => value is not (32 or 9), _ => throw new ArgumentOutOfRangeException(nameof(predicate))
    };

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void EveryOctetAndPredicateAgreeWithIndependentStopPositions(int kind)
    {
        byte[] bytes = Enumerable.Range(0, 256).Select(i => unchecked((byte)(128 + i * 73))).ToArray();
        WithBuffer(kind, bytes, buf =>
        {
            buf.SetIndex(7, 249);
            for (int predicate = 0; predicate < Processors.Length; predicate++)
                for (int start = 0; start <= bytes.Length; start += 16)
                {
                    int length = Math.Min(37, bytes.Length - start);
                    int first = -1, last = -1;
                    for (int index = start; index < start + length; index++)
                        if (Stops(predicate, bytes[index])) { if (first == -1) first = index; last = index; }
                    Assert.Equal(first, buf.ForEachByte(start, length, Processors[predicate]));
                    Assert.Equal(last, buf.ForEachByteDesc(start, length, Processors[predicate]));
                }
            Assert.Equal(bytes, ByteBufUtil.GetBytes(buf, 0, bytes.Length));
            Assert.Equal(7, buf.ReaderIndex); Assert.Equal(249, buf.WriterIndex); Assert.Equal(1, buf.ReferenceCount);
        });
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void CapturedPredicatesUseReadableRangeAbsoluteIndicesAndPropagateTheSameException(int kind)
    {
        byte[] bytes = { 0, 128, 255, 13, 10, 32, 9, 0 };
        WithBuffer(kind, bytes, buf =>
        {
            buf.SetIndex(1, 7); buf.MarkReaderIndex(); buf.MarkWriterIndex();
            foreach (bool reverse in new[] { false, true })
            {
                var seen = new List<byte>(); int remaining = 3;
                Func<byte, bool> processor = value => { seen.Add(value); return --remaining > 0; };
                Assert.Equal(reverse ? 4 : 3, reverse ? buf.ForEachByteDesc(processor) : buf.ForEachByte(processor));
                Assert.Equal(reverse ? new byte[] { 9, 32, 10 } : new byte[] { 128, 255, 13 }, seen);
                var failure = new InvalidOperationException("processor failure");
                Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
                {
                    if (reverse) buf.ForEachByteDesc(_ => throw failure); else buf.ForEachByte(_ => throw failure);
                }));
                Assert.Equal(1, buf.ReaderIndex); Assert.Equal(7, buf.WriterIndex); Assert.Equal(1, buf.ReferenceCount);
            }
            buf.ResetReaderIndex(); buf.ResetWriterIndex(); Assert.Equal(1, buf.ReaderIndex); Assert.Equal(7, buf.WriterIndex);
        });
    }
}
