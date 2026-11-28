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

namespace Netty.NET.Handler.Codec.Http.WebSocketx.Extensions;

/**
 * Convenient class for <tt>io.netty.handler.codec.http.websocketx.extensions.WebSocketExtension</tt> decoder.
 */
// CLR: MessageToMessageDecoder<WebSocketFrame> is the codec-base generic and super(WebSocketFrame.class) is
// base(typeof(WebSocketFrame)).
public abstract class WebSocketExtensionDecoder : MessageToMessageDecoder<WebSocketFrame>
{

    public WebSocketExtensionDecoder() : base(typeof(WebSocketFrame))
    {
    }

}
