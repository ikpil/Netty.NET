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
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * Base class for web socket frames.
 */
// CLR: the Java overrides narrow the DefaultByteBufHolder return types; they are covariant overrides ("override
// methods support covariant return types. In particular, the return type of an override method can derive from the
// return type of the corresponding base method",
// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override), as codec-smtp's
// DefaultSmtpContent does, and the abstract replace(ByteBuf) is an abstract override. content() is the Content
// property and contentToString() is ContentToString() of the Buffer port's DefaultByteBufHolder.
public abstract class WebSocketFrame : DefaultByteBufHolder
{

    /**
     * Flag to indicate if this frame is the final fragment in a message. The first fragment (frame) may also be the
     * final fragment.
     */
    private readonly bool finalFragment;

    /**
     * RSV1, RSV2, RSV3 used for extensions
     */
    private readonly int rsv;

    protected WebSocketFrame(ByteBuf binaryData) : this(true, 0, binaryData)
    {
    }

    protected WebSocketFrame(bool finalFragment, int rsv, ByteBuf binaryData) : base(binaryData)
    {
        this.finalFragment = finalFragment;
        this.rsv = rsv;
    }

    /**
     * Flag to indicate if this frame is the final fragment in a message. The first fragment (frame) may also be the
     * final fragment.
     */
    public bool IsFinalFragment()
    {
        return finalFragment;
    }

    /**
     * Bits used for extensions to the standard.
     */
    public int Rsv()
    {
        return rsv;
    }

    public override WebSocketFrame Copy()
    {
        return (WebSocketFrame) base.Copy();
    }

    public override WebSocketFrame Duplicate()
    {
        return (WebSocketFrame) base.Duplicate();
    }

    public override WebSocketFrame RetainedDuplicate()
    {
        return (WebSocketFrame) base.RetainedDuplicate();
    }

    public abstract override WebSocketFrame Replace(ByteBuf content);

    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + "(data: " + ContentToString() + ')';
    }

    public override WebSocketFrame Retain()
    {
        base.Retain();
        return this;
    }

    public override WebSocketFrame Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override WebSocketFrame Touch()
    {
        base.Touch();
        return this;
    }

    public override WebSocketFrame Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }
}
