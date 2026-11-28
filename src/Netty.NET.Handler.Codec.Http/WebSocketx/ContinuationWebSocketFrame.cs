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

using System.Text;
using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * Web Socket continuation frame containing continuation text or binary data. This is used for
 * fragmented messages where the contents of a messages is contained more than 1 frame.
 */
// CLR: The Java overrides that narrow the return types of WebSocketFrame are C# 9 covariant overrides (see
// WebSocketFrame).
public class ContinuationWebSocketFrame : WebSocketFrame
{

    /**
     * Creates a new empty continuation frame.
     */
    public ContinuationWebSocketFrame() : this(Unpooled.Buffer(0))
    {
    }

    /**
     * Creates a new continuation frame with the specified binary data. The final fragment flag is
     * set to true.
     *
     * @param binaryData the content of the frame.
     */
    public ContinuationWebSocketFrame(ByteBuf binaryData) : base(binaryData)
    {
    }

    /**
     * Creates a new continuation frame with the specified binary data.
     *
     * @param finalFragment
     *            flag indicating if this frame is the final fragment
     * @param rsv
     *            reserved bits used for protocol extensions
     * @param binaryData
     *            the content of the frame.
     */
    public ContinuationWebSocketFrame(bool finalFragment, int rsv, ByteBuf binaryData)
        : base(finalFragment, rsv, binaryData)
    {
    }

    /**
     * Creates a new continuation frame with the specified text data
     *
     * @param finalFragment
     *            flag indicating if this frame is the final fragment
     * @param rsv
     *            reserved bits used for protocol extensions
     * @param text
     *            text content of the frame.
     */
    public ContinuationWebSocketFrame(bool finalFragment, int rsv, string text)
        : this(finalFragment, rsv, FromText(text))
    {
    }

    /**
     * Returns the text data in this frame.
     */
    public string Text()
    {
        return Content.ToString(Encoding.UTF8);
    }

    /**
     * Sets the string for this frame.
     *
     * @param text
     *            text to store.
     */
    private static ByteBuf FromText(string text)
    {
        if (text == null || text.Length == 0)
        {
            return Unpooled.EmptyBuffer;
        }
        else
        {
            return Unpooled.CopiedBuffer(text, Encoding.UTF8);
        }
    }

    public override ContinuationWebSocketFrame Copy()
    {
        return (ContinuationWebSocketFrame) base.Copy();
    }

    public override ContinuationWebSocketFrame Duplicate()
    {
        return (ContinuationWebSocketFrame) base.Duplicate();
    }

    public override ContinuationWebSocketFrame RetainedDuplicate()
    {
        return (ContinuationWebSocketFrame) base.RetainedDuplicate();
    }

    public override ContinuationWebSocketFrame Replace(ByteBuf content)
    {
        return new ContinuationWebSocketFrame(IsFinalFragment(), Rsv(), content);
    }

    public override ContinuationWebSocketFrame Retain()
    {
        base.Retain();
        return this;
    }

    public override ContinuationWebSocketFrame Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override ContinuationWebSocketFrame Touch()
    {
        base.Touch();
        return this;
    }

    public override ContinuationWebSocketFrame Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }
}
