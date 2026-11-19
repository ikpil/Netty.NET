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
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec;

/**
 * A decoder that splits the received {@link ByteBuf}s by one or more
 * delimiters.  It is particularly useful for decoding the frames which ends
 * with a delimiter such as {@link Delimiters#nulDelimiter() NUL} or
 * {@linkplain Delimiters#lineDelimiter() newline characters}.
 *
 * <h3>Predefined delimiters</h3>
 * <p>
 * {@link Delimiters} defines frequently used delimiters for convenience' sake.
 *
 * <h3>Specifying more than one delimiter</h3>
 * <p>
 * {@link DelimiterBasedFrameDecoder} allows you to specify more than one
 * delimiter.  If more than one delimiter is found in the buffer, it chooses
 * the delimiter which produces the shortest frame.  For example, if you have
 * the following data in the buffer:
 * <pre>
 * +--------------+
 * | ABC\nDEF\r\n |
 * +--------------+
 * </pre>
 * a {@link DelimiterBasedFrameDecoder}({@link Delimiters#lineDelimiter() Delimiters.lineDelimiter()})
 * will choose {@code '\n'} as the first delimiter and produce two frames:
 * <pre>
 * +-----+-----+
 * | ABC | DEF |
 * +-----+-----+
 * </pre>
 * rather than incorrectly choosing {@code '\r\n'} as the first delimiter:
 * <pre>
 * +----------+
 * | ABC\nDEF |
 * +----------+
 * </pre>
 */
// CLR: List<Object> out is IList<object> @out and 'in' is @in (C# keywords); the ByteBuf... varargs are params
// ByteBuf[]. Numbers in messages use the invariant culture (Int32.ToString() otherwise uses "the NumberFormatInfo
// object of the current culture", https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
public class DelimiterBasedFrameDecoder : ByteToMessageDecoder
{
    /** Size of the first window searched when more than one delimiter is configured. */
    private const int INITIAL_SCAN_WINDOW = 64;

    private readonly ByteBuf[] delimiters;
    private readonly int maxDelimiterLength;
    private readonly int maxFrameLength;
    private readonly bool stripDelimiter;
    private readonly bool failFast;
    private bool discardingTooLongFrame;
    private int tooLongFrameLength;
    /** Set only when decoding with "\n" and "\r\n" as the delimiter.  */
    private readonly LineBasedFrameDecoder lineBasedDecoder;
    /**
     * Last scan position, relative to the reader index, at which no delimiter could be found.
     * Resuming from this offset avoids rescanning the whole buffer from the reader index on
     * every {@link #decode(ChannelHandlerContext, ByteBuf)} call.
     */
    private int scanOffset;

    /**
     * Creates a new instance.
     *
     * @param maxFrameLength  the maximum length of the decoded frame.
     *                        A {@link TooLongFrameException} is thrown if
     *                        the length of the frame exceeds this value.
     * @param delimiter  the delimiter
     */
    public DelimiterBasedFrameDecoder(int maxFrameLength, ByteBuf delimiter) : this(maxFrameLength, true, delimiter)
    {
    }

    /**
     * Creates a new instance.
     *
     * @param maxFrameLength  the maximum length of the decoded frame.
     *                        A {@link TooLongFrameException} is thrown if
     *                        the length of the frame exceeds this value.
     * @param stripDelimiter  whether the decoded frame should strip out the
     *                        delimiter or not
     * @param delimiter  the delimiter
     */
    public DelimiterBasedFrameDecoder(
            int maxFrameLength, bool stripDelimiter, ByteBuf delimiter)
        : this(maxFrameLength, stripDelimiter, true, delimiter)
    {
    }

    /**
     * Creates a new instance.
     *
     * @param maxFrameLength  the maximum length of the decoded frame.
     *                        A {@link TooLongFrameException} is thrown if
     *                        the length of the frame exceeds this value.
     * @param stripDelimiter  whether the decoded frame should strip out the
     *                        delimiter or not
     * @param failFast  If <tt>true</tt>, a {@link TooLongFrameException} is
     *                  thrown as soon as the decoder notices the length of the
     *                  frame will exceed <tt>maxFrameLength</tt> regardless of
     *                  whether the entire frame has been read.
     *                  If <tt>false</tt>, a {@link TooLongFrameException} is
     *                  thrown after the entire frame that exceeds
     *                  <tt>maxFrameLength</tt> has been read.
     * @param delimiter  the delimiter
     */
    public DelimiterBasedFrameDecoder(
            int maxFrameLength, bool stripDelimiter, bool failFast,
            ByteBuf delimiter)
        : this(maxFrameLength, stripDelimiter, failFast, new ByteBuf[] {
                delimiter.Slice(delimiter.ReaderIndex, delimiter.ReadableBytes)})
    {
    }

