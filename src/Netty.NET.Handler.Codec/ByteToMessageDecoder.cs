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
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec;

/**
 * {@link ChannelInboundHandlerAdapter} which decodes bytes in a stream-like fashion from one {@link ByteBuf} to
 * another Message type.
 * <p>
 * For example here is an implementation which reads all readable bytes from
 * the input {@link ByteBuf} and create a new {@link ByteBuf}.
 *
 * <pre>
 *     public class SquareDecoder extends {@link ByteToMessageDecoder} {
 *         {@code @Override}
 *         public void decode({@link ChannelHandlerContext} ctx, {@link ByteBuf} in, List&lt;Object&gt; out)
 *                 throws {@link Exception} {
 *             out.add(in.readBytes(in.readableBytes()));
 *         }
 *     }
 * </pre>
 *
 * <h3>Frame detection</h3>
 * <p>
 * Generally frame detection should be handled earlier in the pipeline by adding a
 * {@link DelimiterBasedFrameDecoder}, {@link FixedLengthFrameDecoder}, {@link LengthFieldBasedFrameDecoder},
 * or {@link LineBasedFrameDecoder}.
 * <p>
 * If a custom frame decoder is required, then one needs to be careful when implementing
 * one with {@link ByteToMessageDecoder}. Ensure there are enough bytes in the buffer for a
 * complete frame by checking {@link ByteBuf#readableBytes()}. If there are not enough bytes
 * for a complete frame, return without modifying the reader index to allow more bytes to arrive.
 * <p>
 * To check for complete frames without modifying the reader index, use methods like {@link ByteBuf#getInt(int)}.
 * One <strong>MUST</strong> use the reader index when using methods like {@link ByteBuf#getInt(int)}.
 * For example calling <tt>in.getInt(0)</tt> is assuming the frame starts at the beginning of the buffer, which
 * is not always the case. Use <tt>in.getInt(in.readerIndex())</tt> instead.
 * <h3>Pitfalls</h3>
 * <p>
 * Be aware that subclasses of {@link ByteToMessageDecoder} <strong>MUST NOT</strong>
 * annotated with {@link @Sharable}.
 * <p>
 * Some methods such as {@link ByteBuf#readBytes(int)} will cause a memory leak if the returned buffer
 * is not released or added to the <tt>out</tt> {@link List}. Use derived buffers like {@link ByteBuf#readSlice(int)}
 * to avoid leaking memory.
 */
// CLR: List<Object> out is IList<object> @out, and the 'in' parameter is @in (C# keywords).
public abstract class ByteToMessageDecoder : ChannelInboundHandlerAdapter
{
    /**
     * Cumulate {@link ByteBuf}s by merge them into one {@link ByteBuf}'s, using memory copies.
     */
    public static readonly ICumulator MERGE_CUMULATOR = new MergeCumulator();

    // CLR: the anonymous Cumulator classes are named nested classes.
    private sealed class MergeCumulator : ICumulator
    {
        public ByteBuf Cumulate(IByteBufAllocator alloc, ByteBuf cumulation, ByteBuf @in)
        {
            if (cumulation == @in)
            {
                // when the in buffer is the same as the cumulation it is doubly retained, release it once
                @in.Release();
                return cumulation;
            }

            if (!cumulation.IsReadable() && @in.IsContiguous)
            {
                // If cumulation is empty and input buffer is contiguous, use it directly
                cumulation.Release();
                return @in;
            }

            try
            {
                int required = @in.ReadableBytes;
                if (required > cumulation.MaxWritableBytes ||
                    required > cumulation.MaxFastWritableBytes && cumulation.RefCnt > 1 ||
                    cumulation.IsReadOnly)
                {
                    // Expand cumulation (by replacing it) under the following conditions:
                    // - cumulation cannot be resized to accommodate the additional data
                    // - cumulation can be expanded with a reallocation operation to accommodate but the buffer is
                    //   assumed to be shared (e.g. refCnt() > 1) and the reallocation may not be safe.
                    return ExpandCumulation(alloc, cumulation, @in);
                }

                cumulation.WriteBytes(@in, @in.ReaderIndex, required);
                @in.ReaderIndex = @in.WriterIndex;
                return cumulation;
            }
            finally
            {
                // We must release in all cases as otherwise it may produce a leak if writeBytes(...) throw
                // for whatever release (for example because of OutOfMemoryError)
                @in.Release();
            }
        }
    }

