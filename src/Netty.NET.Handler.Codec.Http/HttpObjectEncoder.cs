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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http.HttpConstants;
using static Netty.NET.Handler.Codec.Http.HttpObjectEncoder;

namespace Netty.NET.Handler.Codec.Http;

// CLR: CRLF_SHORT does not depend on H, but a static member of a C# generic class belongs to each closed type
// (HttpObjectEncoder<IHttpRequest>.CRLF_SHORT), so the package-private constant that HttpHeadersEncoder imports is
// declared on this non-generic HttpObjectEncoder class, as codec-base declares DefaultHeaders' static members on a
// non-generic DefaultHeaders class (docs/codec-base-porting.md; src/Netty.NET.Handler.Codec/DefaultHeaders.cs).
public static class HttpObjectEncoder
{
    internal const int CRLF_SHORT = (CR << 8) | LF;
}

/**
 * Encodes an {@link HttpMessage} or an {@link HttpContent} into
 * a {@link ByteBuf}.
 *
 * <h3>Extensibility</h3>
 *
 * Please note that this encoder is designed to be extended to implement
 * a protocol derived from HTTP, such as
 * <a href="https://en.wikipedia.org/wiki/Real_Time_Streaming_Protocol">RTSP</a> and
 * <a href="https://en.wikipedia.org/wiki/Internet_Content_Adaptation_Protocol">ICAP</a>.
 * To implement the encoder of such a derived protocol, extend this class and
 * implement all abstract methods properly.
 */
