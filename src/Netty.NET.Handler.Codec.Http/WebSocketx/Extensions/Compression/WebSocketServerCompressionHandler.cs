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

namespace Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression;

/**
 * Extends <tt>io.netty.handler.codec.http.websocketx.extensions.compression.WebSocketServerExtensionHandler</tt>
 * to handle the most common WebSocket Compression Extensions.
 *
 * See <tt>io.netty.example.http.websocketx.html5.WebSocketServer</tt> for usage.
 */
// CLR: the @Deprecated no-argument constructor names its replacement WebSocketServerCompressionHandler(int) and is
// not ported.
public class WebSocketServerCompressionHandler : WebSocketServerExtensionHandler
{

    /**
     * Constructor with default configuration.
     *
     * @param maxAllocation
     *            Maximum size of the decompression buffer. Must be &gt;= 0. If zero, maximum size is not limited.
     */
    public WebSocketServerCompressionHandler(int maxAllocation)
        : base(new PerMessageDeflateServerExtensionHandshaker(maxAllocation),
                new DeflateFrameServerExtensionHandshaker(
                        DeflateFrameServerExtensionHandshaker.DEFAULT_COMPRESSION_LEVEL, maxAllocation))
    {
    }

}
