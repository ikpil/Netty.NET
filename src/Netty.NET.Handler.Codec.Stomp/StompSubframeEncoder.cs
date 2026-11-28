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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Stomp;

/**
 * Encodes a {@link StompFrame} or a {@link StompSubframe} into a {@link ByteBuf}.
 */
// CLR: StompSubframe.class is typeof(IStompSubframe), IllegalArgumentException is ArgumentException and
// List<Object> out is IList<object> @out. PlatformDependent.throwException(e) rethrows the caught exception
// unchanged, which is throw; in the catch block ("use a throw; statement to re-throw the exception that the catch
// block handles",
// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/exception-handling-statements).
// ByteBufUtil.writeUtf8(ByteBuf, CharSequence) has no ICharSequence overload in the Buffer port; WriteUtf8 below
// picks the AsciiString overload for an AsciiString and the character overload (ReadOnlySpan<char> of ToString())
// otherwise, as the Java method dispatches on the AsciiString type. The enum command prints its constant name
// (https://learn.microsoft.com/en-us/dotnet/api/system.enum.tostring); numbers in messages use the invariant
// culture.
public class StompSubframeEncoder : MessageToMessageEncoder<IStompSubframe>
{

    private const int ESCAPE_HEADER_KEY_CACHE_LIMIT = 32;
    private const float DEFAULT_LOAD_FACTOR = 0.75f;
    private static readonly FastThreadLocal<EscapeHeaderKeyCache> ESCAPE_HEADER_KEY_CACHE =
            new EscapeHeaderKeyCacheThreadLocal();

    private sealed class EscapeHeaderKeyCacheThreadLocal : FastThreadLocal<EscapeHeaderKeyCache>
    {
        protected override EscapeHeaderKeyCache InitialValue()
        {
            EscapeHeaderKeyCache cache = new EscapeHeaderKeyCache();

            cache.Put(IStompHeaders.ACCEPT_VERSION, IStompHeaders.ACCEPT_VERSION);
            cache.Put(IStompHeaders.HOST, IStompHeaders.HOST);
            cache.Put(IStompHeaders.LOGIN, IStompHeaders.LOGIN);
            cache.Put(IStompHeaders.PASSCODE, IStompHeaders.PASSCODE);
            cache.Put(IStompHeaders.HEART_BEAT, IStompHeaders.HEART_BEAT);
            cache.Put(IStompHeaders.VERSION, IStompHeaders.VERSION);
            cache.Put(IStompHeaders.SESSION, IStompHeaders.SESSION);
            cache.Put(IStompHeaders.SERVER, IStompHeaders.SERVER);
            cache.Put(IStompHeaders.DESTINATION, IStompHeaders.DESTINATION);
            cache.Put(IStompHeaders.ID, IStompHeaders.ID);
            cache.Put(IStompHeaders.ACK, IStompHeaders.ACK);
            cache.Put(IStompHeaders.TRANSACTION, IStompHeaders.TRANSACTION);
            cache.Put(IStompHeaders.RECEIPT, IStompHeaders.RECEIPT);
            cache.Put(IStompHeaders.MESSAGE_ID, IStompHeaders.MESSAGE_ID);
            cache.Put(IStompHeaders.SUBSCRIPTION, IStompHeaders.SUBSCRIPTION);
            cache.Put(IStompHeaders.RECEIPT_ID, IStompHeaders.RECEIPT_ID);
            cache.Put(IStompHeaders.MESSAGE, IStompHeaders.MESSAGE);
            cache.Put(IStompHeaders.CONTENT_LENGTH, IStompHeaders.CONTENT_LENGTH);
            cache.Put(IStompHeaders.CONTENT_TYPE, IStompHeaders.CONTENT_TYPE);

            return cache;
        }
    }

    private sealed class EscapeHeaderKeyCache : LinkedHashMap<ICharSequence, ICharSequence>
    {
        internal EscapeHeaderKeyCache() : base(ESCAPE_HEADER_KEY_CACHE_LIMIT, true)
        {
        }

