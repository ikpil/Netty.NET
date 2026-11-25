/*
 * Copyright 2016 The Netty Project
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
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Stream;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * A {@link ChunkedInput} that fetches data chunk by chunk for use with WebSocket chunked transfers.
 * <p>
 * Each chunk from the input data will be wrapped within a {@link ContinuationWebSocketFrame}.
 * At the end of the input data, {@link ContinuationWebSocketFrame} with finalFragment will be written.
 * <p>
 */
// CLR: ChunkedInput<WebSocketFrame> is the handler port's IChunkedInput<WebSocketFrame> and the wrapped
// ChunkedInput<ByteBuf> is IChunkedInput<ByteBuf> (src/Netty.NET.Handler/Stream/IChunkedInput.cs). The @Deprecated
// readChunk(ChannelHandlerContext) override has no counterpart: the interface member it overrides is @Deprecated with
// the replacement readChunk(ByteBufAllocator) named and is not ported (owner decision 2026-11-16, recorded in
// IChunkedInput.cs), as core's HttpChunkedInput. checkNotNull is the Common ObjectUtil.CheckNotNull
// (ArgumentNullException for the NullPointerException of the Javadoc), and "throws Exception" has no C# counterpart.
public sealed class WebSocketChunkedInput : IChunkedInput<WebSocketFrame>
{
    private readonly IChunkedInput<ByteBuf> input;
    private readonly int rsv;

    /**
     * Creates a new instance using the specified input.
     * @param input {@link ChunkedInput} containing data to write
     */
    public WebSocketChunkedInput(IChunkedInput<ByteBuf> input) : this(input, 0)
    {
    }

    /**
     * Creates a new instance using the specified input.
     * @param input {@link ChunkedInput} containing data to write
     * @param rsv RSV1, RSV2, RSV3 used for extensions
     *
     * @throws  NullPointerException if {@code input} is null
     */
    public WebSocketChunkedInput(IChunkedInput<ByteBuf> input, int rsv)
    {
        this.input = ObjectUtil.CheckNotNull(input, "input");
        this.rsv = rsv;
    }

    /**
     * @return {@code true} if and only if there is no data left in the stream
     * and the stream has reached at its end.
     */
    public bool IsEndOfInput()
    {
        return input.IsEndOfInput();
    }

    /**
     * Releases the resources associated with the input.
     */
    public void Close()
    {
        input.Close();
    }

    /**
     * Fetches a chunked data from the stream. Once this method returns the last chunk
     * and thus the stream has reached at its end, any subsequent {@link #isEndOfInput()}
     * call must return {@code true}.
     *
     * @param allocator {@link ByteBufAllocator}
     * @return {@link WebSocketFrame} contain chunk of data
     */
    public WebSocketFrame ReadChunk(IByteBufAllocator allocator)
    {
        ByteBuf buf = input.ReadChunk(allocator);
        if (buf == null)
        {
            return null;
        }
        return new ContinuationWebSocketFrame(input.IsEndOfInput(), rsv, buf);
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
