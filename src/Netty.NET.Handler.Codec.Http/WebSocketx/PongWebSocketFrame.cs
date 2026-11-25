/*
 * Copyright 2012 The Netty Project
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

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * Web Socket frame containing binary data.
 */
// CLR: The Java overrides that narrow the return types of WebSocketFrame are C# 9 covariant overrides (see
// WebSocketFrame).
public class PongWebSocketFrame : WebSocketFrame
{

    /**
     * Creates a new empty pong frame.
     */
    public PongWebSocketFrame() : base(Unpooled.Buffer(0))
    {
    }

    /**
     * Creates a new pong frame with the specified binary data.
     *
     * @param binaryData
     *            the content of the frame.
     */
    public PongWebSocketFrame(ByteBuf binaryData) : base(binaryData)
    {
    }

    /**
     * Creates a new pong frame with the specified binary data
     *
     * @param finalFragment
     *            flag indicating if this frame is the final fragment
     * @param rsv
     *            reserved bits used for protocol extensions
     * @param binaryData
     *            the content of the frame.
     */
    public PongWebSocketFrame(bool finalFragment, int rsv, ByteBuf binaryData) : base(finalFragment, rsv, binaryData)
    {
    }

    public override PongWebSocketFrame Copy()
    {
        return (PongWebSocketFrame) base.Copy();
    }

    public override PongWebSocketFrame Duplicate()
    {
        return (PongWebSocketFrame) base.Duplicate();
    }

    public override PongWebSocketFrame RetainedDuplicate()
    {
        return (PongWebSocketFrame) base.RetainedDuplicate();
    }

    public override PongWebSocketFrame Replace(ByteBuf content)
    {
        return new PongWebSocketFrame(IsFinalFragment(), Rsv(), content);
    }

    public override PongWebSocketFrame Retain()
    {
        base.Retain();
        return this;
    }

    public override PongWebSocketFrame Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override PongWebSocketFrame Touch()
    {
        base.Touch();
        return this;
    }

    public override PongWebSocketFrame Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }
}