        internal void Put(ICharSequence key, ICharSequence value)
        {
            this[key] = value;
            KeyValuePair<ICharSequence, ICharSequence> eldest = this.First();
            if (RemoveEldestEntry(eldest))
            {
                Remove(eldest.Key);
            }
        }

        private bool RemoveEldestEntry(KeyValuePair<ICharSequence, ICharSequence> eldest)
        {
            return Count > ESCAPE_HEADER_KEY_CACHE_LIMIT;
        }
    }

    public StompSubframeEncoder() : base(typeof(IStompSubframe))
    {
    }

    protected override void Encode(IChannelHandlerContext ctx, IStompSubframe msg, IList<object> @out)
    {
        if (msg is IStompFrame)
        {
            IStompFrame stompFrame = (IStompFrame) msg;
            ByteBuf buf = EncodeFullFrame(stompFrame, ctx);

            @out.Add(ConvertFullFrame(stompFrame, buf));
        }
        else if (msg is IStompHeadersSubframe)
        {
            IStompHeadersSubframe stompHeadersSubframe = (IStompHeadersSubframe) msg;
            ByteBuf buf = ctx.Alloc().Buffer(HeadersSubFrameSize(stompHeadersSubframe));

            try
            {
                EncodeHeaders(stompHeadersSubframe, buf);
            }
            catch (Exception)
            {
                buf.Release();
                throw;
            }

            @out.Add(ConvertHeadersSubFrame(stompHeadersSubframe, buf));
        }
        else if (msg is IStompContentSubframe)
        {
            IStompContentSubframe stompContentSubframe = (IStompContentSubframe) msg;
            ByteBuf buf = EncodeContent(stompContentSubframe, ctx);

            @out.Add(ConvertContentSubFrame(stompContentSubframe, buf));
        }
    }

    /**
     * An extension method to convert a STOMP encoded buffer to a different message type
     * based on an original {@link StompFrame} full frame.
     *
     * <p>By default an encoded buffer is returned as is.
     */
    protected virtual object ConvertFullFrame(IStompFrame original, ByteBuf encoded)
    {
        return encoded;
    }

    /**
     * An extension method to convert a STOMP encoded buffer to a different message type
     * based on an original {@link StompHeadersSubframe} headers sub frame.
     *
     * <p>By default an encoded buffer is returned as is.
     */
    protected virtual object ConvertHeadersSubFrame(IStompHeadersSubframe original, ByteBuf encoded)
    {
        return encoded;
    }

    /**
     * An extension method to convert a STOMP encoded buffer to a different message type
     * based on an original {@link StompHeadersSubframe} content sub frame.
     *
     * <p>By default an encoded buffer is returned as is.
     */
    protected virtual object ConvertContentSubFrame(IStompContentSubframe original, ByteBuf encoded)
    {
        return encoded;
    }

    /**
     * Returns a heuristic size for headers (32 bytes per header line) + (2 bytes for colon and eol) + (additional
     * command buffer).
     */
    protected virtual int HeadersSubFrameSize(IStompHeadersSubframe headersSubframe)
    {
        int estimatedSize = headersSubframe.Headers().Size() * 34 + 48;
        if (estimatedSize < 128)
        {
            return 128;
        }

        return Math.Max(estimatedSize, 256);
    }

    private ByteBuf EncodeFullFrame(IStompFrame frame, IChannelHandlerContext ctx)
    {
        int contentReadableBytes = frame.Content.ReadableBytes;
        ByteBuf buf = ctx.Alloc().Buffer(HeadersSubFrameSize(frame) + contentReadableBytes);

        try
        {
            EncodeHeaders(frame, buf);
        }
        catch (Exception)
        {
            buf.Release();
            throw;
        }

        if (contentReadableBytes > 0)
        {
            buf.WriteBytes(frame.Content);
        }

        return buf.WriteByte(StompConstants.NUL);
    }

