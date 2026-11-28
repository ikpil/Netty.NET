/*
 * Copyright 2025 The Netty Project
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

namespace Netty.NET.Handler.Codec.Compression;

/**
 * Special decompressor implementation that buffers input so that it can be processed piecemeal, similar to
 * {@link io.netty.handler.codec.ByteToMessageDecoder}.
 */
// CLR: the package-private abstract class is public with an internal constructor, because its subclasses
// JdkZlibDecompressor, SnappyFrameDecompressor (and the not-ported Lz4FrameDecompressor and LzfDecompressor) are
// public and a C# class cannot be more accessible than its base class; the internal constructor keeps it closed to
// other assemblies as the package-private constructor does. The package-private processInput, processOutput and
// available() are internal. An abstract C# class that implements an interface declares the members it leaves to
// subclasses (status(), endOfInput()) as public abstract. The final addInput/takeOutput are non-virtual and
// close() is virtual. Unpooled.EMPTY_BUFFER is the Buffer port's Unpooled.EmptyBuffer, and catch (Throwable t)
// {...; throw t; } is catch (Exception) {...; throw; }.
public abstract class InputBufferingDecompressor : IDecompressor
{
    protected readonly IByteBufAllocator allocator;
    private ByteBuf cumulation;

    internal InputBufferingDecompressor(IByteBufAllocator allocator)
    {
        this.allocator = allocator;
    }

    public abstract IDecompressor.Status Status_();

    public abstract void EndOfInput();

    public void AddInput(ByteBuf buf)
    {
        if (!buf.IsReadable())
        {
            buf.Release();
            return;
        }

        if (this.cumulation != null)
        {
            buf = ByteToMessageDecoder.MERGE_CUMULATOR.Cumulate(allocator, this.cumulation, buf);
            this.cumulation = null;
        }

        try
        {
            ProcessInput(buf);
        }
        catch (Exception)
        {
            buf.Release();
            throw;
        }

        if (buf.IsReadable())
        {
            this.cumulation = buf;
        }
        else
        {
            buf.Release();
        }
    }

    public ByteBuf TakeOutput()
    {
        ByteBuf buf = cumulation == null ? Unpooled.EmptyBuffer : cumulation;
        ByteBuf output = ProcessOutput(buf);

        try
        {
            if (Status_() == IDecompressor.Status.NEED_INPUT && buf.IsReadable())
            {
                ProcessInput(buf);
            }
        }
        catch (Exception)
        {
            output.Release();
            throw;
        }

        if (this.cumulation != null && !this.cumulation.IsReadable())
        {
            this.cumulation.Release();
            this.cumulation = null;
        }

        return output;
    }

    /**
     * Process some input. The input buffer ownership <i>does not</i> transfer to this method: If there's still data
     * unread after this method finishes, it will be buffered.
     *
     * @param buf The input buffer
     */
    internal abstract void ProcessInput(ByteBuf buf);

    /**
     * Produce some output. The input buffer parameter may be read from to consume some more data, but note that this
     * method <i>must</i> return a buffer even if the input is too short.
     *
     * @param buf The input buffer
     */
    internal abstract ByteBuf ProcessOutput(ByteBuf buf);

    /**
     * Number of buffered bytes.
     *
     * @return Number of buffered bytes
     */
    internal int Available()
    {
        return cumulation == null ? 0 : cumulation.ReadableBytes;
    }

    public virtual void Close()
    {
        if (this.cumulation != null)
        {
            this.cumulation.Release();
            this.cumulation = null;
        }
    }
}
