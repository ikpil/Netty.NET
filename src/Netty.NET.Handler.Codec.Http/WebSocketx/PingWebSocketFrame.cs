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
public class PingWebSocketFrame : WebSocketFrame
{

    /**
     * Creates a new empty ping frame.
     */
    public PingWebSocketFrame() : base(true, 0, Unpooled.Buffer(0))
    {
    }

    /**
     * Creates a new ping frame with the specified binary data.
     *
     * @param binaryData
     *            the content of the frame.
     */
    public PingWebSocketFrame(ByteBuf binaryData) : base(binaryData)
    {
    }

    /**
     * Creates a new ping frame with the specified binary data.
     *
     * @param finalFragment
     *            flag indicating if this frame is the final fragment
     * @param rsv
     *            reserved bits used for protocol extensions
     * @param binaryData
     *            the content of the frame.
     */
    public PingWebSocketFrame(bool finalFragment, int rsv, ByteBuf binaryData) : base(finalFragment, rsv, binaryData)
    {
    }

    public override PingWebSocketFrame Copy()
    {
        return (PingWebSocketFrame) base.Copy();
    }

    public override PingWebSocketFrame Duplicate()
    {
        return (PingWebSocketFrame) base.Duplicate();
    }

    public override PingWebSocketFrame RetainedDuplicate()
    {
        return (PingWebSocketFrame) base.RetainedDuplicate();
    }

    public override PingWebSocketFrame Replace(ByteBuf content)
    {
        return new PingWebSocketFrame(IsFinalFragment(), Rsv(), content);
    }

    public override PingWebSocketFrame Retain()
    {
        base.Retain();
        return this;
    }

    public override PingWebSocketFrame Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override PingWebSocketFrame Touch()
    {
        base.Touch();
        return this;
    }

    public override PingWebSocketFrame Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }
}
