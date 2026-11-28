/*
 * Copyright 2019 The Netty Project
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
using System.Globalization;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * Web Socket Frame for closing the connection.
 */
// CLR: ByteBufAllocator is IByteBufAllocator and UnpooledByteBufAllocator.DEFAULT is
// UnpooledByteBufAllocator.Default. binaryData.writeCharSequence(reasonText, UTF_8) is ByteBufUtil.WriteUtf8, the
// Java UTF-8 path of writeCharSequence (an unpaired surrogate becomes '?'), and toString(index, length, UTF_8) is
// ToString(index, length, Encoding.UTF8). The package-private requireValidStatusCode is internal;
// IllegalArgumentException is ArgumentException and the status code in its message uses the invariant culture. The
// Java overrides that narrow the return types of WebSocketFrame are C# 9 covariant overrides (see WebSocketFrame).
public class CloseWebSocketFrame : WebSocketFrame
{

    /**
     * Creates a new empty close frame.
     */
    public CloseWebSocketFrame() : base(Unpooled.Buffer(0))
    {
    }

    /**
     * Creates a new empty close frame with closing status code and reason text
     *
     * @param status
     *            Status code as per <a href="https://tools.ietf.org/html/rfc6455#section-7.4">RFC 6455</a>. For
     *            example, <tt>1000</tt> indicates normal closure.
     */
    public CloseWebSocketFrame(WebSocketCloseStatus status)
        : this(RequireValidStatusCode(status.Code()), status.ReasonText())
    {
    }

    /**
     * Creates a new empty close frame with closing status code and reason text, using the provided ByteBuf allocator.
     *
     * @param status
     *            Status code as per <a href="https://tools.ietf.org/html/rfc6455#section-7.4">RFC 6455</a>. For
     *            example, <tt>1000</tt> indicates normal closure.
     */
    public CloseWebSocketFrame(WebSocketCloseStatus status, IByteBufAllocator allocator)
        : this(true, 0, allocator, RequireValidStatusCode(status.Code()), status.ReasonText())
    {
    }

    /**
     * Creates a new empty close frame with closing status code and reason text
     *
     * @param status
     *            Status code as per <a href="https://tools.ietf.org/html/rfc6455#section-7.4">RFC 6455</a>. For
     *            example, <tt>1000</tt> indicates normal closure.
     * @param reasonText
     *            Reason text. Set to null if no text.
     */
    public CloseWebSocketFrame(WebSocketCloseStatus status, string reasonText)
        : this(RequireValidStatusCode(status.Code()), reasonText)
    {
    }

    /**
     * Creates a new empty close frame with closing status code and reason text
     *
     * @param statusCode
     *            Integer status code as per <a href="https://tools.ietf.org/html/rfc6455#section-7.4">RFC 6455</a>. For
     *            example, <tt>1000</tt> indicates normal closure.
     * @param reasonText
     *            Reason text. Set to null if no text.
     */
    public CloseWebSocketFrame(int statusCode, string reasonText)
        : this(true, 0, RequireValidStatusCode(statusCode), reasonText)
    {
    }

    /**
     * Creates a new close frame with no losing status code and no reason text
     *
     * @param finalFragment
     *            flag indicating if this frame is the final fragment
     * @param rsv
     *            reserved bits used for protocol extensions.
     */
    public CloseWebSocketFrame(bool finalFragment, int rsv) : this(finalFragment, rsv, Unpooled.Buffer(0))
    {
    }

    /**
     * Creates a new close frame with closing status code and reason text
     *
     * @param finalFragment
     *            flag indicating if this frame is the final fragment
     * @param rsv
     *            reserved bits used for protocol extensions
     * @param statusCode
     *            Integer status code as per <a href="https://tools.ietf.org/html/rfc6455#section-7.4">RFC 6455</a>. For
     *            example, <tt>1000</tt> indicates normal closure.
     * @param reasonText
     *            Reason text. Set to null if no text.
     */
    public CloseWebSocketFrame(bool finalFragment, int rsv, int statusCode, string reasonText)
        : this(finalFragment, rsv, UnpooledByteBufAllocator.Default, statusCode, reasonText)
    {
    }

