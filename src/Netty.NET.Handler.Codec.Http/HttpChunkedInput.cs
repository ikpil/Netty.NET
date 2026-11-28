/*
 * Copyright 2014 The Netty Project
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
using Netty.NET.Handler.Stream;

namespace Netty.NET.Handler.Codec.Http;

/**
 * A {@link ChunkedInput} that fetches data chunk by chunk for use with HTTP chunked transfers.
 * <p>
 * Each chunk from the input data will be wrapped within a {@link HttpContent}. At the end of the input data,
 * {@link LastHttpContent} will be written.
 * <p>
 * Ensure that your HTTP response header contains {@code Transfer-Encoding: chunked}.
 * <p>
 * <pre>
 * public void messageReceived(ChannelHandlerContext ctx, FullHttpRequest request) throws Exception {
 *     HttpResponse response = new DefaultHttpResponse(HTTP_1_1, OK);
 *     response.headers().set(TRANSFER_ENCODING, CHUNKED);
 *     ctx.write(response);
 *
 *     HttpChunkedInput httpChunkWriter = new HttpChunkedInput(
 *         new ChunkedFile(&quot;/tmp/myfile.txt&quot;));
 *     ChannelFuture sendFileFuture = ctx.write(httpChunkWriter);
 * }
 * </pre>
 */
// CLR: ChunkedInput<HttpContent> is the handler port's IChunkedInput<IHttpContent> (src/Netty.NET.Handler/Stream/
// IChunkedInput.cs). Its @Deprecated readChunk(ChannelHandlerContext), whose @deprecated text names
// readChunk(ByteBufAllocator), is not ported with the interface member, so this class's @Deprecated override of it
// has no counterpart either. "throws Exception" has no C# counterpart. The public members are virtual because the
// Java class is not final.
public class HttpChunkedInput : IChunkedInput<IHttpContent>
{

    private readonly IChunkedInput<ByteBuf> input;
    private readonly ILastHttpContent lastHttpContent;
    private bool sentLastChunk;

    /**
     * Creates a new instance using the specified input.
     * @param input {@link ChunkedInput} containing data to write
     */
    public HttpChunkedInput(IChunkedInput<ByteBuf> input)
    {
        this.input = input;
        lastHttpContent = ILastHttpContent.EMPTY_LAST_CONTENT;
    }

    /**
     * Creates a new instance using the specified input. {@code lastHttpContent} will be written as the terminating
     * chunk.
     * @param input {@link ChunkedInput} containing data to write
     * @param lastHttpContent {@link LastHttpContent} that will be written as the terminating chunk. Use this for
     *            trailing headers. Ownership is transferred to this {@link HttpChunkedInput}.
     */
    public HttpChunkedInput(IChunkedInput<ByteBuf> input, ILastHttpContent lastHttpContent)
    {
        this.input = input;
        this.lastHttpContent = lastHttpContent;
    }

    public virtual bool IsEndOfInput()
    {
        if (input.IsEndOfInput())
        {
            // Only end of input after last HTTP chunk has been sent
            return sentLastChunk;
        }
        else
        {
            return false;
        }
    }

    public virtual void Close()
    {
        try
        {
            input.Close();
        }
        finally
        {
            if (!sentLastChunk)
            {
                sentLastChunk = true;
                lastHttpContent.Release();
            }
        }
    }

    public virtual IHttpContent ReadChunk(IByteBufAllocator allocator)
    {
        if (input.IsEndOfInput())
        {
            if (sentLastChunk)
            {
                return null;
            }
            else
            {
                // Send last chunk for this input
                sentLastChunk = true;
                return lastHttpContent;
            }
        }
        else
        {
            ByteBuf buf = input.ReadChunk(allocator);
            if (buf == null)
            {
                return null;
            }

            return new DefaultHttpContent(buf);
        }
    }

    public virtual long Length()
    {
        return input.Length();
    }

    public virtual long Progress()
    {
        return input.Progress();
    }
}