// CLR: H extends HttpMessage is the constraint H : IHttpMessage. write(ctx, msg, promise) is WriteAsync(ctx, msg,
// cancellationToken), whose Task replaces the promise (docs/codec-base-porting.md, Outbound operations); as in codec-base
// MessageToMessageEncoder, there is no void promise, so writeVoidPromise has no counterpart, and PromiseCombiner is
// codec-base's ExecutorPromiseCombiner, which completes the returned Task on the event loop when the last write
// completes as the Java combiner does (a Task.WhenAll of the Transport write Tasks completed later on a thread-pool
// thread; src/Netty.NET.Handler.Codec/ExecutorPromiseCombiner.cs). The reused out list is the field @out. FileRegion is the
// Transport IFileRegion. java.lang.Error is UnreachableException (docs/codec-memcache-porting.md),
// IllegalStateException is InvalidOperationException and assert is Debug.Assert; the switch sections that fall through
// end with goto case (docs/codec-compression-porting.md). In encodeFullHttpMessage a local named state is declared after
// the field is read, so the field is this.state (CS0841: https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0841).
// Long.SIZE - Long.numberOfLeadingZeros is 64 - BitOperations.LeadingZeroCount((ulong) x)
// (docs/codec-compression-porting.md, Bit operations). LeakPresenceDetector.staticInitializer is the Common
// LeakPresenceDetector.StaticInitializer. The deprecated encodeAscii(String, ByteBuf) has no @deprecated text and is
// ported with [Obsolete]; buf.writeCharSequence(s, US_ASCII) is ByteBufUtil.WriteAscii (AsciiString.c2b per char, as
// Java writes; see HttpHeadersEncoder).
public abstract class HttpObjectEncoder<H> : MessageToMessageEncoder<object>
    where H : IHttpMessage
{

    // this is a constant to decide when it is appropriate to copy the data content into the header buffer
    private const int COPY_CONTENT_THRESHOLD = 128;
    private const int ZERO_CRLF_MEDIUM = ('0' << 16) | CRLF_SHORT;
    private static readonly byte[] ZERO_CRLF_CRLF = { (byte) '0', CR, LF, CR, LF };
    private static readonly ByteBuf CRLF_BUF = LeakPresenceDetector.StaticInitializer(() => Unpooled.UnreleasableBuffer(
            Unpooled.DirectBuffer(2).WriteByte(CR).WriteByte(LF)).AsReadOnly());
    private static readonly ByteBuf ZERO_CRLF_CRLF_BUF = LeakPresenceDetector.StaticInitializer(() => Unpooled.UnreleasableBuffer(
            Unpooled.DirectBuffer(ZERO_CRLF_CRLF.Length).WriteBytes(ZERO_CRLF_CRLF)).AsReadOnly());
    private const float HEADERS_WEIGHT_NEW = 1 / 5f;
    private const float HEADERS_WEIGHT_HISTORICAL = 1 - HEADERS_WEIGHT_NEW;
    private const float TRAILERS_WEIGHT_NEW = HEADERS_WEIGHT_NEW;
    private const float TRAILERS_WEIGHT_HISTORICAL = HEADERS_WEIGHT_HISTORICAL;

    private const int ST_INIT = 0;
    private const int ST_CONTENT_NON_CHUNK = 1;
    private const int ST_CONTENT_CHUNK = 2;
    private const int ST_CONTENT_ALWAYS_EMPTY = 3;

    private int state = ST_INIT;

    /**
     * Used to calculate an exponential moving average of the encoded size of the initial line and the headers for
     * a guess for future buffer allocations.
     */
    private float headersEncodedSizeAccumulator = 256;

    /**
     * Used to calculate an exponential moving average of the encoded size of the trailers for
     * a guess for future buffer allocations.
     */
    private float trailersEncodedSizeAccumulator = 256;

    private readonly List<object> @out = new List<object>();

    private static bool CheckContentState(int state)
    {
        return state == ST_CONTENT_CHUNK || state == ST_CONTENT_NON_CHUNK || state == ST_CONTENT_ALWAYS_EMPTY;
    }

    public HttpObjectEncoder() : base(typeof(object))
    {
    }

    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        Task future = null;

        try
        {
            if (AcceptOutboundMessage(msg))
            {
                Encode(ctx, msg, @out);
                if (@out.Count == 0)
                {
                    throw new EncoderException(
                            StringUtil.SimpleClassName(this) + " must produce at least one message.");
                }
            }
            else
            {
                future = ctx.WriteAsync(msg, cancellationToken);
            }
        }
        catch (EncoderException)
        {
            throw;
        }
        catch (Exception t)
        {
            throw new EncoderException(t);
        }
        finally
        {
            Task written = WriteOutList(ctx, @out, cancellationToken);
            if (written != null)
            {
                future = written;
            }
        }

        return future;
    }

    private static Task WriteOutList(IChannelHandlerContext ctx, List<object> @out, CancellationToken cancellationToken)
    {
        int size = @out.Count;

        try
        {
            if (size == 1)
            {
                return ctx.WriteAsync(@out[0], cancellationToken);
            }
            else if (size > 1)
            {
                // Check if we can use a voidPromise for our extra writes to reduce GC-Pressure
                // See https://github.com/netty/netty/issues/2525
                return WritePromiseCombiner(ctx, @out);
            }

            return null;
        }
        finally
        {
            @out.Clear();
        }
    }

    private static Task WritePromiseCombiner(IChannelHandlerContext ctx, List<object> @out)
    {
        Task[] combiner = new Task[@out.Count];

        for (int i = 0; i < @out.Count; i++)
        {
            combiner[i] = ctx.WriteAsync(@out[i]);
        }

        return ExecutorPromiseCombiner.Combine(ctx, combiner);
    }

    protected override void Encode(IChannelHandlerContext ctx, object msg, IList<object> @out)
    {
        // fast-path for common idiom that doesn't require class-checks
        if (msg == Unpooled.EmptyBuffer)
        {
            @out.Add(Unpooled.EmptyBuffer);
            return;
        }

        // The reason why we perform instanceof checks in this order,
        // by duplicating some code and without relying on ReferenceCountUtil::release as a generic release
        // mechanism, is https://bugs.openjdk.org/browse/JDK-8180450.
        // https://github.com/netty/netty/issues/12708 contains more detail re how the previous version of this
        // code was interacting with the JIT instanceof optimizations.
        if (msg is IFullHttpMessage)
        {
            EncodeFullHttpMessage(ctx, msg, @out);
            return;
        }

        if (msg is IHttpMessage)
        {
            H m;

            try
            {
                m = (H) msg;
            }
            catch (Exception)
            {
                ReferenceCountUtil.Release(msg);
                throw;
            }

            if (m is ILastHttpContent)
            {
                EncodeHttpMessageLastContent(ctx, m, @out);
            }
            else if (m is IHttpContent)
            {
                EncodeHttpMessageNotLastContent(ctx, m, @out);
            }
            else
            {
                EncodeJustHttpMessage(ctx, m, @out);
            }
        }
        else
        {
            EncodeNotHttpMessageContentTypes(ctx, msg, @out);
        }
    }

    private void EncodeJustHttpMessage(IChannelHandlerContext ctx, H m, IList<object> @out)
    {
        Debug.Assert(!(m is IHttpContent));

        try
        {
            if (state != ST_INIT)
            {
                ThrowUnexpectedMessageTypeEx(m, state);
            }

            ByteBuf buf = EncodeInitHttpMessage(ctx, m);

            Debug.Assert(CheckContentState(state));

            @out.Add(buf);
        }
        finally
        {
            ReferenceCountUtil.Release(m);
        }
    }

    private void EncodeByteBufHttpContent(int state, IChannelHandlerContext ctx, ByteBuf buf, ByteBuf content,
                                          HttpHeaders trailingHeaders, IList<object> @out)
    {
        switch (state)
        {
            case ST_CONTENT_NON_CHUNK:
                if (EncodeContentNonChunk(@out, buf, content))
                {
                    break;
                }

                // fall-through!
                goto case ST_CONTENT_ALWAYS_EMPTY;
            case ST_CONTENT_ALWAYS_EMPTY:
                // We allocated a buffer so add it now.
                @out.Add(buf);
                break;
            case ST_CONTENT_CHUNK:
                // We allocated a buffer so add it now.
                @out.Add(buf);
                EncodeChunkedHttpContent(ctx, content, trailingHeaders, @out);
                break;
            default:
                throw new UnreachableException("Unexpected http object encoder state: " + state);
        }
    }

    private void EncodeHttpMessageNotLastContent(IChannelHandlerContext ctx, H m, IList<object> @out)
    {
        Debug.Assert(m is IHttpContent);
        Debug.Assert(!(m is ILastHttpContent));
        IHttpContent httpContent = (IHttpContent) m;

        try
        {
            if (state != ST_INIT)
            {
                ThrowUnexpectedMessageTypeEx(m, state);
            }

            ByteBuf buf = EncodeInitHttpMessage(ctx, m);

            Debug.Assert(CheckContentState(state));

            EncodeByteBufHttpContent(state, ctx, buf, httpContent.Content, null, @out);
        }
        finally
        {
            httpContent.Release();
        }
    }

    private void EncodeHttpMessageLastContent(IChannelHandlerContext ctx, H m, IList<object> @out)
    {
        Debug.Assert(m is ILastHttpContent);
        ILastHttpContent httpContent = (ILastHttpContent) m;

        try
        {
            if (state != ST_INIT)
            {
                ThrowUnexpectedMessageTypeEx(m, state);
            }

            ByteBuf buf = EncodeInitHttpMessage(ctx, m);

            Debug.Assert(CheckContentState(state));

            EncodeByteBufHttpContent(state, ctx, buf, httpContent.Content, httpContent.TrailingHeaders(), @out);

            state = ST_INIT;
        }
        finally
        {
            httpContent.Release();
        }
    }

    private void EncodeNotHttpMessageContentTypes(IChannelHandlerContext ctx, object msg, IList<object> @out)
    {
        Debug.Assert(!(msg is IHttpMessage));

        if (state == ST_INIT)
        {
            try
            {
                if (msg is ByteBuf && BypassEncoderIfEmpty((ByteBuf) msg, @out))
                {
                    return;
                }

                ThrowUnexpectedMessageTypeEx(msg, ST_INIT);
            }
            finally
            {
                ReferenceCountUtil.Release(msg);
            }
        }

        if (msg == ILastHttpContent.EMPTY_LAST_CONTENT)
        {
            state = EncodeEmptyLastHttpContent(state, @out);
            return;
        }

        if (msg is ILastHttpContent)
        {
            EncodeLastHttpContent(ctx, (ILastHttpContent) msg, @out);
            return;
        }

        if (msg is IHttpContent)
        {
            EncodeHttpContent(ctx, (IHttpContent) msg, @out);
            return;
        }

        if (msg is ByteBuf)
        {
            EncodeByteBufContent(ctx, (ByteBuf) msg, @out);
            return;
        }

        if (msg is IFileRegion)
        {
            EncodeFileRegionContent(ctx, (IFileRegion) msg, @out);
            return;
        }

        try
        {
            ThrowUnexpectedMessageTypeEx(msg, state);
        }
        finally
        {
            ReferenceCountUtil.Release(msg);
        }
    }

    private void EncodeFullHttpMessage(IChannelHandlerContext ctx, object o, IList<object> @out)
    {
        Debug.Assert(o is IFullHttpMessage);
        IFullHttpMessage msg = (IFullHttpMessage) o;

        try
        {
            if (this.state != ST_INIT)
            {
                ThrowUnexpectedMessageTypeEx(o, this.state);
            }

            H m = (H) o;

            int state = IsContentAlwaysEmpty(m) ? ST_CONTENT_ALWAYS_EMPTY :
                    HttpUtil.IsTransferEncodingChunked(m) ? ST_CONTENT_CHUNK : ST_CONTENT_NON_CHUNK;

            ByteBuf content = msg.Content;

            bool accountForContentSize = content.ReadableBytes > 0 &&
                                        state == ST_CONTENT_NON_CHUNK &&
                                        // try embed the content if less or equals than
                                        // the biggest of ~12.5% of the header estimated size and COPY_DATA_THRESHOLD:
                                        // it limits a wrong estimation to waste too much memory
                                        content.ReadableBytes <=
                                        Math.Max(COPY_CONTENT_THRESHOLD, ((int) headersEncodedSizeAccumulator) / 8);
            int headersAndContentSize = (int) headersEncodedSizeAccumulator +
                                                  (accountForContentSize? content.ReadableBytes : 0);
            ByteBuf buf = ctx.Alloc().Buffer(headersAndContentSize);
            bool handedOff = false;

            try
            {
                EncodeInitialLine(buf, m);

                SanitizeHeadersBeforeEncode(m, state == ST_CONTENT_ALWAYS_EMPTY);

                EncodeHeaders(m.Headers(), buf);
                ByteBufUtil.WriteShortBE(buf, CRLF_SHORT);

                // don't consider the copyContent case here: the statistics is just related the headers
                headersEncodedSizeAccumulator = HEADERS_WEIGHT_NEW * PadSizeForAccumulation(buf.ReadableBytes) +
                        HEADERS_WEIGHT_HISTORICAL * headersEncodedSizeAccumulator;

                handedOff = true;
                EncodeByteBufHttpContent(state, ctx, buf, content, msg.TrailingHeaders(), @out);
            }
            finally
            {
                if (!handedOff)
                {
                    buf.Release();
                }
            }
        }
        finally
        {
            msg.Release();
        }
    }

    private static bool EncodeContentNonChunk(IList<object> @out, ByteBuf buf, ByteBuf content)
    {
        int contentLength = content.ReadableBytes;
        if (contentLength > 0)
        {
            if (buf.MaxFastWritableBytes >= contentLength)
            {
                // merge into other buffer for performance reasons
                buf.WriteBytes(content);
                @out.Add(buf);
            }
            else
            {
                @out.Add(buf);
                @out.Add(content.Retain());
            }

            return true;
        }

        return false;
    }

    private static void ThrowUnexpectedMessageTypeEx(object msg, int state)
    {
        throw new InvalidOperationException("unexpected message type: " + StringUtil.SimpleClassName(msg)
                + ", state: " + state);
    }

    private void EncodeFileRegionContent(IChannelHandlerContext ctx, IFileRegion msg, IList<object> @out)
    {
        try
        {
            Debug.Assert(state != ST_INIT);

            switch (state)
            {
                case ST_CONTENT_NON_CHUNK:
                    if (msg.Count() > 0)
                    {
                        @out.Add(msg.Retain());
                        break;
                    }

                    // fall-through!
                    goto case ST_CONTENT_ALWAYS_EMPTY;
                case ST_CONTENT_ALWAYS_EMPTY:
                    // Need to produce some output otherwise an
                    // IllegalStateException will be thrown as we did not write anything
                    // Its ok to just write an EMPTY_BUFFER as if there are reference count issues these will be
                    // propagated as the caller of the encode(...) method will release the original
                    // buffer.
                    // Writing an empty buffer will not actually write anything on the wire, so if there is a user
                    // error with msg it will not be visible externally
                    @out.Add(Unpooled.EmptyBuffer);
                    break;
                case ST_CONTENT_CHUNK:
                    EncodedChunkedFileRegionContent(ctx, msg, @out);
                    break;
                default:
                    throw new UnreachableException("Unexpected http object encoder state: " + state);
            }
        }
        finally
        {
            msg.Release();
        }
    }

    // Bypass the encoder in case of an empty buffer, so that the following idiom works:
    //
    //     ch.write(Unpooled.EMPTY_BUFFER).addListener(ChannelFutureListener.CLOSE);
    //
    // See https://github.com/netty/netty/issues/2983 for more information.
    private static bool BypassEncoderIfEmpty(ByteBuf msg, IList<object> @out)
    {
        if (!msg.IsReadable())
        {
            @out.Add(msg.Retain());
            return true;
        }

        return false;
    }

    private void EncodeByteBufContent(IChannelHandlerContext ctx, ByteBuf content, IList<object> @out)
    {
        try
        {
            Debug.Assert(state != ST_INIT);
            if (BypassEncoderIfEmpty(content, @out))
            {
                return;
            }

            EncodeByteBufAndTrailers(state, ctx, @out, content, null);
        }
        finally
        {
            content.Release();
        }
    }

    private static int EncodeEmptyLastHttpContent(int state, IList<object> @out)
    {
        Debug.Assert(state != ST_INIT);

        switch (state)
        {
            case ST_CONTENT_NON_CHUNK:
            case ST_CONTENT_ALWAYS_EMPTY:
                @out.Add(Unpooled.EmptyBuffer);
                break;
            case ST_CONTENT_CHUNK:
                @out.Add(ZERO_CRLF_CRLF_BUF.Duplicate());
                break;
            default:
                throw new UnreachableException("Unexpected http object encoder state: " + state);
        }

        return ST_INIT;
    }

    private void EncodeLastHttpContent(IChannelHandlerContext ctx, ILastHttpContent msg, IList<object> @out)
    {
        Debug.Assert(state != ST_INIT);
        Debug.Assert(!(msg is IHttpMessage));

        try
        {
            EncodeByteBufAndTrailers(state, ctx, @out, msg.Content, msg.TrailingHeaders());
            state = ST_INIT;
        }
        finally
        {
            msg.Release();
        }
    }

    private void EncodeHttpContent(IChannelHandlerContext ctx, IHttpContent msg, IList<object> @out)
    {
        Debug.Assert(state != ST_INIT);
        Debug.Assert(!(msg is IHttpMessage));
        Debug.Assert(!(msg is ILastHttpContent));

        try
        {
            this.EncodeByteBufAndTrailers(state, ctx, @out, msg.Content, null);
        }
        finally
        {
            msg.Release();
        }
    }

    private void EncodeByteBufAndTrailers(int state, IChannelHandlerContext ctx, IList<object> @out, ByteBuf content,
                                          HttpHeaders trailingHeaders)
    {
        switch (state)
        {
            case ST_CONTENT_NON_CHUNK:
                if (content.IsReadable())
                {
                    @out.Add(content.Retain());
                    break;
                }

                // fall-through!
                goto case ST_CONTENT_ALWAYS_EMPTY;
            case ST_CONTENT_ALWAYS_EMPTY:
                @out.Add(Unpooled.EmptyBuffer);
                break;
            case ST_CONTENT_CHUNK:
                EncodeChunkedHttpContent(ctx, content, trailingHeaders, @out);
                break;
            default:
                throw new UnreachableException("Unexpected http object encoder state: " + state);
        }
    }

    private void EncodeChunkedHttpContent(IChannelHandlerContext ctx, ByteBuf content, HttpHeaders trailingHeaders,
                                          IList<object> @out)
    {
        int contentLength = content.ReadableBytes;
        if (contentLength > 0)
        {
            AddEncodedLengthHex(ctx, contentLength, @out);
            @out.Add(content.Retain());
            @out.Add(CRLF_BUF.Duplicate());
        }

        if (trailingHeaders != null)
        {
            EncodeTrailingHeaders(ctx, trailingHeaders, @out);
        }
        else if (contentLength == 0)
        {
            // Need to produce some output otherwise an
            // IllegalStateException will be thrown
            @out.Add(content.Retain());
        }
    }

    private void EncodeTrailingHeaders(IChannelHandlerContext ctx, HttpHeaders trailingHeaders, IList<object> @out)
    {
        if (trailingHeaders.IsEmpty())
        {
            @out.Add(ZERO_CRLF_CRLF_BUF.Duplicate());
        }
        else
        {
            ByteBuf buf = ctx.Alloc().Buffer((int) trailersEncodedSizeAccumulator);
            ByteBufUtil.WriteMediumBE(buf, ZERO_CRLF_MEDIUM);
            EncodeHeaders(trailingHeaders, buf);
            ByteBufUtil.WriteShortBE(buf, CRLF_SHORT);
            trailersEncodedSizeAccumulator = TRAILERS_WEIGHT_NEW * PadSizeForAccumulation(buf.ReadableBytes) +
                    TRAILERS_WEIGHT_HISTORICAL * trailersEncodedSizeAccumulator;
            @out.Add(buf);
        }
    }

    private ByteBuf EncodeInitHttpMessage(IChannelHandlerContext ctx, H m)
    {
        Debug.Assert(state == ST_INIT);

        ByteBuf buf = ctx.Alloc().Buffer((int) headersEncodedSizeAccumulator);
        bool success = false;

        try
        {
            // Encode the message.
            EncodeInitialLine(buf, m);
            int nextState = IsContentAlwaysEmpty(m) ? ST_CONTENT_ALWAYS_EMPTY :
                    HttpUtil.IsTransferEncodingChunked(m) ? ST_CONTENT_CHUNK : ST_CONTENT_NON_CHUNK;

            SanitizeHeadersBeforeEncode(m, nextState == ST_CONTENT_ALWAYS_EMPTY);

            EncodeHeaders(m.Headers(), buf);
            ByteBufUtil.WriteShortBE(buf, CRLF_SHORT);

            headersEncodedSizeAccumulator = HEADERS_WEIGHT_NEW * PadSizeForAccumulation(buf.ReadableBytes) +
                    HEADERS_WEIGHT_HISTORICAL * headersEncodedSizeAccumulator;
            state = nextState;
            success = true;
            return buf;
        }
        finally
        {
            if (!success)
            {
                buf.Release();
            }
        }
    }

    /**
     * Encode the {@link HttpHeaders} into a {@link ByteBuf}.
     */
    protected virtual void EncodeHeaders(HttpHeaders headers, ByteBuf buf)
    {
        IIterator<IEntry<ICharSequence, ICharSequence>> iter = headers.IteratorCharSequence();

        while (iter.HasNext())
        {
            IEntry<ICharSequence, ICharSequence> header = iter.Next();
            HttpHeadersEncoder.EncoderHeader(header.GetKey(), header.GetValue(), buf);
        }
    }

    private static void EncodedChunkedFileRegionContent(IChannelHandlerContext ctx, IFileRegion msg, IList<object> @out)
    {
        long contentLength = msg.Count();
        if (contentLength > 0)
        {
            AddEncodedLengthHex(ctx, contentLength, @out);
            @out.Add(msg.Retain());
            @out.Add(CRLF_BUF.Duplicate());
        }
        else if (contentLength == 0)
        {
            // Need to produce some output otherwise an
            // IllegalStateException will be thrown
            @out.Add(msg.Retain());
        }
    }

    private static void AddEncodedLengthHex(IChannelHandlerContext ctx, long contentLength, IList<object> @out)
    {
        // logic is from Long.toHexString() but we want to avoid creating a String
        int hexLen = contentLength == 0 ? 1 : (64 - BitOperations.LeadingZeroCount((ulong) contentLength) + 3) >>> 2;
        ByteBuf buf = ctx.Alloc().Buffer(hexLen + 2); // +2 for CRLF
        WriteHexAscii(buf, contentLength, hexLen);
        ByteBufUtil.WriteShortBE(buf, CRLF_SHORT);
        @out.Add(buf);
    }

    private static readonly byte[] HEX = {
            (byte) '0', (byte) '1', (byte) '2', (byte) '3', (byte) '4', (byte) '5', (byte) '6', (byte) '7',
            (byte) '8', (byte) '9', (byte) 'a', (byte) 'b', (byte) 'c', (byte) 'd', (byte) 'e', (byte) 'f'
    };
    private static void WriteHexAscii(ByteBuf @out, long contentLength, int hexLen)
    {
        for (int shift = (hexLen - 1) << 2; shift >= 0; shift -= 4)
        {
            @out.WriteByte(HEX[(int) ((contentLength >>> shift) & 0xF)]);
        }
    }

    /**
     * Allows to sanitize headers of the message before encoding these.
     */
    protected virtual void SanitizeHeadersBeforeEncode(H msg, bool isAlwaysEmpty)
    {
        // noop
    }

    /**
     * Determine whether a message has a content or not. Some message may have headers indicating
     * a content without having an actual content, e.g the response to an HEAD or CONNECT request.
     *
     * @param msg the message to test
     * @return {@code true} to signal the message has no content
     */
    protected virtual bool IsContentAlwaysEmpty(H msg)
    {
        return false;
    }

    public override bool AcceptOutboundMessage(object msg)
    {
        return msg == Unpooled.EmptyBuffer ||
                msg == ILastHttpContent.EMPTY_LAST_CONTENT ||
                msg is IFullHttpMessage ||
                msg is IHttpMessage ||
                msg is ILastHttpContent ||
                msg is IHttpContent ||
                msg is ByteBuf || msg is IFileRegion;
    }

    /**
     * Add some additional overhead to the buffer. The rational is that it is better to slightly over allocate and waste
     * some memory, rather than under allocate and require a resize/copy.
     *
     * @param readableBytes The readable bytes in the buffer.
     * @return The {@code readableBytes} with some additional padding.
     */
    private static int PadSizeForAccumulation(int readableBytes)
    {
        return (readableBytes << 2) / 3;
    }

    [Obsolete]
    protected static void EncodeAscii(string s, ByteBuf buf)
    {
        ByteBufUtil.WriteAscii(buf, s);
    }

    protected abstract void EncodeInitialLine(ByteBuf buf, H message);

    // CLR: Java protected also admits the package, and HttpRequestEncoderTest calls encodeInitialLine(...) through it.
    // C# protected access needs an instance of the calling class (CS1540), so the tests reach it through this internal
    // forwarder (InternalsVisibleTo), as codec-base MessageToMessageEncoder.InvokeEncode and handler
    // IpSubnetFilter.InvokeAccept do (docs/codec-base-porting.md, Package access; docs/handler-porting.md).
    internal void InvokeEncodeInitialLine(ByteBuf buf, H message)
    {
        EncodeInitialLine(buf, message);
    }
}
