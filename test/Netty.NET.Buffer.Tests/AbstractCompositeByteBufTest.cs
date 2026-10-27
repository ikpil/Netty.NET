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
using System.Collections.Generic;

namespace Netty.NET.Buffer.Tests;

/**
 * An abstract test class for composite channel buffers
 */
public abstract partial class AbstractCompositeByteBufTest : AbstractByteBufTest
{
    protected override ByteBuf NewBuffer(int length, int maxCapacity)
    {
        Assert.Equal(int.MaxValue, maxCapacity);
        var buffers = new List<ByteBuf>();
        for (int i = 0; i < length + 45; i += 45)
        {
            buffers.Add(Unpooled.EmptyBuffer);
            buffers.Add(Unpooled.WrappedBuffer(new byte[1]));
            buffers.Add(Unpooled.EmptyBuffer);
            buffers.Add(Unpooled.WrappedBuffer(new byte[2]));
            buffers.Add(Unpooled.EmptyBuffer);
            buffers.Add(Unpooled.WrappedBuffer(new byte[3]));
            buffers.Add(Unpooled.EmptyBuffer);
            buffers.Add(Unpooled.WrappedBuffer(new byte[4]));
            buffers.Add(Unpooled.EmptyBuffer);
            buffers.Add(Unpooled.WrappedBuffer(new byte[5]));
            buffers.Add(Unpooled.EmptyBuffer);
            buffers.Add(Unpooled.WrappedBuffer(new byte[6]));
            buffers.Add(Unpooled.EmptyBuffer);
            buffers.Add(Unpooled.WrappedBuffer(new byte[7]));
            buffers.Add(Unpooled.EmptyBuffer);
            buffers.Add(Unpooled.WrappedBuffer(new byte[8]));
            buffers.Add(Unpooled.EmptyBuffer);
            buffers.Add(Unpooled.WrappedBuffer(new byte[9]));
            buffers.Add(Unpooled.EmptyBuffer);
        }

        ByteBuf buffer;
        // Ensure that we are really testing a CompositeByteBuf
        switch (buffers.Count)
        {
            case 0:
                buffer = Unpooled.CompositeBuffer(int.MaxValue);
                break;
            case 1:
                buffer = Unpooled.CompositeBuffer(int.MaxValue).AddComponent(buffers[0]);
                break;
            default:
                buffer = Unpooled.WrappedBuffer(int.MaxValue, buffers.ToArray());
                break;
        }
        // CLR has explicit BE/LE operations and no mutable byte-order facade.

        // Truncate to the requested capacity.
        buffer.Capacity = length;

        Assert.Equal(length, buffer.Capacity);
        Assert.Equal(length, buffer.ReadableBytes);
        Assert.False(buffer.IsWritable);
        buffer.WriterIndex = 0;
        return buffer;
    }

    // Composite buffer does not waste bandwidth on discardReadBytes, but
    // the test will fail in strict mode.
    protected override bool DiscardReadBytesDoesNotMoveWritableBytes()
    {
        return false;
    }
}
