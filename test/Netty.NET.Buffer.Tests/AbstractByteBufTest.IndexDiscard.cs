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
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public abstract partial class AbstractByteBufTest
{
    protected virtual bool DiscardReadBytesDoesNotMoveWritableBytes()
    {
        return true;
    }

    [Fact]
    public void ReaderIndexBoundaryCheck1()
    {
        try
        {
            buffer.WriterIndex = 0;
        }
        catch (ArgumentOutOfRangeException)
        {
            Assert.Fail();
        }
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.ReaderIndex = -1;
        });
    }

    [Fact]
    public void ReaderIndexBoundaryCheck2()
    {
        try
        {
            buffer.WriterIndex = buffer.Capacity;
        }
        catch (ArgumentOutOfRangeException)
        {
            Assert.Fail();
        }
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.ReaderIndex = buffer.Capacity + 1;
        });
    }

    [Fact]
    public void ReaderIndexBoundaryCheck3()
    {
        try
        {
            buffer.WriterIndex = CAPACITY / 2;
        }
        catch (ArgumentOutOfRangeException)
        {
            Assert.Fail();
        }
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.ReaderIndex = CAPACITY * 3 / 2;
        });
    }

    [Fact]
    public void ReaderIndexBoundaryCheck4()
    {
        buffer.WriterIndex = 0;
        buffer.ReaderIndex = 0;
        buffer.WriterIndex = buffer.Capacity;
        buffer.ReaderIndex = buffer.Capacity;
    }

    [Fact]
    public void WriterIndexBoundaryCheck1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.WriterIndex = -1;
        });
    }

    [Fact]
    public void WriterIndexBoundaryCheck2()
    {
        try
        {
            buffer.WriterIndex = CAPACITY;
            buffer.ReaderIndex = CAPACITY;
        }
        catch (ArgumentOutOfRangeException)
        {
            Assert.Fail();
        }
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.WriterIndex = buffer.Capacity + 1;
        });
    }

    [Fact]
    public void WriterIndexBoundaryCheck3()
    {
        try
        {
            buffer.WriterIndex = CAPACITY;
            buffer.ReaderIndex = CAPACITY / 2;
        }
        catch (ArgumentOutOfRangeException)
        {
            Assert.Fail();
        }
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.WriterIndex = CAPACITY / 4;
        });
    }

    [Fact]
    public void WriterIndexBoundaryCheck4()
    {
        buffer.WriterIndex = 0;
        buffer.ReaderIndex = 0;
        buffer.WriterIndex = CAPACITY;

        buffer.WriteBytes(ReadOnlySpan<byte>.Empty);
    }

    [Fact]
    public void SetIndexBoundaryCheck1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.SetIndex(-1, CAPACITY);
        });
    }

    [Fact]
    public void SetIndexBoundaryCheck2()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.SetIndex(CAPACITY / 2, CAPACITY / 4);
        });
    }

    [Fact]
    public void SetIndexBoundaryCheck3()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.SetIndex(0, CAPACITY + 1);
        });
    }

    [Fact]
    public void TestSkipBytes1()
    {
        buffer.SetIndex(CAPACITY / 4, CAPACITY / 2);

        buffer.SkipBytes(CAPACITY / 4);
        Assert.Equal(CAPACITY / 4 * 2, buffer.ReaderIndex);

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.SkipBytes(CAPACITY / 4 + 1));

        // Should remain unchanged.
        Assert.Equal(CAPACITY / 4 * 2, buffer.ReaderIndex);
    }

    [Fact]
    public void TestDiscardReadBytes()
    {
        buffer.WriterIndex = 0;
        for (int i = 0; i < buffer.Capacity; i += 4)
        {
            buffer.WriteInt(i);
        }
        ByteBuf copy = Own(Unpooled.CopiedBuffer(buffer));

        // Make sure there's no effect if called when readerIndex is 0.
        buffer.ReaderIndex = CAPACITY / 4;
        buffer.MarkReaderIndex();
        buffer.WriterIndex = CAPACITY / 3;
        buffer.MarkWriterIndex();
        buffer.ReaderIndex = 0;
        buffer.WriterIndex = CAPACITY / 2;
        buffer.DiscardReadBytes();

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(CAPACITY / 2, buffer.WriterIndex);
        Assert.Equal(copy.Slice(0, CAPACITY / 2), buffer.Slice(0, CAPACITY / 2));
        buffer.ResetReaderIndex();
        Assert.Equal(CAPACITY / 4, buffer.ReaderIndex);
        buffer.ResetWriterIndex();
        Assert.Equal(CAPACITY / 3, buffer.WriterIndex);

        // Make sure bytes after writerIndex is not copied.
        buffer.ReaderIndex = 1;
        buffer.WriterIndex = CAPACITY / 2;
        buffer.DiscardReadBytes();

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(CAPACITY / 2 - 1, buffer.WriterIndex);
        Assert.Equal(copy.Slice(1, CAPACITY / 2 - 1), buffer.Slice(0, CAPACITY / 2 - 1));

        if (DiscardReadBytesDoesNotMoveWritableBytes())
        {
            // If writable bytes were copied, the test should fail to avoid unnecessary memory bandwidth consumption.
            Assert.False(copy.Slice(CAPACITY / 2, CAPACITY / 2).Equals(buffer.Slice(CAPACITY / 2 - 1, CAPACITY / 2)));
        }
        else
        {
            Assert.Equal(copy.Slice(CAPACITY / 2, CAPACITY / 2), buffer.Slice(CAPACITY / 2 - 1, CAPACITY / 2));
        }

        // Marks also should be relocated.
        buffer.ResetReaderIndex();
        Assert.Equal(CAPACITY / 4 - 1, buffer.ReaderIndex);
        buffer.ResetWriterIndex();
        Assert.Equal(CAPACITY / 3 - 1, buffer.WriterIndex);
        copy.Release();
    }

    /**
     * The similar test case with {@link #testDiscardReadBytes()} but this one
     * discards a large chunk at once.
     */
    [Fact]
    public void TestDiscardReadBytes2()
    {
        buffer.WriterIndex = 0;
        for (int i = 0; i < buffer.Capacity; i ++)
        {
            buffer.WriteByte(unchecked((byte)i));
        }
        ByteBuf copy = Own(Unpooled.CopiedBuffer(buffer));

        // Discard the first (CAPACITY / 2 - 1) bytes.
        buffer.SetIndex(CAPACITY / 2 - 1, CAPACITY - 1);
        buffer.DiscardReadBytes();
        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(CAPACITY / 2, buffer.WriterIndex);
        for (int i = 0; i < CAPACITY / 2; i ++)
        {
            Assert.Equal(copy.Slice(CAPACITY / 2 - 1 + i, CAPACITY / 2 - i), buffer.Slice(i, CAPACITY / 2 - i));
        }
        copy.Release();
    }

    // Test case for https://github.com/netty/netty/issues/325
    [Fact]
    public void TestDiscardAllReadBytes()
    {
        buffer.WriterIndex = buffer.Capacity;
        buffer.ReaderIndex = buffer.WriterIndex;
        buffer.DiscardReadBytes();
    }

    [Fact]
    public void TestDiscardReadBytesAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().DiscardReadBytes();
        });
    }

    [Fact]
    public void TestDiscardSomeReadBytesAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().DiscardSomeReadBytes();
        });
    }

    private ByteBuf ReleasedBuffer()
    {
        ByteBuf buffer = NewBuffer(8);
        // Clear the buffer so we are sure the reader and writer indices are 0.
        // This is important as we may return a slice from newBuffer(...).
        buffer.Clear();
        Assert.True(buffer.Release());
        return buffer;
    }
}
