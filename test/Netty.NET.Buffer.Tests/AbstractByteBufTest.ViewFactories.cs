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
    [Fact]
    public void TestRetainedSliceIndexOutOfBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            AssertSliceOutOfBounds(true, true, true);
        });
    }

    [Fact]
    public void TestRetainedSliceLengthOutOfBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            AssertSliceOutOfBounds(true, true, false);
        });
    }

    [Fact]
    public void TestMixedSliceAIndexOutOfBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            AssertSliceOutOfBounds(true, false, true);
        });
    }

    [Fact]
    public void TestMixedSliceALengthOutOfBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            AssertSliceOutOfBounds(true, false, false);
        });
    }

    [Fact]
    public void TestMixedSliceBIndexOutOfBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            AssertSliceOutOfBounds(false, true, true);
        });
    }

    [Fact]
    public void TestMixedSliceBLengthOutOfBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            AssertSliceOutOfBounds(false, true, false);
        });
    }

    [Fact]
    public void TestSliceIndexOutOfBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            AssertSliceOutOfBounds(false, false, true);
        });
    }

    [Fact]
    public void TestSliceLengthOutOfBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            AssertSliceOutOfBounds(false, false, false);
        });
    }

    [Fact]
    public void TestReadSliceOutOfBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            AssertReadSliceOutOfBounds(false);
        });
    }

    [Fact]
    public void TestReadRetainedSliceOutOfBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            AssertReadSliceOutOfBounds(true);
        });
    }

    [Fact]
    public void TestSliceAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().Slice();
        });
    }

    [Fact]
    public void TestSliceAfterRelease2()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().Slice(0, 1);
        });
    }

    [Fact]
    public void TestRetainedSliceAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().RetainedSlice();
        });
    }

    [Fact]
    public void TestRetainedSliceAfterRelease2()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().RetainedSlice(0, 1);
        });
    }

    [Fact]
    public void TestDuplicateAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().Duplicate();
        });
    }

    [Fact]
    public void TestRetainedDuplicateAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().RetainedDuplicate();
        });
    }

    [Fact]
    public void TestSliceRelease()
    {
        ByteBuf buf = NewBuffer(8);
        Assert.Equal(1, buf.ReferenceCount);
        Assert.True(buf.Slice().Release());
        Assert.Equal(0, buf.ReferenceCount);
    }

    [Fact]
    public void TestSliceIndex()
    {
        Assert.Equal(0, buffer.Slice(0, buffer.Capacity).ReaderIndex);
        Assert.Equal(0, buffer.Slice(0, buffer.Capacity - 1).ReaderIndex);
        Assert.Equal(0, buffer.Slice(1, buffer.Capacity - 1).ReaderIndex);
        Assert.Equal(0, buffer.Slice(1, buffer.Capacity - 2).ReaderIndex);

        Assert.Equal(buffer.Capacity, buffer.Slice(0, buffer.Capacity).WriterIndex);
        Assert.Equal(buffer.Capacity - 1, buffer.Slice(0, buffer.Capacity - 1).WriterIndex);
        Assert.Equal(buffer.Capacity - 1, buffer.Slice(1, buffer.Capacity - 1).WriterIndex);
        Assert.Equal(buffer.Capacity - 2, buffer.Slice(1, buffer.Capacity - 2).WriterIndex);
    }

    [Fact]
    public void TestRetainedSliceIndex()
    {
        ByteBuf retainedSlice = buffer.RetainedSlice(0, buffer.Capacity);
        Assert.Equal(0, retainedSlice.ReaderIndex);
        retainedSlice.Release();

        retainedSlice = buffer.RetainedSlice(0, buffer.Capacity - 1);
        Assert.Equal(0, retainedSlice.ReaderIndex);
        retainedSlice.Release();

        retainedSlice = buffer.RetainedSlice(1, buffer.Capacity - 1);
        Assert.Equal(0, retainedSlice.ReaderIndex);
        retainedSlice.Release();

        retainedSlice = buffer.RetainedSlice(1, buffer.Capacity - 2);
        Assert.Equal(0, retainedSlice.ReaderIndex);
        retainedSlice.Release();

        retainedSlice = buffer.RetainedSlice(0, buffer.Capacity);
        Assert.Equal(buffer.Capacity, retainedSlice.WriterIndex);
        retainedSlice.Release();

        retainedSlice = buffer.RetainedSlice(0, buffer.Capacity - 1);
        Assert.Equal(buffer.Capacity - 1, retainedSlice.WriterIndex);
        retainedSlice.Release();

        retainedSlice = buffer.RetainedSlice(1, buffer.Capacity - 1);
        Assert.Equal(buffer.Capacity - 1, retainedSlice.WriterIndex);
        retainedSlice.Release();

        retainedSlice = buffer.RetainedSlice(1, buffer.Capacity - 2);
        Assert.Equal(buffer.Capacity - 2, retainedSlice.WriterIndex);
        retainedSlice.Release();
    }

    [Fact]
    public void TestDuplicateOfSliceHasTheSameCapacityAsTheSlice()
    {
        ByteBuf slice = buffer.Slice();
        ByteBuf duplicate = slice.Duplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);

        slice = buffer.Slice(0, buffer.Capacity - 2);
        duplicate = slice.Duplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);

        slice = buffer.Slice();
        duplicate = slice.RetainedDuplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);
        duplicate.Release();

        slice = buffer.Slice(0, buffer.Capacity - 2);
        duplicate = slice.RetainedDuplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);
        duplicate.Release();
    }

    [Fact]
    public void TestDuplicateOfRetainedSliceHasTheSameCapacityAsTheSlice()
    {
        ByteBuf slice = buffer.RetainedSlice();
        ByteBuf duplicate = slice.Duplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);
        slice.Release();

        slice = buffer.RetainedSlice(0, buffer.Capacity - 2);
        duplicate = slice.Duplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);
        slice.Release();

        slice = buffer.RetainedSlice();
        duplicate = slice.RetainedDuplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);
        slice.Release();
        duplicate.Release();

        slice = buffer.RetainedSlice(0, buffer.Capacity - 2);
        duplicate = slice.RetainedDuplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);
        slice.Release();
        duplicate.Release();
    }

    [Fact]
    public void TestRetainedSliceOfNonRetainedDerivedBufferCoversTheReadableBytes()
    {
        for (int i = 0; i < buffer.Capacity; i++)
        {
            buffer.SetByte(i, i);
        }
        // a writer index below the capacity, and a reader index at zero or above
        for (int readerIndex = 0; readerIndex <= 1; readerIndex++)
        {
            buffer.SetIndex(readerIndex, buffer.Capacity - 2);
            ByteBuf retainedDuplicate = null;
            ByteBuf retainedSlice = null;
            try
            {
                retainedDuplicate = buffer.RetainedDuplicate();
                retainedSlice = buffer.RetainedSlice();
                foreach (ByteBuf derived in new ByteBuf[] { retainedDuplicate, retainedSlice })
                {
                    AssertRetainedSliceBeforeAndAfterRead(derived.Duplicate());
                    AssertRetainedSliceBeforeAndAfterRead(derived.Slice());
                    AssertRetainedSliceBeforeAndAfterRead(derived.Duplicate().Duplicate());
                    AssertRetainedSliceBeforeAndAfterRead(derived.Slice().Slice());
                }
            }
            finally
            {
                if (retainedDuplicate != null)
                {
                    retainedDuplicate.Release();
                }
                if (retainedSlice != null)
                {
                    retainedSlice.Release();
                }
            }
            Assert.Equal(1, buffer.ReferenceCount);
        }
    }

    [Fact]
    public void TestSequentialSlice1()
    {
        buffer.WriterIndex = 0;
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            byte[] value = new byte[BLOCK_SIZE];
            random.NextBytes(value);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(i, buffer.WriterIndex);
            buffer.WriteBytes(value);
        }

        random = new Random(seed);
        byte[] expectedValue = new byte[BLOCK_SIZE];
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValue);
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.Equal(CAPACITY, buffer.WriterIndex);
            ByteBuf actualValue = buffer.ReadSlice(BLOCK_SIZE);
            // CLR BE/LE interpretation is fixed by each accessor; no mutable byte-order identity exists.
            Assert.Equal(WrappedBuffer(expectedValue), actualValue);

            // Make sure if it is a sliced buffer.
            actualValue.SetByte(0, unchecked((byte)(actualValue.GetByte(0) + 1)));
            Assert.Equal(buffer.GetByte(i), actualValue.GetByte(0));
        }
    }

    private void AssertSliceOutOfBounds(bool initRetainedSlice, bool finalRetainedSlice, bool indexOutOfBounds)
    {
        ByteBuf buf = NewBuffer(8);
        ByteBuf slice = initRetainedSlice ? buf.RetainedSlice(buf.ReaderIndex + 1, 2)
                                          : buf.Slice(buf.ReaderIndex + 1, 2);
        try
        {
            Assert.Equal(2, slice.Capacity);
            Assert.Equal(2, slice.MaxCapacity);
            int index = indexOutOfBounds ? 3 : 0;
            int length = indexOutOfBounds ? 0 : 3;
            if (finalRetainedSlice)
            {
                // This is expected to fail ... so no need to release.
                slice.RetainedSlice(index, length);
            }
            else
            {
                slice.Slice(index, length);
            }
        }
        finally
        {
            if (initRetainedSlice)
            {
                slice.Release();
            }
            buf.Release();
        }
    }

    private void AssertReadSliceOutOfBounds(bool retainedSlice)
    {
        ByteBuf buf = NewBuffer(100);
        try
        {
            buf.WriteZero(50);
            if (retainedSlice)
            {
                buf.ReadRetainedSlice(51);
            }
            else
            {
                buf.ReadSlice(51);
            }
            Assert.Fail();
        }
        finally
        {
            buf.Release();
        }
    }

    private static void AssertRetainedSliceBeforeAndAfterRead(ByteBuf buf)
    {
        AssertRetainedSliceMatchesTheReadableBytes(buf);
        buf.ReadByte();
        AssertRetainedSliceMatchesTheReadableBytes(buf);
    }

    private static void AssertRetainedSliceMatchesTheReadableBytes(ByteBuf buf)
    {
        ByteBuf retainedSlice = buf.RetainedSlice();
        try
        {
            Assert.Equal(buf.ReadableBytes, retainedSlice.ReadableBytes);
            Assert.Equal(buf.Slice(), retainedSlice);
        }
        finally
        {
            retainedSlice.Release();
        }
    }

}
