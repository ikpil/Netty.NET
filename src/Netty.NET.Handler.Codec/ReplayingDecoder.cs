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

namespace Netty.NET.Handler.Codec;

/**
 * A specialized variation of {@link ByteToMessageDecoder} which enables implementation
 * of a non-blocking decoder in the blocking I/O paradigm.
 * <p>
 * The biggest difference between {@link ReplayingDecoder} and
 * {@link ByteToMessageDecoder} is that {@link ReplayingDecoder} allows you to
 * implement the {@code decode()} and {@code decodeLast()} methods just like
 * all required bytes were received already, rather than checking the
 * availability of the required bytes.  For example, the following
 * {@link ByteToMessageDecoder} implementation:
 * <pre>
 * public class IntegerHeaderFrameDecoder extends {@link ByteToMessageDecoder} {
 *
 *   {@code @Override}
 *   protected void decode({@link ChannelHandlerContext} ctx,
 *                           {@link ByteBuf} buf, List&lt;Object&gt; out) throws Exception {
 *
 *     if (buf.readableBytes() &lt; 4) {
 *        return;
 *     }
 *
 *     buf.markReaderIndex();
 *     int length = buf.readInt();
 *
 *     if (buf.readableBytes() &lt; length) {
 *        buf.resetReaderIndex();
 *        return;
 *     }
 *
 *     out.add(buf.readBytes(length));
 *   }
 * }
 * </pre>
 * is simplified like the following with {@link ReplayingDecoder}:
 * <pre>
 * public class IntegerHeaderFrameDecoder
 *      extends {@link ReplayingDecoder}&lt;{@link Void}&gt; {
 *
 *   protected void decode({@link ChannelHandlerContext} ctx,
 *                           {@link ByteBuf} buf, List&lt;Object&gt; out) throws Exception {
 *
 *     out.add(buf.readBytes(buf.readInt()));
 *   }
 * }
 * </pre>
 *
 * <h3>How does this work?</h3>
 * <p>
 * {@link ReplayingDecoder} passes a specialized {@link ByteBuf}
 * implementation which throws an {@link Error} of certain type when there's not
 * enough data in the buffer.  In the {@code IntegerHeaderFrameDecoder} above,
 * you just assumed that there will be 4 or more bytes in the buffer when
 * you call {@code buf.readInt()}.  If there's really 4 bytes in the buffer,
 * it will return the integer header as you expected.  Otherwise, the
 * {@link Error} will be raised and the control will be returned to
 * {@link ReplayingDecoder}.  If {@link ReplayingDecoder} catches the
 * {@link Error}, then it will rewind the {@code readerIndex} of the buffer
 * back to the 'initial' position (i.e. the beginning of the buffer) and call
 * the {@code decode(..)} method again when more data is received into the
 * buffer.
 * <p>
 * Please note that {@link ReplayingDecoder} always throws the same cached
 * {@link Error} instance to avoid the overhead of creating a new {@link Error}
 * and filling its stack trace for every throw.
 *
 * <h3>Limitations</h3>
 * <p>
 * At the cost of the simplicity, {@link ReplayingDecoder} enforces you a few
 * limitations:
 * <ul>
 * <li>Some buffer operations are prohibited.</li>
 * <li>Performance can be worse if the network is slow and the message
 *     format is complicated unlike the example above.  In this case, your
 *     decoder might have to decode the same part of the message over and over
 *     again.</li>
 * <li>You must keep in mind that {@code decode(..)} method can be called many
 *     times to decode a single message.  For example, the following code will
 *     not work:
 * <pre> public class MyDecoder extends {@link ReplayingDecoder}&lt;{@link Void}&gt; {
 *
 *   private final Queue&lt;Integer&gt; values = new LinkedList&lt;Integer&gt;();
 *
 *   {@code @Override}
 *   public void decode(.., {@link ByteBuf} buf, List&lt;Object&gt; out) throws Exception {
 *
 *     // A message contains 2 integers.
 *     values.offer(buf.readInt());
 *     values.offer(buf.readInt());
 *
 *     // This assertion will fail intermittently since values.offer()
 *     // can be called more than two times!
 *     assert values.size() == 2;
 *     out.add(values.poll() + values.poll());
 *   }
 * }</pre>
 *      The correct implementation looks like the following, and you can also
 *      utilize the 'checkpoint' feature which is explained in detail in the
 *      next section.
 * <pre> public class MyDecoder extends {@link ReplayingDecoder}&lt;{@link Void}&gt; {
 *
 *   private final Queue&lt;Integer&gt; values = new LinkedList&lt;Integer&gt;();
 *
 *   {@code @Override}
 *   public void decode(.., {@link ByteBuf} buf, List&lt;Object&gt; out) throws Exception {
 *
 *     // Revert the state of the variable that might have been changed
 *     // since the last partial decode.
 *     values.clear();
 *
 *     // A message contains 2 integers.
 *     values.offer(buf.readInt());
 *     values.offer(buf.readInt());
 *
 *     // Now we know this assertion will never fail.
 *     assert values.size() == 2;
 *     out.add(values.poll() + values.poll());
 *   }
 * }</pre>
 *     </li>
 * </ul>
 *
 * <h3>Improving the performance</h3>
 * <p>
 * Fortunately, the performance of a complex decoder implementation can be
 * improved significantly with the {@code checkpoint()} method.  The
 * {@code checkpoint()} method updates the 'initial' position of the buffer so
 * that {@link ReplayingDecoder} rewinds the {@code readerIndex} of the buffer
 * to the last position where you called the {@code checkpoint()} method.
 *
 * <h4>Calling {@code checkpoint(T)} with an {@link Enum}</h4>
 * <p>
 * Although you can just use {@code checkpoint()} method and manage the state
 * of the decoder by yourself, the easiest way to manage the state of the
 * decoder is to create an {@link Enum} type which represents the current state
 * of the decoder and to call {@code checkpoint(T)} method whenever the state
 * changes.  You can have as many states as you want depending on the
 * complexity of the message you want to decode:
 *
 * <pre>
 * public enum MyDecoderState {
 *   READ_LENGTH,
 *   READ_CONTENT;
 * }
 *
 * public class IntegerHeaderFrameDecoder
 *      extends {@link ReplayingDecoder}&lt;<strong>MyDecoderState</strong>&gt; {
 *
 *   private int length;
 *
 *   public IntegerHeaderFrameDecoder() {
 *     // Set the initial state.
 *     <strong>super(MyDecoderState.READ_LENGTH);</strong>
 *   }
 *
 *   {@code @Override}
 *   protected void decode({@link ChannelHandlerContext} ctx,
 *                           {@link ByteBuf} buf, List&lt;Object&gt; out) throws Exception {
 *     switch (state()) {
 *     case READ_LENGTH:
 *       length = buf.readInt();
 *       <strong>checkpoint(MyDecoderState.READ_CONTENT);</strong>
 *     case READ_CONTENT:
 *       ByteBuf frame = buf.readBytes(length);
 *       <strong>checkpoint(MyDecoderState.READ_LENGTH);</strong>
 *       out.add(frame);
 *       break;
 *     default:
 *       throw new Error("Shouldn't reach here.");
 *     }
 *   }
 * }
 * </pre>
 *
 * <h4>Calling {@code checkpoint()} with no parameter</h4>
 * <p>
 * An alternative way to manage the decoder state is to manage it by yourself.
 * <pre>
 * public class IntegerHeaderFrameDecoder
 *      extends {@link ReplayingDecoder}&lt;<strong>{@link Void}</strong>&gt; {
 *
 *   <strong>private boolean readLength;</strong>
 *   private int length;
 *
 *   {@code @Override}
 *   protected void decode({@link ChannelHandlerContext} ctx,
 *                           {@link ByteBuf} buf, List&lt;Object&gt; out) throws Exception {
 *     if (!readLength) {
 *       length = buf.readInt();
 *       <strong>readLength = true;</strong>
 *       <strong>checkpoint();</strong>
 *     }
 *
 *     if (readLength) {
 *       ByteBuf frame = buf.readBytes(length);
 *       <strong>readLength = false;</strong>
 *       <strong>checkpoint();</strong>
 *       out.add(frame);
 *     }
 *   }
 * }
 * </pre>
 *
 * <h3>Replacing a decoder with another decoder in a pipeline</h3>
 * <p>
 * If you are going to write a protocol multiplexer, you will probably want to
 * replace a {@link ReplayingDecoder} (protocol detector) with another
 * {@link ReplayingDecoder}, {@link ByteToMessageDecoder} or {@link MessageToMessageDecoder}
 * (actual protocol decoder).
 * It is not possible to achieve this simply by calling
 * {@link ChannelPipeline#replace(ChannelHandler, String, ChannelHandler)}, but
 * some additional steps are required:
 * <pre>
 * public class FirstDecoder extends {@link ReplayingDecoder}&lt;{@link Void}&gt; {
 *
 *     {@code @Override}
 *     protected void decode({@link ChannelHandlerContext} ctx,
 *                             {@link ByteBuf} buf, List&lt;Object&gt; out) {
 *         ...
 *         // Decode the first message
 *         Object firstMessage = ...;
 *
 *         // Add the second decoder
 *         ctx.pipeline().addLast("second", new SecondDecoder());
 *
 *         if (buf.isReadable()) {
 *             // Hand off the remaining data to the second decoder
 *             out.add(firstMessage);
 *             out.add(buf.readBytes(<b>super.actualReadableBytes()</b>));
 *         } else {
 *             // Nothing to hand off
 *             out.add(firstMessage);
 *         }
 *         // Remove the first decoder (me)
 *         ctx.pipeline().remove(this);
 *     }
 * </pre>
 * @param <S>
 *        the state type which is usually an {@link Enum}; use {@link Void} if state management is
 *        unused
 */
