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

using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression;

/**
 * Extends <tt>io.netty.handler.codec.http.websocketx.extensions.compression.WebSocketClientExtensionHandler</tt>
 * to handle the most common WebSocket Compression Extensions.
 *
 * See <tt>io.netty.example.http.websocketx.client.WebSocketClient</tt> for usage.
 */
// CLR: @ChannelHandler.Sharable is the Transport's [Sharable]. The @Deprecated INSTANCE field names its
// replacement WebSocketClientCompressionHandler(int) and is not ported, and with it the private no-argument
// constructor, which only INSTANCE called.
[Sharable]
public sealed class WebSocketClientCompressionHandler : WebSocketClientExtensionHandler
{

    /**
     * Constructor with default configuration.
     * @param maxAllocation
     *            Maximum size of the decompression buffer. Must be &gt;= 0. If zero, maximum size is not limited.
     */
    public WebSocketClientCompressionHandler(int maxAllocation)
        : base(new PerMessageDeflateClientExtensionHandshaker(maxAllocation),
                new DeflateFrameClientExtensionHandshaker(false, maxAllocation),
                new DeflateFrameClientExtensionHandshaker(true, maxAllocation))
    {
    }

}
