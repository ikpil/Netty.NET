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
    public void EnsureWritableWithForceDoesNotThrow()
    {
        AssertEnsureWritableDoesNotThrow(true);
    }

    [Fact]
    public void EnsureWritableWithOutForceDoesNotThrow()
    {
        AssertEnsureWritableDoesNotThrow(false);
    }

    [Fact]
    public void EnsureWritableWithForceAsReadyOnly()
    {
        AssertEnsureWritableReadOnly(true);
    }

    [Fact]
    public void EnsureWritableWithOutForceAsReadOnly()
    {
        AssertEnsureWritableReadOnly(false);
    }

    [Fact]
    public void TestEnsureWritableAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().EnsureWritable(16);
        });
    }

    [Fact]
    public void TestEnsureWritableIntegerOverflow()
    {
        ByteBuf buffer = NewBuffer(CAPACITY);
        buffer.WriterIndex = buffer.ReaderIndex;
        buffer.WriteByte(1);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.EnsureWritable(int.MaxValue));
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestGetBooleanAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetBoolean(0);
        });
    }

    [Fact]
    public void TestGetByteAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetByte(0);
        });
    }

    [Fact]
    public void TestGetUnsignedByteAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetByte(0);
        });
    }

    [Fact]
    public void TestGetShortAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetShort(0);
        });
    }

    [Fact]
    public void TestGetShortLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetShortLE(0);
        });
    }

    [Fact]
    public void TestGetUnsignedShortAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetUnsignedShort(0);
        });
    }

    [Fact]
    public void TestGetUnsignedShortLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetUnsignedShortLE(0);
        });
    }

    [Fact]
    public void TestGetMediumAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetMedium(0);
        });
    }

    [Fact]
    public void TestGetMediumLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetMediumLE(0);
        });
    }

    [Fact]
    public void TestGetUnsignedMediumAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetUnsignedMedium(0);
        });
    }

    [Fact]
    public void TestGetIntAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetInt(0);
        });
    }

    [Fact]
    public void TestGetIntLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetIntLE(0);
        });
    }

    [Fact]
    public void TestGetUnsignedIntAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetUnsignedInt(0);
        });
    }

    [Fact]
    public void TestGetUnsignedIntLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetUnsignedIntLE(0);
        });
    }

    [Fact]
    public void TestGetLongAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetLong(0);
        });
    }

    [Fact]
    public void TestGetLongLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetLongLE(0);
        });
    }

    [Fact]
    public void TestGetCharAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetChar(0);
        });
    }

    [Fact]
    public void TestGetFloatAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetFloat(0);
        });
    }

    [Fact]
    public void TestGetFloatLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetFloatLE(0);
        });
    }

    [Fact]
    public void TestGetDoubleAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetDouble(0);
        });
    }

    [Fact]
    public void TestGetDoubleLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetDoubleLE(0);
        });
    }

    [Fact]
    public void TestSetBooleanAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetBoolean(0, true);
        });
    }

    [Fact]
    public void TestSetByteAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetByte(0, 1);
        });
    }

    [Fact]
    public void TestSetShortAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetShort(0, 1);
        });
    }

    [Fact]
    public void TestSetShortLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetShortLE(0, 1);
        });
    }

    [Fact]
    public void TestSetMediumAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetMedium(0, 1);
        });
    }

    [Fact]
    public void TestSetMediumLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetMediumLE(0, 1);
        });
    }

    [Fact]
    public void TestSetIntAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetInt(0, 1);
        });
    }

    [Fact]
    public void TestSetIntLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetIntLE(0, 1);
        });
    }

    [Fact]
    public void TestSetLongAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetLong(0, 1);
        });
    }

    [Fact]
    public void TestSetLongLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetLongLE(0, 1);
        });
    }

    [Fact]
    public void TestSetCharAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetChar(0, (char)1);
        });
    }

    [Fact]
    public void TestSetFloatAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetFloat(0, 1);
        });
    }

    [Fact]
    public void TestSetDoubleAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetDouble(0, 1);
        });
    }

    [Fact]
    public void TestReadBooleanAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadBoolean();
        });
    }

    [Fact]
    public void TestReadByteAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadByte();
        });
    }

    [Fact]
    public void TestReadUnsignedByteAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadByte();
        });
    }

    [Fact]
    public void TestReadShortAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadShort();
        });
    }

    [Fact]
    public void TestReadShortLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadShortLE();
        });
    }

    [Fact]
    public void TestReadUnsignedShortAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadUnsignedShort();
        });
    }

    [Fact]
    public void TestReadUnsignedShortLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadUnsignedShortLE();
        });
    }

    [Fact]
    public void TestReadMediumAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadMedium();
        });
    }

    [Fact]
    public void TestReadMediumLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadMediumLE();
        });
    }

    [Fact]
    public void TestReadUnsignedMediumAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadUnsignedMedium();
        });
    }

    [Fact]
    public void TestReadUnsignedMediumLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadUnsignedMediumLE();
        });
    }

    [Fact]
    public void TestReadIntAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadInt();
        });
    }

    [Fact]
    public void TestReadIntLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadIntLE();
        });
    }

    [Fact]
    public void TestReadUnsignedIntAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadUnsignedInt();
        });
    }

    [Fact]
    public void TestReadUnsignedIntLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadUnsignedIntLE();
        });
    }

    [Fact]
    public void TestReadLongAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadLong();
        });
    }

    [Fact]
    public void TestReadLongLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadLongLE();
        });
    }

    [Fact]
    public void TestReadCharAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadChar();
        });
    }

    [Fact]
    public void TestReadFloatAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadFloat();
        });
    }

    [Fact]
    public void TestReadFloatLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadFloatLE();
        });
    }

    [Fact]
    public void TestReadDoubleAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadDouble();
        });
    }

    [Fact]
    public void TestReadDoubleLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadDoubleLE();
        });
    }

    [Fact]
    public void GetBooleanBoundaryCheck1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetBoolean(-1);
        });
    }

    [Fact]
    public void GetBooleanBoundaryCheck2()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetBoolean(buffer.Capacity);
        });
    }

    [Fact]
    public void GetByteBoundaryCheck1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetByte(-1);
        });
    }

    [Fact]
    public void GetByteBoundaryCheck2()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetByte(buffer.Capacity);
        });
    }

    [Fact]
    public void GetShortBoundaryCheck1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetShort(-1);
        });
    }

    [Fact]
    public void GetShortBoundaryCheck2()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetShort(buffer.Capacity - 1);
        });
    }

    [Fact]
    public void GetMediumBoundaryCheck1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetMedium(-1);
        });
    }

    [Fact]
    public void GetMediumBoundaryCheck2()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetMedium(buffer.Capacity - 2);
        });
    }

    [Fact]
    public void GetIntBoundaryCheck1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetInt(-1);
        });
    }

    [Fact]
    public void GetIntBoundaryCheck2()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetInt(buffer.Capacity - 3);
        });
    }

    [Fact]
    public void GetLongBoundaryCheck1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetLong(-1);
        });
    }

    [Fact]
    public void GetLongBoundaryCheck2()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetLong(buffer.Capacity - 7);
        });
    }

    [Fact]
    public void ReadByteThrowsIndexOutOfBoundsException()
    {
        ByteBuf buffer = NewBuffer(8);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                buffer.WriteByte(0);
                Assert.Equal((byte)0, buffer.ReadByte());
                buffer.ReadByte();
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    private void AssertEnsureWritableDoesNotThrow(bool force)
    {
        ByteBuf buffer = NewBuffer(8);
        buffer.WriterIndex = buffer.Capacity;
        buffer.EnsureWritable(8, force);
        buffer.Release();
    }

    private void AssertEnsureWritableReadOnly(bool force)
    {
        ByteBuf buffer = NewBuffer(8);
        buffer.WriterIndex = buffer.Capacity;
        Assert.Equal(1, buffer.AsReadOnly().EnsureWritable(8, force));
        buffer.Release();
    }

}
