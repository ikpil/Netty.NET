/*
 * Copyright 2021 The Netty Project
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
using Netty.NET.Buffer;
using Netty.NET.Handler.Stream;

namespace Netty.NET.Handler.Tests.Stream;

public class ChunkedStreamTest
{

    [Fact]
    public void WriteTest()
    {
        ChunkedStream chunkedStream = new ChunkedStream(new AvailableAtEndInputStream());

        Assert.False(chunkedStream.IsEndOfInput());
        Assert.Null(chunkedStream.ReadChunk(UnpooledByteBufAllocator.Default));
        Assert.Equal(0, chunkedStream.Progress());
        chunkedStream.Close();
        Assert.True(chunkedStream.IsEndOfInput());
        Assert.Null(chunkedStream.ReadChunk(UnpooledByteBufAllocator.Default));
    }

    // CLR: the anonymous InputStream whose read() returns -1 while available() returns 1. System.IO.Stream has no
    // available(); ChunkedStream reads it as Length - Position of a seekable stream, so this stream reports one byte
    // left (Length 1, Position 0) while Read returns 0, the end of the stream ("Read returns 0 only if zero bytes were
    // requested or when there is no more data in the stream",
    // https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.read?view=net-10.0). The other abstract Stream
    // members are not used.
    private sealed class AvailableAtEndInputStream : System.IO.Stream
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            return 0;
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => 1;

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