    /**
     * Creates a new close frame with closing status code and reason text
     *
     * @param finalFragment
     *            flag indicating if this frame is the final fragment
     * @param rsv
     *            reserved bits used for protocol extensions
     * @param allocator
     *            the ByteBuf allocator to use to build the binary data.
     * @param statusCode
     *            Integer status code as per <a href="https://tools.ietf.org/html/rfc6455#section-7.4">RFC 6455</a>. For
     *            example, <tt>1000</tt> indicates normal closure.
     * @param reasonText
     *            Reason text. Set to null if no text.
     */
    public CloseWebSocketFrame(bool finalFragment, int rsv, IByteBufAllocator allocator,
                               int statusCode, string reasonText)
        : base(finalFragment, rsv, NewBinaryData(allocator, RequireValidStatusCode(statusCode), reasonText))
    {
    }

    private static ByteBuf NewBinaryData(IByteBufAllocator allocator, int statusCode, string reasonText)
    {
        if (reasonText == null)
        {
            reasonText = StringUtil.EMPTY_STRING;
        }

        ByteBuf binaryData = allocator.Buffer(2 + reasonText.Length);
        binaryData.WriteShort(statusCode);

        if (reasonText.Length != 0)
        {
            ByteBufUtil.WriteUtf8(binaryData, reasonText);
        }

        return binaryData;
    }

    /**
     * Creates a new close frame
     *
     * @param finalFragment
     *            flag indicating if this frame is the final fragment
     * @param rsv
     *            reserved bits used for protocol extensions
     * @param binaryData
     *            the content of the frame. Must be 2 byte integer followed by optional UTF-8 encoded string.
     */
    public CloseWebSocketFrame(bool finalFragment, int rsv, ByteBuf binaryData) : base(finalFragment, rsv, binaryData)
    {
    }

    /**
     * Returns the closing status code as per <a href="https://tools.ietf.org/html/rfc6455#section-7.4">RFC 6455</a>. If
     * a status code is set, -1 is returned.
     */
    public int StatusCode()
    {
        ByteBuf binaryData = Content;
        if (binaryData == null || binaryData.ReadableBytes < 2)
        {
            return -1;
        }

        return binaryData.GetUnsignedShort(binaryData.ReaderIndex);
    }

    /**
     * Returns the reason text as per <a href="https://tools.ietf.org/html/rfc6455#section-7.4">RFC 6455</a> If a reason
     * text is not supplied, an empty string is returned.
     */
    public string ReasonText()
    {
        ByteBuf binaryData = Content;
        if (binaryData == null || binaryData.ReadableBytes <= 2)
        {
            return "";
        }

        return binaryData.ToString(binaryData.ReaderIndex + 2, binaryData.ReadableBytes - 2, Encoding.UTF8);
    }

    public override CloseWebSocketFrame Copy()
    {
        return (CloseWebSocketFrame) base.Copy();
    }

    public override CloseWebSocketFrame Duplicate()
    {
        return (CloseWebSocketFrame) base.Duplicate();
    }

    public override CloseWebSocketFrame RetainedDuplicate()
    {
        return (CloseWebSocketFrame) base.RetainedDuplicate();
    }

    public override CloseWebSocketFrame Replace(ByteBuf content)
    {
        return new CloseWebSocketFrame(IsFinalFragment(), Rsv(), content);
    }

    public override CloseWebSocketFrame Retain()
    {
        base.Retain();
        return this;
    }

    public override CloseWebSocketFrame Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override CloseWebSocketFrame Touch()
    {
        base.Touch();
        return this;
    }

    public override CloseWebSocketFrame Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }

    internal static int RequireValidStatusCode(int statusCode)
    {
        if (WebSocketCloseStatus.IsValidStatusCode(statusCode))
        {
            return statusCode;
        }
        else
        {
            throw new ArgumentException("WebSocket close status code does NOT comply with RFC-6455: " +
                    statusCode.ToString(CultureInfo.InvariantCulture));
        }
    }
}
