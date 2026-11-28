/*
 * Copyright 2020 The Netty Project
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
using System.Collections.Generic;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Handler.Codec.Stomp;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Stomp.WebSocket;

// CLR: the source overrides the protected encode as public only to call it from StompWebSocketProtocolCodec; "An
// override declaration can't change the accessibility of the virtual method"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override), so Encode is a public new
// member that calls base.Encode (the codec-http2 builders widen protected methods the same way,
// docs/codec-http2-porting.md), and the encoder's own pipeline path still dispatches to StompSubframeEncoder.Encode,
// which the source's override only forwards to. The convert methods narrow their object returns to WebSocketFrame
// with C# 9 covariant override returns, as the source narrows them. The Java interfaces StompFrame,
// StompHeadersSubframe, StompContentSubframe, LastStompContentSubframe, StompSubframe and StompHeaders are the
// I-prefixed interfaces, List<Object> is IList<object>, and startsWith is an ordinal StartsWith.
public class StompWebSocketFrameEncoder : StompSubframeEncoder
{

    public new void Encode(IChannelHandlerContext ctx, IStompSubframe msg, IList<object> @out)
    {
        base.Encode(ctx, msg, @out);
    }

    protected override WebSocketFrame ConvertFullFrame(IStompFrame original, ByteBuf encoded)
    {
        if (IsTextFrame(original))
        {
            return new TextWebSocketFrame(encoded);
        }

        return new BinaryWebSocketFrame(encoded);
    }

    protected override WebSocketFrame ConvertHeadersSubFrame(IStompHeadersSubframe original, ByteBuf encoded)
    {
        if (IsTextFrame(original))
        {
            return new TextWebSocketFrame(false, 0, encoded);
        }

        return new BinaryWebSocketFrame(false, 0, encoded);
    }

    protected override WebSocketFrame ConvertContentSubFrame(IStompContentSubframe original, ByteBuf encoded)
    {
        if (original is ILastStompContentSubframe)
        {
            return new ContinuationWebSocketFrame(true, 0, encoded);
        }

        return new ContinuationWebSocketFrame(false, 0, encoded);
    }

    private static bool IsTextFrame(IStompHeadersSubframe headersSubframe)
    {
        string contentType = headersSubframe.Headers().GetAsString(IStompHeaders.CONTENT_TYPE);
        return contentType != null && (contentType.StartsWith("text", StringComparison.Ordinal) || contentType.StartsWith("application/json", StringComparison.Ordinal));
    }
}
