/*
 * Copyright 2026 The Netty Project
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

using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: Decompressor is IDecompressor and status() is Status_() (see IDecompressor). ByteBufAllocator.DEFAULT is
// IByteBufAllocator.Default and refCnt() is the Buffer port's RefCnt property. The anonymous Executable is a lambda. The
// nested subclass overrides the internal processInput/processOutput of InputBufferingDecompressor as internal members
// (InternalsVisibleTo), and its package-private fields are internal. readByte() returns the unsigned byte 2, which equals
// the Java byte 2.
public class InputBufferingDecompressorTest
{

    [Fact]
    public void ReleasesEmptyInput()
    {
        TestInputBufferingDecompressor decompressor = new TestInputBufferingDecompressor();
        ByteBuf input = Unpooled.Buffer(0);

        decompressor.AddInput(input);

        Assert.Equal(0, input.RefCnt);
        Assert.Equal(0, decompressor.processInputCalls);
    }

    [Fact]
    public void BuffersUnreadInputUntilClose()
    {
        TestInputBufferingDecompressor decompressor = new TestInputBufferingDecompressor();
        ByteBuf input = Unpooled.Buffer(2).WriteByte(1).WriteByte(2);

        decompressor.AddInput(input);

        Assert.Equal(1, decompressor.processInputCalls);
        Assert.Equal(1, decompressor.Available());
        Assert.Equal(1, input.RefCnt);

        decompressor.Close();

        Assert.Equal(0, input.RefCnt);
    }

    [Fact]
    public void ReleasesInputWhenProcessingFails()
    {
        TestInputBufferingDecompressor decompressor = new TestInputBufferingDecompressor();
        decompressor.processInputException = new DecompressionException("input failed");
        ByteBuf input = Unpooled.Buffer(1).WriteByte(1);

        Assert.Same(decompressor.processInputException,
                Assert.Throws<DecompressionException>(() =>
                {
                    decompressor.AddInput(input);
                }));
        Assert.Equal(0, input.RefCnt);
    }

    [Fact]
    public void TakeOutputConsumesBufferedInput()
    {
        TestInputBufferingDecompressor decompressor = new TestInputBufferingDecompressor();
        ByteBuf input = Unpooled.Buffer(2).WriteByte(1).WriteByte(2);

        decompressor.AddInput(input);
        ByteBuf output = decompressor.TakeOutput();

        Assert.Equal(1, output.ReadableBytes);
        Assert.Equal(2, output.ReadByte());
        Assert.Equal(0, decompressor.Available());
        Assert.Equal(0, input.RefCnt);

        output.Release();
    }

    [Fact]
    public void TakeOutputReleasesOutputWhenStatusFails()
    {
        TestInputBufferingDecompressor decompressor = new TestInputBufferingDecompressor();
        decompressor.statusException = new DecompressionException("status failed");
        ByteBuf input = Unpooled.Buffer(2).WriteByte(1).WriteByte(2);

        decompressor.AddInput(input);

        Assert.Same(decompressor.statusException,
                Assert.Throws<DecompressionException>(() => decompressor.TakeOutput()));
        Assert.Equal(0, decompressor.lastOutput.RefCnt);
        decompressor.Close();
    }

    [Fact]
    public void CloseClearsCumulation()
    {
        TestInputBufferingDecompressor decompressor = new TestInputBufferingDecompressor();
        ByteBuf input = Unpooled.Buffer(2).WriteByte(1).WriteByte(2);

        decompressor.AddInput(input);
        decompressor.Close();
        decompressor.Close();

        Assert.Equal(0, input.RefCnt);
    }

    private sealed class TestInputBufferingDecompressor : InputBufferingDecompressor
    {
        internal int processInputCalls;
        internal DecompressionException processInputException;
        internal DecompressionException statusException;
        internal ByteBuf lastOutput;

        internal TestInputBufferingDecompressor() : base(IByteBufAllocator.Default)
        {
        }

        internal override void ProcessInput(ByteBuf buf)
        {
            processInputCalls++;
            if (processInputException != null)
            {
                throw processInputException;
            }

            if (buf.IsReadable())
            {
                buf.ReadByte();
            }
        }

        internal override ByteBuf ProcessOutput(ByteBuf buf)
        {
            ByteBuf output = allocator.Buffer(buf.ReadableBytes);
            output.WriteBytes(buf);
            lastOutput = output;
            return output;
        }

        public override void EndOfInput()
        {
        }

        public override IDecompressor.Status Status_()
        {
            if (statusException != null)
            {
                throw statusException;
            }

            return Available() == 0 ? IDecompressor.Status.NEED_INPUT : IDecompressor.Status.NEED_OUTPUT;
        }
    }
}