    /**
     * Creates a new instance.
     *
     * @param maxFrameLength  the maximum length of the decoded frame.
     *                        A {@link TooLongFrameException} is thrown if
     *                        the length of the frame exceeds this value.
     * @param delimiters  the delimiters
     */
    public DelimiterBasedFrameDecoder(int maxFrameLength, params ByteBuf[] delimiters)
        : this(maxFrameLength, true, delimiters)
    {
    }

    /**
     * Creates a new instance.
     *
     * @param maxFrameLength  the maximum length of the decoded frame.
     *                        A {@link TooLongFrameException} is thrown if
     *                        the length of the frame exceeds this value.
     * @param stripDelimiter  whether the decoded frame should strip out the
     *                        delimiter or not
     * @param delimiters  the delimiters
     */
    public DelimiterBasedFrameDecoder(
            int maxFrameLength, bool stripDelimiter, params ByteBuf[] delimiters)
        : this(maxFrameLength, stripDelimiter, true, delimiters)
    {
    }

    /**
     * Creates a new instance.
     *
     * @param maxFrameLength  the maximum length of the decoded frame.
     *                        A {@link TooLongFrameException} is thrown if
     *                        the length of the frame exceeds this value.
     * @param stripDelimiter  whether the decoded frame should strip out the
     *                        delimiter or not
     * @param failFast  If <tt>true</tt>, a {@link TooLongFrameException} is
     *                  thrown as soon as the decoder notices the length of the
     *                  frame will exceed <tt>maxFrameLength</tt> regardless of
     *                  whether the entire frame has been read.
     *                  If <tt>false</tt>, a {@link TooLongFrameException} is
     *                  thrown after the entire frame that exceeds
     *                  <tt>maxFrameLength</tt> has been read.
     * @param delimiters  the delimiters
     */
    public DelimiterBasedFrameDecoder(
            int maxFrameLength, bool stripDelimiter, bool failFast, params ByteBuf[] delimiters)
    {
        ValidateMaxFrameLength(maxFrameLength);
        ObjectUtil.CheckNonEmpty(delimiters, "delimiters");

        if (IsLineBased(delimiters) && !IsSubclass())
        {
            lineBasedDecoder = new LineBasedFrameDecoder(maxFrameLength, stripDelimiter, failFast);
            this.delimiters = null;
            this.maxDelimiterLength = 0;
        }
        else
        {
            this.delimiters = new ByteBuf[delimiters.Length];
            int maxDelimiterLength = 0;
            for (int i = 0; i < delimiters.Length; i ++)
            {
                ByteBuf d = delimiters[i];
                ValidateDelimiter(d);
                this.delimiters[i] = d.Slice(d.ReaderIndex, d.ReadableBytes);
                maxDelimiterLength = Math.Max(maxDelimiterLength, this.delimiters[i].ReadableBytes);
            }
            this.maxDelimiterLength = maxDelimiterLength;
            lineBasedDecoder = null;
        }
        this.maxFrameLength = maxFrameLength;
        this.stripDelimiter = stripDelimiter;
        this.failFast = failFast;
    }

    /** Returns true if the delimiters are "\n" and "\r\n".  */
    private static bool IsLineBased(ByteBuf[] delimiters)
    {
        if (delimiters.Length != 2)
        {
            return false;
        }
        ByteBuf a = delimiters[0];
        ByteBuf b = delimiters[1];
        if (a.Capacity < b.Capacity)
        {
            a = delimiters[1];
            b = delimiters[0];
        }
        return a.Capacity == 2 && b.Capacity == 1
                && a.GetByte(0) == '\r' && a.GetByte(1) == '\n'
                && b.GetByte(0) == '\n';
    }

    /**
     * Return {@code true} if the current instance is a subclass of DelimiterBasedFrameDecoder
     */
    // CLR: GetType() is "the exact runtime type of the current instance"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.object.gettype), as getClass() is.
    private bool IsSubclass()
    {
        return GetType() != typeof(DelimiterBasedFrameDecoder);
    }