    /**
     * Cumulate {@link ByteBuf}s by add them to a {@link CompositeByteBuf} and so do no memory copy whenever possible.
     * Be aware that {@link CompositeByteBuf} use a more complex indexing implementation so depending on your use-case
     * and the decoder implementation this may be slower than just use the {@link #MERGE_CUMULATOR}.
     */
    public static readonly ICumulator COMPOSITE_CUMULATOR = new CompositeCumulator();

    private sealed class CompositeCumulator : ICumulator
    {
        public ByteBuf Cumulate(IByteBufAllocator alloc, ByteBuf cumulation, ByteBuf @in)
        {
            if (cumulation == @in)
            {
                // when the in buffer is the same as the cumulation it is doubly retained, release it once
                @in.Release();
                return cumulation;
            }

            if (!cumulation.IsReadable())
            {
                cumulation.Release();
                return @in;
            }

            CompositeByteBuf composite = null;

            try
            {
                if (cumulation is CompositeByteBuf && cumulation.RefCnt == 1)
                {
                    composite = (CompositeByteBuf) cumulation;
                    // Writer index must equal capacity if we are going to "write"
                    // new components to the end
                    if (composite.WriterIndex != composite.Capacity)
                    {
                        composite.Capacity = composite.WriterIndex;
                    }
                }
                else
                {
                    composite = alloc.CompositeBuffer(int.MaxValue).AddFlattenedComponents(cumulation, true);
                }

                composite.AddFlattenedComponents(@in, true);
                @in = null;
                return composite;
            }
            finally
            {
                if (@in != null)
                {
                    // We must release if the ownership was not transferred as otherwise it may produce a leak
                    @in.Release();

                    // Also release any new buffer allocated if we're not returning it
                    if (composite != null && composite != cumulation)
                    {
                        composite.Release();
                    }
                }
            }
        }
    }

    private const byte STATE_INIT = 0;
    private const byte STATE_CALLING_CHILD_DECODE = 1;
    private const byte STATE_HANDLER_REMOVED_PENDING = 2;

    // Used to guard the inputs for reentrant channelRead calls
    private Queue<object> inputMessages;
    internal ByteBuf cumulation;
    private ICumulator cumulator = MERGE_CUMULATOR;
    private bool singleDecode;
    private bool first;

    /**
     * This flag is used to determine if we need to call {@link ChannelHandlerContext#read()} to consume more data
     * when {@link ChannelConfig#isAutoRead()} is {@code false}.
     */
    private bool firedChannelRead;

    private bool selfFiredChannelRead;

    /**
     * A bitmask where the bits are defined as
     * <ul>
     *     <li>{@link #STATE_INIT}</li>
     *     <li>{@link #STATE_CALLING_CHILD_DECODE}</li>
     *     <li>{@link #STATE_HANDLER_REMOVED_PENDING}</li>
     * </ul>
     */
    private byte decodeState = STATE_INIT;
    private int discardAfterReads = 16;
    private int numReads;

    protected ByteToMessageDecoder()
    {
        EnsureNotSharable();
    }

    /**
     * If set then only one message is decoded on each {@link #channelRead(ChannelHandlerContext, Object)}
     * call. This may be useful if you need to do some protocol upgrade and want to make sure nothing is mixed up.
     *
     * Default is {@code false} as this has performance impacts.
     */
    public void SetSingleDecode(bool singleDecode)
    {
        this.singleDecode = singleDecode;
    }

    /**
     * If {@code true} then only one message is decoded on each
     * {@link #channelRead(ChannelHandlerContext, Object)} call.
     *
     * Default is {@code false} as this has performance impacts.
     */
    public virtual bool IsSingleDecode()
    {
        return singleDecode;
    }

    /**
     * Set the {@link Cumulator} to use for cumulate the received {@link ByteBuf}s.
     */
    public void SetCumulator(ICumulator cumulator)
    {
        this.cumulator = ObjectUtil.CheckNotNull(cumulator, "cumulator");
    }

    /**
     * Set the number of reads after which {@link ByteBuf#discardSomeReadBytes()} are called and so free up memory.
     * The default is {@code 16}.
     */
    public void SetDiscardAfterReads(int discardAfterReads)
    {
        CheckPositive(discardAfterReads, "discardAfterReads");

        this.discardAfterReads = discardAfterReads;
    }

    /**
     * Returns the actual number of readable bytes in the internal cumulative
     * buffer of this decoder. You usually do not need to rely on this value
     * to write a decoder. Use it only when you must use it at your own risk.
     * This method is a shortcut to {@link #internalBuffer() internalBuffer().readableBytes()}.
     */
    protected int ActualReadableBytes()
    {
        return InternalBuffer().ReadableBytes;
    }

