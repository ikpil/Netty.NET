/*
 * Copyright 2016 The Netty Project
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

using System.Collections.Generic;
using System.Diagnostics;
using Netty.NET.Common;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Flow;

/**
 * The {@link FlowControlHandler} ensures that only one message per {@code read()} is sent downstream.
 * <p>
 * Classes such as {@link ByteToMessageDecoder} or {@link MessageToByteEncoder} are free to emit as
 * many events as they like for any given input. A channel's auto reading configuration doesn't usually
 * apply in these scenarios. This is causing problems in downstream {@link ChannelHandler}s that would
 * like to hold subsequent events while they're processing one event. It's a common problem with the
 * {@code HttpObjectDecoder} that will very often fire an {@code HttpRequest} that is immediately followed
 * by a {@code LastHttpContent} event.
 *
 * <pre>{@code
 * ChannelPipeline pipeline = ...;
 *
 * pipeline.addLast(new HttpServerCodec());
 * pipeline.addLast(new FlowControlHandler());
 *
 * pipeline.addLast(new MyExampleHandler());
 *
 * class MyExampleHandler extends ChannelInboundHandlerAdapter {
 *   @Override
 *   public void channelRead(ChannelHandlerContext ctx, Object msg) {
 *     if (msg instanceof HttpRequest) {
 *       ctx.channel().config().setAutoRead(false);
 *
 *       // The FlowControlHandler will hold any subsequent events that
 *       // were emitted by HttpObjectDecoder until auto reading is turned
 *       // back on or Channel#read() is being called.
 *     }
 *   }
 * }
 * }</pre>
 *
 * @see ChannelConfig#setAutoRead(boolean)
 */
