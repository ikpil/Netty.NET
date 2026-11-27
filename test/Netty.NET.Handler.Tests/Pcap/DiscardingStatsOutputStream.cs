/*
 * Copyright 2023 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System;

namespace Netty.NET.Handler.Tests.Pcap;

// CLR: java.io.OutputStream is System.IO.Stream: write(int) is WriteByte(byte) and write(byte[], int, int) is
// Write(byte[], int, int), which the span-based Stream.Write of ByteBuf.ReadBytes(Stream, int) reaches through the base
// implementation, as OutputStream.write(byte[]) calls write(b, 0, b.length). The other abstract Stream members describe
// a write-only stream; flush() does nothing, as OutputStream.flush() "does nothing"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/OutputStream.html).
internal sealed class DiscardingStatsOutputStream : System.IO.Stream
{

    private int writesCount;

    public override void WriteByte(byte value)
    {
        // NO-OP
    }

    public override void Write(byte[] b, int off, int len)
    {
        writesCount++;
    }

    internal int WritesCalled()
    {
        return writesCount;
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, System.IO.SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }
}
