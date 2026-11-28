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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Compression.Bzip2Constants;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * Compresses a {@link ByteBuf} using the Bzip2 algorithm.
 *
 * See <a href="https://en.wikipedia.org/wiki/Bzip2">Bzip2</a>.
 */
// CLR: encode(ctx, in, out) is Encode(IChannelHandlerContext ctx, ByteBuf @in, ByteBuf @out) of the codec-base
// MessageToByteEncoder<ByteBuf>, and super(ByteBuf.class) is base(typeof(ByteBuf)). ChannelFuture and ChannelPromise are
// Task (docs/transport-porting.md): the outbound close(ctx, promise) is CloseAsync(ctx, cancellationToken), and the
// encoder's own close() and close(ChannelPromise) fold into the one Task-returning CloseAsync(CancellationToken), as
// EmbeddedChannel folds close() and close(promise) (docs/transport-porting.md: "The ChannelPromise overloads fold into
// the Task-returning members"). isClosed() is IsClosed(), ctx() is Ctx() next to the field ctx, and the private enum State
// is a nested C# enum. IllegalArgumentException and IllegalStateException are ArgumentException and
// InvalidOperationException (docs/codec-base-porting.md); the number in the constructor message uses the invariant
// culture. The volatile fields stay volatile.
public class Bzip2Encoder : MessageToByteEncoder<ByteBuf>
{
    /**
     * Current state of stream.
     */
    private enum State
    {
        INIT,
        INIT_BLOCK,
        WRITE_DATA,
        CLOSE_BLOCK
    }

    private State currentState = State.INIT;

    /**
     * A writer that provides bit-level writes.
     */
    private readonly Bzip2BitWriter writer = new Bzip2BitWriter();

    /**
     * The declared maximum block size of the stream (before final run-length decoding).
     */
    private readonly int streamBlockSize;

    /**
     * The merged CRC of all blocks compressed so far.
     */
    private int streamCRC;

    /**
     * The compressor for the current block.
     */
    private Bzip2BlockCompressor blockCompressor;

    /**
     * (@code true} if the compressed stream has been finished, otherwise {@code false}.
     */
    private volatile bool finished;

    /**
     * Used to interact with its {@link ChannelPipeline} and other handlers.
     */
    private volatile IChannelHandlerContext ctx;

    /**
     * Creates a new bzip2 encoder with the maximum (900,000 byte) block size.
     */
    public Bzip2Encoder() : this(MAX_BLOCK_SIZE)
    {
    }

    /**
     * Creates a new bzip2 encoder with the specified {@code blockSizeMultiplier}.
     * @param blockSizeMultiplier
     *        The Bzip2 block size as a multiple of 100,000 bytes (minimum {@code 1}, maximum {@code 9}).
     *        Larger block sizes require more memory for both compression and decompression,
     *        but give better compression ratios. {@code 9} will usually be the best value to use.
     */
    public Bzip2Encoder(int blockSizeMultiplier) : base(typeof(ByteBuf))
    {
        if (blockSizeMultiplier < MIN_BLOCK_SIZE || blockSizeMultiplier > MAX_BLOCK_SIZE)
        {
            throw new ArgumentException(
                    "blockSizeMultiplier: " + blockSizeMultiplier.ToString(CultureInfo.InvariantCulture) +
                    " (expected: 1-9)");
        }

        streamBlockSize = blockSizeMultiplier * BASE_BLOCK_SIZE;
    }