// CLR: List<Object> out is IList<object> @out and 'in' is @in (C# keywords). A Java Void state, "an uninstantiable
// placeholder class" (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/lang/Void.html), and the raw
// type are ReplayingDecoder<object> with a null state: C# needs a type argument and "System.Void cannot be used from
// C#" (https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0673).
public abstract class ReplayingDecoder<S> : ByteToMessageDecoder
{
    // CLR: static fields of a generic class exist once per closed type; the open type's name gives every
    // ReplayingDecoder<S> the same pooled Signal, as the source's single ReplayingDecoder.class does.
    internal static readonly Signal REPLAY = Signal.ValueOf(typeof(ReplayingDecoder<>), "REPLAY");

    private readonly ReplayingDecoderByteBuf replayable = new ReplayingDecoderByteBuf();
    private S state;
    private int checkpoint = -1;

    /**
     * Creates a new instance with no initial state (i.e: {@code null}).
     */
    // CLR: the initial state is default(S): null for a reference type, the zero value for a value type.
    protected ReplayingDecoder() : this(default)
    {
    }

    /**
     * Creates a new instance with the specified initial state.
     */
    protected ReplayingDecoder(S initialState)
    {
        state = initialState;
    }

    /**
     * Stores the internal cumulative buffer's reader position.
     */
    // CLR: a reader index set through the non-virtual ByteBuf.ReaderIndex of the decode() buffer reaches the
    // cumulation first (see ReplayingDecoderByteBuf.FlushReaderIndex).
    protected void Checkpoint()
    {
        replayable.FlushReaderIndex();
        checkpoint = InternalBuffer().ReaderIndex;
    }