public class FlowControlHandler : ChannelDuplexHandler
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(FlowControlHandler));

    private readonly bool releaseMessages;

    private RecyclableArrayDeque queue;

    private IChannelConfig config;

    /**
     * Number of unsatisfied downstream {@code read()} calls. A downstream {@code read()} is considered unsatisfied
     * if auto-read is off and if it has not yet been paired with a {@code fireChannelRead} or
     * a cumulative {@code fireChannelReadComplete}.
     * <p>
     * A {@code read()} can be satisfied in three ways, whichever comes first:
     * <ul>
     *     <li>inside the {@code read()} call itself, by {@code dequeue()}ing a message</li>
     *     <li>in a {@code channelRead()}</li>
     *     <li>in a {@code channelReadComplete()}</li>
     * </ul>
     * A {@code read()} can be satisfied with auto-read on.
     * <p>
     * When one or more {@code read()} calls are unsatisfied, a downstream {@code channelReadComplete} is fired
     * only when either of the following happens:
     * <ul>
     *     <li>auto-read is off and {@code unsatisfiedReads} returns to zero after {@code dequeue()}ing, or</li>
     *     <li>an upstream {@code channelReadComplete} arrives</li>
     * </ul>
     */
    private int unsatisfiedReads;

    /**
     * {@code true} while a {@link #dequeue(ChannelHandlerContext)} loop is on the stack.
     */
    private bool dequeuing;

    public FlowControlHandler() : this(true)
    {
    }

    public FlowControlHandler(bool releaseMessages)
    {
        this.releaseMessages = releaseMessages;
    }

    /**
     * Determine if the underlying {@link Queue} is empty. This method exists for
     * testing, debugging and inspection purposes and it is not Thread safe!
     */
    internal bool IsQueueEmpty()
    {
        return queue == null || queue.Count == 0;
    }

    /**
     * Releases all messages and destroys the {@link Queue}.
     */
    private void Destroy()
    {
        if (queue != null)
        {

            if (queue.Count != 0)
            {
                logger.Trace("Non-empty queue: {}", queue);

                if (releaseMessages)
                {
                    object msg;

                    while ((msg = Poll(queue)) != null)
                    {
                        ReferenceCountUtil.SafeRelease(msg);
                    }
                }
            }

            queue.Recycle();
            queue = null;
        }
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        config = ctx.Channel.Config;
    }

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        base.HandlerRemoved(ctx);

        if (!IsQueueEmpty())
        {
            unsatisfiedReads = queue.Count;
            Dequeue(ctx);
            ctx.FireChannelReadComplete();
        }

        Destroy();
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        Destroy();
        ctx.FireChannelInactive();
    }

    // CLR: the Java assert is Debug.Assert.
    public override void Read(IChannelHandlerContext ctx)
    {
        if (!config.IsAutoRead())
        {
            unsatisfiedReads++;
        }

        bool didSatisfyARead = Dequeue(ctx);
        bool isAutoRead = config.IsAutoRead();
        if (!didSatisfyARead || isAutoRead)
        {
            Debug.Assert(unsatisfiedReads > 0 || isAutoRead);
            // We either could not satisfy the read or auto-read is on.
            // In both cases we need to delegate the read upstream.
            ctx.Read();
        }
        else if (unsatisfiedReads == 0 && !dequeuing)
        {
            // Auto-read is off, and we have satisfied all reads.
            // As such, we can complete the current read cycle. && !dequeueing makes sure we are completing the
            // read cycle only once in the top-most read() call.
            ctx.FireChannelReadComplete();
        }
        else
        {
            // Auto-read is off, and either reads are still unsatisfied or we are nested in a dequeue.
            // Wait for the outermost call, an upstream channelRead() or a channelReadComplete().
        }
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (queue == null)
        {
            queue = RecyclableArrayDeque.NewInstance();
        }

        queue.Enqueue(msg);

        if (Dequeue(ctx))
        {
            if (!config.IsAutoRead() && unsatisfiedReads == 0 && !dequeuing)
            {
                ctx.FireChannelReadComplete();
            }
        }
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        // Upstream closed the read cycle. Collapse every outstanding read() into a single downstream
        // channelReadComplete; spurious upstream completions with no pending read are dropped.
        if (config.IsAutoRead() || unsatisfiedReads > 0)
        {
            unsatisfiedReads = 0;
            ctx.FireChannelReadComplete();
        }
    }

    /**
     * Dequeues messages while auto-read is enabled or downstream reads are unsatisfied, and updates
     * {@code unsatisfiedReads} accordingly.
     *
     * @see #read(ChannelHandlerContext)
     * @see #channelRead(ChannelHandlerContext, Object)
     */
    private bool Dequeue(IChannelHandlerContext ctx)
    {
        bool didSatisfyARead = false;

        bool wasDequeuing = dequeuing;
        dequeuing = true;

        try
        {
            // fireChannelRead(...) may call ctx.read() and so this method may be re-entered. Because of that
            // we need to check if queue was set to null in the meantime and, if so, break out of the loop.
            while (queue != null && (config.IsAutoRead() || unsatisfiedReads > 0))
            {
                object msg = Poll(queue);
                if (msg == null)
                {
                    break;
                }

                if (unsatisfiedReads > 0)
                {
                    unsatisfiedReads--;
                }

                ctx.FireChannelRead(msg);

                didSatisfyARead = true;
            }

            if (queue != null && queue.Count == 0)
            {
                queue.Recycle();
                queue = null;
            }

            return didSatisfyARead;
        }
        finally
        {
            dequeuing = wasDequeuing;
        }
    }

    // CLR: Queue.poll() returns null when the queue is empty; Queue<T>.TryDequeue reports it instead.
    private static object Poll(RecyclableArrayDeque queue)
    {
        return queue.TryDequeue(out object msg) ? msg : null;
    }

    /**
     * A recyclable {@link ArrayDeque}.
     */
    // CLR: ArrayDeque<Object> is Queue<object>, the BCL circular-array FIFO queue: the handler uses only the Queue
    // operations offer/poll/size/isEmpty/clear (Enqueue, TryDequeue, Count, Clear). serialVersionUID is JVM
    // serialization metadata and has no counterpart. The anonymous Recycler subclass is Recycler.Create with the
    // newObject factory, as the Transport port pools its WriteTask and ChannelOutboundBuffer.Entry
    // (src/Netty.NET.Transport/Channels/AbstractChannelHandlerContext.cs), and ObjectPool.Handle is IRecyclerHandle.
    private sealed class RecyclableArrayDeque : Queue<object>
    {

        /**
         * A value of {@code 2} should be a good choice for most scenarios.
         */
        private const int DEFAULT_NUM_ELEMENTS = 2;

        private static readonly Recycler<RecyclableArrayDeque> RECYCLER =
                Recycler.Create<RecyclableArrayDeque>(handle => new RecyclableArrayDeque(DEFAULT_NUM_ELEMENTS, handle));

        public static RecyclableArrayDeque NewInstance()
        {
            return RECYCLER.Get();
        }

        private readonly IRecyclerHandle<RecyclableArrayDeque> handle;

        private RecyclableArrayDeque(int numElements, IRecyclerHandle<RecyclableArrayDeque> handle) : base(numElements)
        {
            this.handle = handle;
        }

        public void Recycle()
        {
            Clear();
            handle.Recycle(this);
        }

        // CLR: the trace message prints the queue as AbstractCollection.toString() does ("[a, b]"); Queue<T> inherits
        // Object.ToString(), which returns the type name.
        public override string ToString()
        {
            return "[" + string.Join(", ", this) + "]";
        }
    }
}
