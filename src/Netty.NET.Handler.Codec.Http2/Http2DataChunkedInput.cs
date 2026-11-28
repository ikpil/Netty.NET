/*
 * Copyright 2022 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Stream;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * A {@link ChunkedInput} that fetches data chunk by chunk for use with HTTP/2 Data Frames.
 * <p>
 * Each chunk from the input data will be wrapped within a {@link Http2DataFrame}. At the end of the input data,
 * {@link Http2DataFrame#isEndStream()} will be set to true and will be written.
 * <p>
 * <p>
 * <pre>
 *
 *     public void channelRead(ChannelHandlerContext ctx, Object msg) throws Exception {
 *         if (msg instanceof Http2HeadersFrame) {
 *             Http2HeadersFrame http2HeadersFrame = (Http2HeadersFrame) msg;
 *
 *             Http2HeadersFrame response = new DefaultHttp2HeadersFrame(new DefaultHttp2Headers().status("200"));
 *             response.stream(http2HeadersFrame.stream());
 *             ctx.write(response);
 *
 *             ChannelFuture sendFileFuture = ctx.writeAndFlush(new Http2DataChunkedInput(
 *                     new ChunkedFile(new File(("/home/meow/cats.mp4"))), http2HeadersFrame.stream()));
 *         }
 *     }
 * </pre>
 */
// CLR: ChunkedInput<Http2DataFrame> is the handler port's IChunkedInput<IHttp2DataFrame> and the wrapped
// ChunkedInput<ByteBuf> is IChunkedInput<ByteBuf> (src/Netty.NET.Handler/Stream/IChunkedInput.cs), as codec-http's
// HttpChunkedInput (src/Netty.NET.Handler.Codec.Http/HttpChunkedInput.cs). The @Deprecated
// readChunk(ChannelHandlerContext) override has no counterpart: the interface member it overrides is @Deprecated with
// the replacement readChunk(ByteBufAllocator) named and is not ported (owner decision 2026-11-16, recorded in
// IChunkedInput.cs); callers pass ctx.Alloc() to ReadChunk(IByteBufAllocator). checkNotNull is the Common
// ObjectUtil.CheckNotNull (ArgumentNullException, docs/codec-base-porting.md). "throws Exception" has no C#
// counterpart. The final class is sealed.
public sealed class Http2DataChunkedInput : IChunkedInput<IHttp2DataFrame>
{

    private readonly IChunkedInput<ByteBuf> input;
    private readonly IHttp2FrameStream stream;
    private bool endStreamSent;

    /**
     * Creates a new instance using the specified input.
     *
     * @param input  {@link ChunkedInput} containing data to write
     * @param stream {@link Http2FrameStream} holding stream info
     */
    public Http2DataChunkedInput(IChunkedInput<ByteBuf> input, IHttp2FrameStream stream)
    {
        this.input = ObjectUtil.CheckNotNull(input, "input");
        this.stream = ObjectUtil.CheckNotNull(stream, "stream");
    }

    public bool IsEndOfInput()
    {
        if (input.IsEndOfInput())
        {
            // Only end of input after last HTTP chunk has been sent
            return endStreamSent;
        }

        return false;
    }

    public void Close()
    {
        input.Close();
    }

    public IHttp2DataFrame ReadChunk(IByteBufAllocator allocator)
    {
        if (endStreamSent)
        {
            return null;
        }

        if (input.IsEndOfInput())
        {
            endStreamSent = true;
            return new DefaultHttp2DataFrame(true).Stream(stream);
        }

        ByteBuf buf = input.ReadChunk(allocator);
        if (buf == null)
        {
            return null;
        }

        IHttp2DataFrame dataFrame = new DefaultHttp2DataFrame(buf, input.IsEndOfInput()).Stream(stream);
        if (dataFrame.IsEndStream())
        {
            endStreamSent = true;
        }

        return dataFrame;
    }

    public long Length()
    {
        return input.Length();
    }

    public long Progress()
    {
        return input.Progress();
    }
}