    // CLR: the Java switch falls through from state to state; C# switch sections cannot, so each "// fall through" is
    // followed by goto case ("To imitate the fall-through behavior and pass control to other switch section, you can use
    // the goto statement", https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/selection-statements).
    // The local blockCompressor of WRITE_DATA is in scope in the whole C# switch block, so INIT_BLOCK writes the field as
    // this.blockCompressor ("Cannot use local variable 'name' before it is declared",
    // https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0841).
    protected override void Encode(IChannelHandlerContext ctx, ByteBuf @in, ByteBuf @out)
    {
        if (finished)
        {
            @out.WriteBytes(@in);
            return;
        }

        for (;;)
        {
            switch (currentState)
            {
                case State.INIT:
                    @out.EnsureWritable(4);
                    @out.WriteMedium(MAGIC_NUMBER);
                    @out.WriteByte('0' + streamBlockSize / BASE_BLOCK_SIZE);
                    currentState = State.INIT_BLOCK;
                    // fall through
                    goto case State.INIT_BLOCK;
                case State.INIT_BLOCK:
                    this.blockCompressor = new Bzip2BlockCompressor(writer, streamBlockSize);
                    currentState = State.WRITE_DATA;
                    // fall through
                    goto case State.WRITE_DATA;
                case State.WRITE_DATA:
                    if (!@in.IsReadable())
                    {
                        return;
                    }

                    Bzip2BlockCompressor blockCompressor = this.blockCompressor;
                    int length = Math.Min(@in.ReadableBytes, blockCompressor.AvailableSize());
                    int bytesWritten = blockCompressor.Write(@in, @in.ReaderIndex, length);
                    @in.SkipBytes(bytesWritten);

                    if (!blockCompressor.IsFull())
                    {
                        if (@in.IsReadable())
                        {
                            break;
                        }
                        else
                        {
                            return;
                        }
                    }

                    currentState = State.CLOSE_BLOCK;
                    // fall through
                    goto case State.CLOSE_BLOCK;
                case State.CLOSE_BLOCK:
                    CloseBlock(@out);
                    currentState = State.INIT_BLOCK;
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }
    }

    /**
     * Close current block and update {@link #streamCRC}.
     */
    private void CloseBlock(ByteBuf @out)
    {
        Bzip2BlockCompressor blockCompressor = this.blockCompressor;
        if (!blockCompressor.IsEmpty())
        {
            blockCompressor.Close(@out);
            int blockCRC = blockCompressor.Crc();
            streamCRC = (streamCRC << 1 | streamCRC >>> 31) ^ blockCRC;
        }
    }

    /**
     * Returns {@code true} if and only if the end of the compressed stream has been reached.
     */
    public bool IsClosed()
    {
        return finished;
    }

    /**
     * Close this {@link Bzip2Encoder} and so finish the encoding.
     *
     * The returned {@link ChannelFuture} will be notified once the operation completes.
     */
    /**
     * Close this {@link Bzip2Encoder} and so finish the encoding.
     * The given {@link ChannelFuture} will be notified once the operation
     * completes and will also be returned.
     */
    // CLR: close() and close(promise) are the one Task-returning CloseAsync: close() passed ctx().newPromise(), so a call
    // without a token is close(). The promise is the returned Task. On the event loop it is the Task of the footer write
    // (finishEncode returns ctx.writeAndFlush(footer, promise)); otherwise a TaskCompletionSource stands for the promise,
    // the anonymous Runnable is a lambda, and PromiseNotifier.cascade(f, promise) copies the outcome of f with
    // TaskCompletionSource.TrySetFromTask, which copies "the completion status (including exception or cancellation
    // information)" (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource.trysetfromtask?view=net-10.0;
    // the PromiseNotifier mapping of docs/common-task-composition.md, as in EncoderUtil).
    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        IChannelHandlerContext ctx = Ctx();
        IEventExecutor executor = ctx.Executor;
        if (executor.InEventLoop())
        {
            return FinishEncode(ctx, cancellationToken);
        }
        else
        {
            TaskCompletionSource promise = new TaskCompletionSource();
            executor.Execute(() =>
            {
                Task f = FinishEncode(Ctx(), cancellationToken);
                _ = f.ContinueWith(static (t, state) => ((TaskCompletionSource) state).TrySetFromTask(t), promise,
                        CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            });
            return promise.Task;
        }
    }

    // CLR: close(ctx, promise) is CloseAsync(ctx, cancellationToken), whose returned Task is the promise
    // (docs/transport-porting.md); ctx.newPromise() is a fresh promise that the caller's close cannot cancel, so the
    // footer write gets CancellationToken.None, and EncoderUtil.closeAfterFinishEncode takes the token of this close.
    public override Task CloseAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        Task f = FinishEncode(ctx, CancellationToken.None);
        return EncoderUtil.CloseAfterFinishEncode(ctx, f, cancellationToken);
    }

    // CLR: finishEncode(ctx, promise) returns the Task of the write that the promise would have tracked;
    // promise.setSuccess() for an already finished encoder is the completed Task.
    private Task FinishEncode(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        if (finished)
        {
            return Task.CompletedTask;
        }

        finished = true;

        ByteBuf footer = ctx.Alloc().Buffer();
        CloseBlock(footer);

        int streamCRC = this.streamCRC;
        Bzip2BitWriter writer = this.writer;

        try
        {
            writer.WriteBits(footer, 24, END_OF_STREAM_MAGIC_1);
            writer.WriteBits(footer, 24, END_OF_STREAM_MAGIC_2);
            writer.WriteInt(footer, streamCRC);
            writer.Flush(footer);
        }
        finally
        {
            blockCompressor = null;
        }

        return ctx.WriteAndFlushAsync(footer, cancellationToken);
    }

    private IChannelHandlerContext Ctx()
    {
        IChannelHandlerContext ctx = this.ctx;
        if (ctx == null)
        {
            throw new InvalidOperationException("not added to a pipeline");
        }

        return ctx;
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        this.ctx = ctx;
    }
}