    /**
     * Returns the internal cumulative buffer of this decoder. You usually
     * do not need to access the internal buffer directly to write a decoder.
     * Use it only when you must use it at your own risk.
     */
    protected ByteBuf InternalBuffer()
    {
        if (cumulation != null)
        {
            return cumulation;
        }
        else
        {
            return Unpooled.EmptyBuffer;
        }
    }

    public sealed override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        if (decodeState == STATE_CALLING_CHILD_DECODE)
        {
            decodeState = STATE_HANDLER_REMOVED_PENDING;
            return;
        }

        ByteBuf buf = cumulation;
        if (buf != null)
        {
            // Directly set this to null, so we are sure we not access it in any other method here anymore.
            cumulation = null;
            numReads = 0;
            int readable = buf.ReadableBytes;
            if (readable > 0)
            {
                ctx.FireChannelRead(buf);
                ctx.FireChannelReadComplete();
            }
            else
            {
                buf.Release();
            }
        }

        HandlerRemoved0(ctx);
    }

    /**
     * Gets called after the {@link ByteToMessageDecoder} was removed from the actual context and it doesn't handle
     * events anymore.
     */
    protected virtual void HandlerRemoved0(IChannelHandlerContext ctx)
    {
    }

    // CLR: the parameter keeps the source name 'input' (the source renames ChannelInboundHandler's 'msg' here).
    public override void ChannelRead(IChannelHandlerContext ctx, object input)
    {
        if (decodeState == STATE_INIT)
        {
            do
            {
                if (input is ByteBuf)
                {
                    selfFiredChannelRead = true;
                    CodecOutputList @out = CodecOutputList.NewInstance();

                    try
                    {
                        first = cumulation == null;
                        cumulation = cumulator.Cumulate(ctx.Alloc(),
                                first ? Unpooled.EmptyBuffer : cumulation, (ByteBuf) input);
                        CallDecode(ctx, cumulation, @out);
                    }
                    catch (DecoderException)
                    {
                        throw;
                    }
                    // CLR: Signal is an Exception here but a java.lang.Error in the source, which catch (Exception) does not
        // wrap; the filter keeps it out, as in ReplayingDecoder.
        catch (Exception e) when (e is not Signal)
                    {
                        throw new DecoderException(e);
                    }
                    finally
                    {
                        try
                        {
                            if (cumulation != null && !cumulation.IsReadable())
                            {
                                numReads = 0;

                                try
                                {
                                    cumulation.Release();
                                }
                                catch (IllegalReferenceCountException e)
                                {
                                    //noinspection ThrowFromFinallyBlock
                                    throw new IllegalReferenceCountException(
                                            GetType().Name +
                                                    "#decode() might have released its input buffer, " +
                                                    "or passed it down the pipeline without a retain() call, " +
                                                    "which is not allowed.", e);
                                }

                                cumulation = null;
                            }
                            else if (++numReads >= discardAfterReads)
                            {
                                // We did enough reads already try to discard some bytes, so we not risk to see a OOME.
                                // See https://github.com/netty/netty/issues/4275
                                numReads = 0;
                                DiscardSomeReadBytes();
                            }

                            int size = @out.Count;
                            firedChannelRead |= @out.InsertSinceRecycled();
                            FireChannelRead(ctx, @out, size);
                        }
                        finally
                        {
                            @out.Recycle();
                        }
                    }
                }
                else
                {
                    ctx.FireChannelRead(input);
                }
            } while (inputMessages != null && inputMessages.TryDequeue(out input));
        }
        else
        {
            // Reentrant call. Bail out here and let original call process our message.
            if (inputMessages == null)
            {
                inputMessages = new Queue<object>(2);
            }

            inputMessages.Enqueue(input);
        }
    }

    /**
     * Get {@code numElements} out of the {@link List} and forward these through the pipeline.
     */
    internal static void FireChannelRead(IChannelHandlerContext ctx, IList<object> msgs, int numElements)
    {
        if (msgs is CodecOutputList)
        {
            FireChannelRead(ctx, (CodecOutputList) msgs, numElements);
        }
        else
        {
            for (int i = 0; i < numElements; i++)
            {
                ctx.FireChannelRead(msgs[i]);
            }
        }
    }

    /**
     * Get {@code numElements} out of the {@link CodecOutputList} and forward these through the pipeline.
     */
    internal static void FireChannelRead(IChannelHandlerContext ctx, CodecOutputList msgs, int numElements)
    {
        for (int i = 0; i < numElements; i ++)
        {
            ctx.FireChannelRead(msgs.GetUnsafe(i));
        }
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        numReads = 0;
        DiscardSomeReadBytes();

        if (selfFiredChannelRead && !firedChannelRead && !ctx.Channel.Config.IsAutoRead())
        {
            ctx.Read();
        }

        firedChannelRead = false;
        selfFiredChannelRead = false;
        ctx.FireChannelReadComplete();
    }

    protected void DiscardSomeReadBytes()
    {
        if (cumulation != null && !first && cumulation.RefCnt == 1)
        {
            // discard some bytes if possible to make more room in the
            // buffer but only if the refCnt == 1  as otherwise the user may have
            // used slice().retain() or duplicate().retain().
            //
            // See:
            // - https://github.com/netty/netty/issues/2327
            // - https://github.com/netty/netty/issues/1764
            cumulation.DiscardSomeReadBytes();
        }
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        ChannelInputClosed(ctx, true);
    }

    public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        if (evt is ChannelInputShutdownEvent)
        {
            // The decodeLast method is invoked when a channelInactive event is encountered.
            // This method is responsible for ending requests in some situations and must be called
            // when the input has been shutdown.
            ChannelInputClosed(ctx, false);
        }

        base.UserEventTriggered(ctx, evt);
    }

    private void ChannelInputClosed(IChannelHandlerContext ctx, bool callChannelInactive)
    {
        CodecOutputList @out = CodecOutputList.NewInstance();

        try
        {
            ChannelInputClosed(ctx, @out);
        }
        catch (DecoderException)
        {
            throw;
        }
        // CLR: Signal is an Exception here but a java.lang.Error in the source, which catch (Exception) does not
        // wrap; the filter keeps it out, as in ReplayingDecoder.
        catch (Exception e) when (e is not Signal)
        {
            throw new DecoderException(e);
        }
        finally
        {
            try
            {
                if (cumulation != null)
                {
                    cumulation.Release();
                    cumulation = null;
                }

                int size = @out.Count;
                FireChannelRead(ctx, @out, size);

                if (size > 0)
                {
                    // Something was read, call fireChannelReadComplete()
                    ctx.FireChannelReadComplete();
                }

                if (callChannelInactive)
                {
                    ctx.FireChannelInactive();
                }
            }
            finally
            {
                // Recycle in all cases
                @out.Recycle();
            }
        }
    }

    /**
     * Called when the input of the channel was closed which may be because it changed to inactive or because of
     * {@link ChannelInputShutdownEvent}.
     */
    internal virtual void ChannelInputClosed(IChannelHandlerContext ctx, IList<object> @out)
    {
        if (cumulation != null)
        {
            CallDecode(ctx, cumulation, @out);

            // If callDecode(...) removed the handle from the pipeline we should not call decodeLast(...) as this would
            // be unexpected.
            if (!ctx.IsRemoved)
            {
                // Use Unpooled.EMPTY_BUFFER if cumulation become null after calling callDecode(...).
                // See https://github.com/netty/netty/issues/10802.
                ByteBuf buffer = cumulation == null ? Unpooled.EmptyBuffer : cumulation;
                DecodeLast(ctx, buffer, @out);
            }
        }
        else
        {
            DecodeLast(ctx, Unpooled.EmptyBuffer, @out);
        }
    }

    /**
     * Called once data should be decoded from the given {@link ByteBuf}. This method will call
     * {@link #decode(ChannelHandlerContext, ByteBuf, List)} as long as decoding should take place.
     *
     * @param ctx           the {@link ChannelHandlerContext} which this {@link ByteToMessageDecoder} belongs to
     * @param in            the {@link ByteBuf} from which to read data
     * @param out           the {@link List} to which decoded messages should be added
     */
    protected virtual void CallDecode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        try
        {
            while (@in.IsReadable())
            {
                int outSize = @out.Count;

                if (outSize > 0)
                {
                    FireChannelRead(ctx, @out, outSize);
                    @out.Clear();

                    // Check if this handler was removed before continuing with decoding.
                    // If it was removed, it is not safe to continue to operate on the buffer.
                    //
                    // See:
                    // - https://github.com/netty/netty/issues/4635
                    if (ctx.IsRemoved)
                    {
                        break;
                    }
                }

                int oldInputLength = @in.ReadableBytes;
                DecodeRemovalReentryProtection(ctx, @in, @out);

                // Check if this handler was removed before continuing the loop.
                // If it was removed, it is not safe to continue to operate on the buffer.
                //
                // See https://github.com/netty/netty/issues/1664
                if (ctx.IsRemoved)
                {
                    break;
                }

                if (@out.Count == 0)
                {
                    if (oldInputLength == @in.ReadableBytes)
                    {
                        break;
                    }
                    else
                    {
                        continue;
                    }
                }

                if (oldInputLength == @in.ReadableBytes)
                {
                    throw new DecoderException(
                            StringUtil.SimpleClassName(GetType()) +
                                    ".decode() did not read anything but decoded a message.");
                }

                if (IsSingleDecode())
                {
                    break;
                }
            }
        }
        catch (DecoderException)
        {
            throw;
        }
        // CLR: Signal is an Exception here but a java.lang.Error in the source, which catch (Exception) does not
        // wrap; the filter keeps it out, as in ReplayingDecoder.
        catch (Exception cause) when (cause is not Signal)
        {
            throw new DecoderException(cause);
        }
    }

    /**
     * Decode the from one {@link ByteBuf} to an other. This method will be called till either the input
     * {@link ByteBuf} has nothing to read when return from this method or till nothing was read from the input
     * {@link ByteBuf}.
     *
     * @param ctx           the {@link ChannelHandlerContext} which this {@link ByteToMessageDecoder} belongs to
     * @param in            the {@link ByteBuf} from which to read data
     * @param out           the {@link List} to which decoded messages should be added
     * @throws Exception    is thrown if an error occurs
     */
    protected abstract void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out);

    /**
     * Decode the from one {@link ByteBuf} to an other. This method will be called till either the input
     * {@link ByteBuf} has nothing to read when return from this method or till nothing was read from the input
     * {@link ByteBuf}.
     *
     * @param ctx           the {@link ChannelHandlerContext} which this {@link ByteToMessageDecoder} belongs to
     * @param in            the {@link ByteBuf} from which to read data
     * @param out           the {@link List} to which decoded messages should be added
     * @throws Exception    is thrown if an error occurs
     */
    internal void DecodeRemovalReentryProtection(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        decodeState = STATE_CALLING_CHILD_DECODE;

        try
        {
            Decode(ctx, @in, @out);
        }
        finally
        {
            if (inputMessages == null || inputMessages.Count == 0)
            {
                bool removePending = decodeState == STATE_HANDLER_REMOVED_PENDING;
                decodeState = STATE_INIT;

                if (removePending)
                {
                    FireChannelRead(ctx, @out, @out.Count);
                    @out.Clear();
                    HandlerRemoved(ctx);
                }
            }
        }
    }

    /**
     * Is called one last time when the {@link ChannelHandlerContext} goes in-active. Which means the
     * {@link #channelInactive(ChannelHandlerContext)} was triggered.
     *
     * By default, this will just call {@link #decode(ChannelHandlerContext, ByteBuf, List)} but sub-classes may
     * override this for some special cleanup operation.
     */
    protected virtual void DecodeLast(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        if (@in.IsReadable())
        {
            // Only call decode() if there is something left in the buffer to decode.
            // See https://github.com/netty/netty/issues/4386
            DecodeRemovalReentryProtection(ctx, @in, @out);
        }
    }

    internal static ByteBuf ExpandCumulation(IByteBufAllocator alloc, ByteBuf oldCumulation, ByteBuf @in)
    {
        int oldBytes = oldCumulation.ReadableBytes;
        int newBytes = @in.ReadableBytes;
        int totalBytes = oldBytes + newBytes;
        ByteBuf newCumulation = alloc.Buffer(alloc.CalculateNewCapacity(totalBytes, int.MaxValue));
        ByteBuf toRelease = newCumulation;

        try
        {
            // This avoids redundant checks and stack depth compared to calling writeBytes(...)
            newCumulation.SetBytes(0, oldCumulation, oldCumulation.ReaderIndex, oldBytes)
                .SetBytes(oldBytes, @in, @in.ReaderIndex, newBytes)
                .WriterIndex = totalBytes;
            @in.ReaderIndex = @in.WriterIndex;
            toRelease = oldCumulation;
            return newCumulation;
        }
        finally
        {
            toRelease.Release();
        }
    }

    /**
     * Cumulate {@link ByteBuf}s.
     */
    public interface ICumulator
    {
        /**
         * Cumulate the given {@link ByteBuf}s and return the {@link ByteBuf} that holds the cumulated bytes.
         * The implementation is responsible to correctly handle the life-cycle of the given {@link ByteBuf}s and so
         * call {@link ByteBuf#release()} if a {@link ByteBuf} is fully consumed.
         */
        ByteBuf Cumulate(IByteBufAllocator alloc, ByteBuf cumulation, ByteBuf @in);
    }
}