    protected sealed override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        object decoded = Decode(ctx, @in);
        if (decoded != null)
        {
            @out.Add(decoded);
        }
    }

    /**
     * Create a frame out of the {@link ByteBuf} and return it.
     *
     * @param   ctx             the {@link ChannelHandlerContext} which this {@link ByteToMessageDecoder} belongs to
     * @param   buffer          the {@link ByteBuf} from which to read data
     * @return  frame           the {@link ByteBuf} which represent the frame or {@code null} if no frame could
     *                          be created.
     */
    protected virtual object Decode(IChannelHandlerContext ctx, ByteBuf buffer)
    {
        if (lineBasedDecoder != null)
        {
            return lineBasedDecoder.Decode(ctx, buffer);
        }
        // Try all delimiters and choose the delimiter which yields the shortest frame.
        int minFrameLength = int.MaxValue;
        ByteBuf minDelim = null;
        int readableBytes = buffer.ReadableBytes;
        // Searching every delimiter through the whole remaining buffer would cost O(readableBytes) per delimiter
        // even if another delimiter matches right at the start, which is quadratic when many small frames are
        // contained in one large buffer. With more than one delimiter we therefore search in windows that double in
        // size, so the work done is bounded by the length of the frame we find rather than by the buffer size.
        long window = delimiters.Length == 1 ? readableBytes : Math.Max(INITIAL_SCAN_WINDOW, maxDelimiterLength);
        int from = scanOffset;
        while (from < readableBytes)
        {
            // Extend the region by maxDelimiterLength - 1 so a delimiter that starts inside the window but ends
            // after it is still found.
            int end = (int) Math.Min(readableBytes, from + window + maxDelimiterLength - 1);
            ByteBuf region = buffer.Slice(buffer.ReaderIndex + from, end - from);
            foreach (ByteBuf delim in delimiters)
            {
                int index = ByteBufUtil.IndexOf(delim, region);
                if (index >= 0 && from + index < minFrameLength)
                {
                    minFrameLength = from + index;
                    minDelim = delim;
                }
            }
            if (minDelim != null && (end == readableBytes || minFrameLength < from + window))
            {
                // All matches that start before the current minimum are guaranteed to be inside the region.
                break;
            }
            // Either nothing was found, or the only matches start in the overlap and another delimiter might
            // match earlier. Continue with the next window, which will find those matches again.
            minFrameLength = int.MaxValue;
            minDelim = null;
            from = (int) Math.Min(readableBytes, from + window);
            window = Math.Min(window << 1, int.MaxValue);
        }

        if (minDelim != null)
        {
            // A delimiter was found; the next decode() call starts scanning fresh data only.
            scanOffset = 0;
            int minDelimLength = minDelim.Capacity;
            ByteBuf frame;

            if (discardingTooLongFrame)
            {
                // We've just finished discarding a very large frame.
                // Go back to the initial state.
                discardingTooLongFrame = false;
                buffer.SkipBytes(minFrameLength + minDelimLength);

                int tooLongFrameLength = this.tooLongFrameLength;
                this.tooLongFrameLength = 0;
                if (!failFast)
                {
                    Fail(tooLongFrameLength);
                }
                return null;
            }

            if (minFrameLength > maxFrameLength)
            {
                // Discard read frame.
                buffer.SkipBytes(minFrameLength + minDelimLength);
                Fail(minFrameLength);
                return null;
            }

            if (stripDelimiter)
            {
                frame = buffer.ReadRetainedSlice(minFrameLength);
                buffer.SkipBytes(minDelimLength);
            }
            else
            {
                frame = buffer.ReadRetainedSlice(minFrameLength + minDelimLength);
            }

            return frame;
        }
        else
        {
            if (!discardingTooLongFrame)
            {
                if (readableBytes > maxFrameLength)
                {
                    // Discard the content of the buffer until a delimiter is found.
                    tooLongFrameLength = readableBytes;
                    buffer.SkipBytes(readableBytes);
                    discardingTooLongFrame = true;
                    scanOffset = 0;
                    if (failFast)
                    {
                        Fail(tooLongFrameLength);
                    }
                }
                else
                {
                    // No delimiter found yet. Remember how far we've already scanned so the next
                    // call doesn't rescan bytes that cannot possibly contain the start of a match.
                    scanOffset = Math.Max(0, readableBytes - maxDelimiterLength + 1);
                }
            }
            else
            {
                // Still discarding the buffer since a delimiter is not found.
                tooLongFrameLength += buffer.ReadableBytes;
                buffer.SkipBytes(buffer.ReadableBytes);
                scanOffset = 0;
            }
            return null;
        }
    }

    private void Fail(long frameLength)
    {
        if (frameLength > 0)
        {
            throw new TooLongFrameException(
                            "frame length exceeds " + maxFrameLength.ToString(CultureInfo.InvariantCulture) +
                            ": " + frameLength.ToString(CultureInfo.InvariantCulture) + " - discarded");
        }
        else
        {
            throw new TooLongFrameException(
                            "frame length exceeds " + maxFrameLength.ToString(CultureInfo.InvariantCulture) +
                            " - discarding");
        }
    }

    // CLR: IllegalArgumentException is ArgumentException.
    private static void ValidateDelimiter(ByteBuf delimiter)
    {
        ObjectUtil.CheckNotNull(delimiter, "delimiter");
        if (!delimiter.IsReadable())
        {
            throw new ArgumentException("empty delimiter");
        }
    }

    private static void ValidateMaxFrameLength(int maxFrameLength)
    {
        CheckPositive(maxFrameLength, "maxFrameLength");
    }
}
