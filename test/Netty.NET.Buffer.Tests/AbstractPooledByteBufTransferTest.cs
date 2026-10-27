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
namespace Netty.NET.Buffer.Tests;

public abstract class AbstractPooledByteBufTransferTest : AbstractContiguousByteBufTest
{
    protected abstract ByteBuf Alloc(int length, int maxCapacity);

    protected override ByteBuf NewBuffer(int length, int maxCapacity)
    {
        ByteBuf buffer = Alloc(length, maxCapacity);

        // Testing if the writerIndex and readerIndex are correct when allocate and also after we reset the mark.
        Assert.Equal(0, buffer.WriterIndex);
        Assert.Equal(0, buffer.ReaderIndex);
        buffer.ResetReaderIndex();
        buffer.ResetWriterIndex();
        Assert.Equal(0, buffer.WriterIndex);
        Assert.Equal(0, buffer.ReaderIndex);
        return buffer;
    }
}
