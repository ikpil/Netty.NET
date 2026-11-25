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
public class BinaryWebSocketFrame : WebSocketFrame
{

    /**
     * Creates a new empty binary frame.
     */
    public BinaryWebSocketFrame() : base(Unpooled.Buffer(0))
    {
    }

    /**
     * Creates a new binary frame with the specified binary data. The final fragment flag is set to true.
     *
     * @param binaryData
     *            the content of the frame.
     */
    public BinaryWebSocketFrame(ByteBuf binaryData) : base(binaryData)
    {
    }

    /**
     * Creates a new binary frame with the specified binary data and the final fragment flag.
     *
     * @param finalFragment
     *            flag indicating if this frame is the final fragment
     * @param rsv
     *            reserved bits used for protocol extensions
     * @param binaryData
     *            the content of the frame.
     */
    public BinaryWebSocketFrame(bool finalFragment, int rsv, ByteBuf binaryData) : base(finalFragment, rsv, binaryData)
    {
    }

    public override BinaryWebSocketFrame Copy()
    {
        return (BinaryWebSocketFrame) base.Copy();
    }

    public override BinaryWebSocketFrame Duplicate()
    {
        return (BinaryWebSocketFrame) base.Duplicate();
    }

    public override BinaryWebSocketFrame RetainedDuplicate()
    {
        return (BinaryWebSocketFrame) base.RetainedDuplicate();
    }

    public override BinaryWebSocketFrame Replace(ByteBuf content)
    {
        return new BinaryWebSocketFrame(IsFinalFragment(), Rsv(), content);
    }

    public override BinaryWebSocketFrame Retain()
    {
        base.Retain();
        return this;
    }

    public override BinaryWebSocketFrame Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override BinaryWebSocketFrame Touch()
    {
        base.Touch();
        return this;
    }

    public override BinaryWebSocketFrame Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }
}