    /**
     * Stores the internal cumulative buffer's reader position and updates
     * the current decoder state.
     */
    protected void Checkpoint(S state)
    {
        Checkpoint();
        State(state);
    }

    /**
     * Returns the current state of this decoder.
     * @return the current state of this decoder
     */
    protected S State()
    {
        return state;
    }

    /**
     * Sets the current state of this decoder.
     * @return the old state of this decoder
     */
    protected S State(S newState)
    {
        S oldState = state;
        state = newState;
        return oldState;
    }

    internal sealed override void ChannelInputClosed(IChannelHandlerContext ctx, IList<object> @out)
    {
        try
        {
            replayable.Terminate();
            if (cumulation != null)
            {
                CallDecode(ctx, InternalBuffer(), @out);
            }
            else
            {
                replayable.SetCumulation(Unpooled.EmptyBuffer);
            }
            DecodeLast(ctx, replayable, @out);
        }
        catch (Signal replay)
        {
            // Ignore
            replay.Expect(REPLAY);
        }
    }

    protected override void CallDecode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        replayable.SetCumulation(@in);
        try
        {
            while (@in.IsReadable())
            {
                int oldReaderIndex = checkpoint = @in.ReaderIndex;
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
                    outSize = 0;
                }

                S oldState = state;
                int oldInputLength = @in.ReadableBytes;
                try
                {
                    DecodeRemovalReentryProtection(ctx, replayable, @out);
                    // CLR: a reader index set through the non-virtual ByteBuf.ReaderIndex of the decode() buffer
                    // reaches the cumulation before it is read below (see ReplayingDecoderByteBuf.FlushReaderIndex).
                    replayable.FlushReaderIndex();

                    // Check if this handler was removed before continuing the loop.
                    // If it was removed, it is not safe to continue to operate on the buffer.
                    //
                    // See https://github.com/netty/netty/issues/1664
                    if (ctx.IsRemoved)
                    {
                        break;
                    }

                    if (outSize == @out.Count)
                    {
                        if (oldInputLength == @in.ReadableBytes && IsSameState(oldState, state))
                        {
                            throw new DecoderException(
                                    StringUtil.SimpleClassName(GetType()) + ".decode() must consume the inbound " +
                                    "data or change its state if it did not decode anything.");
                        }
                        else
                        {
                            // Previous data has been discarded or caused state transition.
                            // Probably it is reading on.
                            continue;
                        }
                    }
                }
                catch (Signal replay)
                {
                    replay.Expect(REPLAY);

                    // Check if this handler was removed before continuing the loop.
                    // If it was removed, it is not safe to continue to operate on the buffer.
                    //
                    // See https://github.com/netty/netty/issues/1664
                    if (ctx.IsRemoved)
                    {
                        break;
                    }

                    // Return to the checkpoint (or oldPosition) and retry.
                    int checkpoint = this.checkpoint;
                    if (checkpoint >= 0)
                    {
                        @in.ReaderIndex = checkpoint;
                    }
                    else
                    {
                        // Called by cleanup() - no need to maintain the readerIndex
                        // anymore because the buffer has been released already.
                    }
                    break;
                }

                if (oldReaderIndex == @in.ReaderIndex && IsSameState(oldState, state))
                {
                    throw new DecoderException(
                           StringUtil.SimpleClassName(GetType()) + ".decode() method must consume the inbound data " +
                           "or change its state if it decoded something.");
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
        // CLR: Signal is an Exception here, but a java.lang.Error ("a subclass of Throwable", not of Exception:
        // https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/lang/Error.html) in the source, which
        // catch (Exception) does not wrap; the filter keeps it out ("An exception filter is a Boolean expression that
        // follows the when keyword", https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/exception-handling-statements).
        catch (Exception cause) when (cause is not Signal)
        {
            throw new DecoderException(cause);
        }
    }

    // CLR: the source compares the states with ==, reference identity. A value-type S compares by value, since
    // ReferenceEquals of boxed value types "returns false" (https://learn.microsoft.com/en-us/dotnet/api/system.object.referenceequals),
    // through EqualityComparer<S>.Default (https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.equalitycomparer-1.default).
    private static bool IsSameState(S a, S b)
    {
        return typeof(S).IsValueType ? EqualityComparer<S>.Default.Equals(a, b) : ReferenceEquals(a, b);
    }
}
