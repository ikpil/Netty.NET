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
 * Web Socket text frame.
 */
// CLR: CharsetUtil.UTF_8 is Encoding.UTF8 (docs/codec-smtp-porting.md). Unpooled.copiedBuffer(text, UTF_8) is
// Unpooled.CopiedBuffer(text, Encoding.UTF8), which writes an unpaired surrogate as EF BF BD where Java writes '?'
// (docs/buffer-porting.md, text copying), and content().toString(UTF_8) is Content.ToString(Encoding.UTF8). The Java
// overrides that narrow the return types of WebSocketFrame are C# 9 covariant overrides (see WebSocketFrame).
public class TextWebSocketFrame : WebSocketFrame
{

    /**
     * Creates a new empty text frame.
     */
    public TextWebSocketFrame() : base(Unpooled.Buffer(0))
    {
    }

    /**
     * Creates a new text frame with the specified text string. The final fragment flag is set to true.
     *
     * @param text
     *            String to put in the frame.
     */
    public TextWebSocketFrame(string text) : base(FromText(text))
    {
    }

    /**
     * Creates a new text frame with the specified binary data. The final fragment flag is set to true.
     *
     * @param binaryData
     *            the content of the frame.
     */
    public TextWebSocketFrame(ByteBuf binaryData) : base(binaryData)
    {
    }

    /**
     * Creates a new text frame with the specified text string. The final fragment flag is set to true.
     *
     * @param finalFragment
     *            flag indicating if this frame is the final fragment
     * @param rsv
     *            reserved bits used for protocol extensions
     * @param text
     *            String to put in the frame.
     */
    public TextWebSocketFrame(bool finalFragment, int rsv, string text) : base(finalFragment, rsv, FromText(text))
    {
    }

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

    /**
     * Creates a new text frame with the specified binary data and the final fragment flag.
     *
     * @param finalFragment
     *            flag indicating if this frame is the final fragment
     * @param rsv
     *            reserved bits used for protocol extensions
     * @param binaryData
     *            the content of the frame.
     */
    public TextWebSocketFrame(bool finalFragment, int rsv, ByteBuf binaryData) : base(finalFragment, rsv, binaryData)
    {
    }

    /**
     * Returns the text data in this frame.
     */
    public string Text()
    {
        return Content.ToString(Encoding.UTF8);
    }

    public override TextWebSocketFrame Copy()
    {
        return (TextWebSocketFrame) base.Copy();
    }

    public override TextWebSocketFrame Duplicate()
    {
        return (TextWebSocketFrame) base.Duplicate();
    }

    public override TextWebSocketFrame RetainedDuplicate()
    {
        return (TextWebSocketFrame) base.RetainedDuplicate();
    }

    public override TextWebSocketFrame Replace(ByteBuf content)
    {
        return new TextWebSocketFrame(IsFinalFragment(), Rsv(), content);
    }

    public override TextWebSocketFrame Retain()
    {
        base.Retain();
        return this;
    }

    public override TextWebSocketFrame Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override TextWebSocketFrame Touch()
    {
        base.Touch();
        return this;
    }

    public override TextWebSocketFrame Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }
}