    private static void EncodeHeaders(IStompHeadersSubframe frame, ByteBuf buf)
    {
        StompCommand command = frame.Command();
        ByteBufUtil.WriteUtf8(buf, command.ToString());
        buf.WriteByte(StompConstants.LF);

        bool shouldEscape = ShouldEscape(command);
        EscapeHeaderKeyCache cache = ESCAPE_HEADER_KEY_CACHE.Get();

        foreach (IEntry<ICharSequence, ICharSequence> entry in frame.Headers())
        {
            ICharSequence headerKey = entry.GetKey();
            ICharSequence headerValue = entry.GetValue();
            if (headerKey.Length() == 0)
            {
                throw new ArgumentException("STOMP " + command + " contains empty header name");
            }

            if (shouldEscape)
            {
                cache.TryGetValue(headerKey, out ICharSequence cachedHeaderKey);

                if (cachedHeaderKey == null)
                {
                    cachedHeaderKey = Escape(command, "header name", headerKey);
                    cache.Put(headerKey, cachedHeaderKey);
                }

                headerKey = cachedHeaderKey;
                headerValue = Escape(command, "header value", headerValue);
            }
            else
            {
                // For CONNECT/CONNECTED: don't escape but REJECT illegal characters
                ValidateNoIllegalCharacters(command, headerKey, "header name");
                ValidateNoIllegalCharacters(command, headerValue, "header value");
            }

            WriteUtf8(buf, headerKey);
            buf.WriteByte(StompConstants.COLON);

            WriteUtf8(buf, headerValue);
            buf.WriteByte(StompConstants.LF);
        }

        buf.WriteByte(StompConstants.LF);
    }

    private static int WriteUtf8(ByteBuf buf, ICharSequence seq)
    {
        return seq is AsciiString asciiString
                ? ByteBufUtil.WriteUtf8(buf, asciiString)
                : ByteBufUtil.WriteUtf8(buf, seq.ToString());
    }

    private static ByteBuf EncodeContent(IStompContentSubframe content, IChannelHandlerContext ctx)
    {
        if (content is ILastStompContentSubframe)
        {
            ByteBuf buf = ctx.Alloc().Buffer(content.Content.ReadableBytes + 1);
            buf.WriteBytes(content.Content);
            buf.WriteByte(StompConstants.NUL);
            return buf;
        }

        return content.Content.Retain();
    }

    private static bool ShouldEscape(StompCommand command)
    {
        return command != StompCommand.CONNECT && command != StompCommand.CONNECTED;
    }

    private static void ValidateNoIllegalCharacters(StompCommand command, ICharSequence value, string type)
    {
        for (int i = 0; i < value.Length(); i++)
        {
            char c = value.CharAt(i);
            if (c == '\n' || c == '\r' || c == ':' || c == '\0')
            {
                throw NewIllegalCharacterException(command, type, i);
            }
        }
    }

    private static ArgumentException NewIllegalCharacterException(StompCommand command, string type, int index)
    {
        return new ArgumentException(
                "STOMP " + command + " " + type + " contains illegal character at index " +
                index.ToString(CultureInfo.InvariantCulture));
    }

    private static ICharSequence Escape(StompCommand command, string type, ICharSequence input)
    {
        AppendableCharSequence builder = null;

        for (int i = 0; i < input.Length(); i++)
        {
            char chr = input.CharAt(i);
            if (chr == '\\')
            {
                builder = EscapeBuilder(builder, input, i);
                builder.Append("\\\\");
            }
            else if (chr == ':')
            {
                builder = EscapeBuilder(builder, input, i);
                builder.Append("\\c");
            }
            else if (chr == '\n')
            {
                builder = EscapeBuilder(builder, input, i);
                builder.Append("\\n");
            }
            else if (chr == '\r')
            {
                builder = EscapeBuilder(builder, input, i);
                builder.Append("\\r");
            }
            else if (chr == '\0')
            {
                // The NUL character has no escape and is always illegal.
                throw NewIllegalCharacterException(command, type, i);
            }
            else if (builder != null)
            {
                builder.Append(chr);
            }
        }

        return builder != null? builder : input;
    }

    private static AppendableCharSequence EscapeBuilder(AppendableCharSequence builder, ICharSequence input,
                                                        int offset)
    {
        if (builder != null)
        {
            return builder;
        }

        // Add extra overhead to the input char sequence to avoid resizing during escaping.
        return new AppendableCharSequence(input.Length() + 8).Append(input, 0, offset);
    }
}
