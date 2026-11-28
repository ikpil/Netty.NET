/*
 * Copyright 2013 The Netty Project
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
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Memcache;
using Netty.NET.Transport.Channels;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Memcache.Binary;

/**
 * Decoder for both {@link BinaryMemcacheRequest} and {@link BinaryMemcacheResponse}.
 * <p/>
 * The difference in the protocols (header) is implemented by the subclasses.
 */
[UnstableApi]
public abstract class AbstractBinaryMemcacheDecoder<M>
    : AbstractMemcacheObjectDecoder
    where M : IBinaryMemcacheMessage
{

    public const int DEFAULT_MAX_CHUNK_SIZE = 8192;

    private readonly int chunkSize;

    private M currentMessage;
    private int alreadyReadChunkSize;

    private State state = State.READ_HEADER;

    /**
     * Create a new {@link AbstractBinaryMemcacheDecoder} with default settings.
     */
    protected AbstractBinaryMemcacheDecoder() : this(DEFAULT_MAX_CHUNK_SIZE)
    {
    }

    /**
     * Create a new {@link AbstractBinaryMemcacheDecoder} with custom settings.
     *
     * @param chunkSize the maximum chunk size of the payload.
     */
    protected AbstractBinaryMemcacheDecoder(int chunkSize)
    {
        CheckPositiveOrZero(chunkSize, "chunkSize");

        this.chunkSize = chunkSize;
    }

    // CLR: a C# switch section cannot fall through to the next one, so each section that falls through in the source
    // ends with goto case to the next state (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/selection-statements).
    // The default branch throws UnreachableException for java.lang.Error, as codec-base LengthFieldPrepender does
    // (docs/codec-base-porting-manifest.json; https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.unreachableexception).
    // extrasLength() & 0xFF of the System.Byte value is the same unsigned length as of the Java signed byte.
    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        switch (state)
        {
            case State.READ_HEADER:
                try
            {
                if (@in.ReadableBytes < 24)
                {
                    return;
                }

                ResetDecoder();

                currentMessage = DecodeHeader(@in);
                ValidateHeader(currentMessage);
                state = State.READ_EXTRAS;
            }
            catch (Exception e)
            {
                ResetDecoder();
                @out.Add(InvalidMessage(e));
                return;
            }

                goto case State.READ_EXTRAS;
            case State.READ_EXTRAS:
                try
            {
                int extrasLength = currentMessage.ExtrasLength() & 0xFF;
                if (extrasLength > 0)
                {
                    if (@in.ReadableBytes < extrasLength)
                    {
                        return;
                    }

                    currentMessage.SetExtras(@in.ReadRetainedSlice(extrasLength));
                }

                state = State.READ_KEY;
            }
            catch (Exception e)
            {
                ResetDecoder();
                @out.Add(InvalidMessage(e));
                return;
            }

                goto case State.READ_KEY;
            case State.READ_KEY:
                try
            {
                int keyLength = currentMessage.KeyLength() & 0xFFFF;
                if (keyLength > 0)
                {
                    if (@in.ReadableBytes < keyLength)
                    {
                        return;
                    }

                    currentMessage.SetKey(@in.ReadRetainedSlice(keyLength));
                }

                @out.Add(currentMessage.Retain());
                state = State.READ_CONTENT;
            }
            catch (Exception e)
            {
                ResetDecoder();
                @out.Add(InvalidMessage(e));
                return;
            }

                goto case State.READ_CONTENT;
            case State.READ_CONTENT:
                try
            {
                int valueLength = currentMessage.TotalBodyLength()
                    - (currentMessage.KeyLength() & 0xFFFF)
                    - (currentMessage.ExtrasLength() & 0xFF);
                int toRead = @in.ReadableBytes;

                if (valueLength > 0)
                {
                    if (toRead == 0)
                    {
                        return;
                    }

                    if (toRead > chunkSize)
                    {
                        toRead = chunkSize;
                    }

                    int remainingLength = valueLength - alreadyReadChunkSize;
                    if (toRead > remainingLength)
                    {
                        toRead = remainingLength;
                    }

                    ByteBuf chunkBuffer = @in.ReadRetainedSlice(toRead);

                    IMemcacheContent chunk;

                    if ((alreadyReadChunkSize += toRead) >= valueLength)
                    {
                        chunk = new DefaultLastMemcacheContent(chunkBuffer);
                    }
                    else
                    {
                        chunk = new DefaultMemcacheContent(chunkBuffer);
                    }

                    @out.Add(chunk);
                    if (alreadyReadChunkSize < valueLength)
                    {
                        return;
                    }
                }
                else
                {
                    @out.Add(ILastMemcacheContent.EMPTY_LAST_CONTENT);
                }

                ResetDecoder();
                state = State.READ_HEADER;
                return;
            }
            catch (Exception e)
            {
                ResetDecoder();
                @out.Add(InvalidChunk(e));
                return;
            }
            case State.BAD_MESSAGE:
                @in.SkipBytes(ActualReadableBytes());
                return;
            default:
                throw new UnreachableException("Unexpected state reached: " + state);
        }
    }

    /**
     * Validates the length fields of a decoded header against each other.
     * <p/>
     * A header that fails this check can not be framed, so decoding stops permanently rather than attempting to
     * resynchronize, see the class documentation.
     * <p/>
     * Note that {@code keyLength} and {@code extrasLength} are unsigned in the protocol and are interpreted as such
     * here, see {@link BinaryMemcacheMessage#keyLength()} and {@link BinaryMemcacheMessage#extrasLength()}.
     *
     * @param header the decoded header to validate.
     * @throws CorruptedFrameException if the lengths in the header are inconsistent.
     */
    // CLR: the numbers of the messages use the invariant culture, because Java's decimal text does not depend on a
    // culture while Int32.ToString() formats with the current culture (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
    private static void ValidateHeader(IBinaryMemcacheMessage header)
    {
        int totalBodyLength = header.TotalBodyLength();
        // The protocol defines totalBodyLength as an unsigned 32 bit value. A body that does not fit into a
        // positive int can not be represented, and is never legitimate.
        if (totalBodyLength < 0)
        {
            throw new CorruptedFrameException(
                "totalBodyLength must neither be negative nor be larger than " +
                int.MaxValue.ToString(CultureInfo.InvariantCulture) + ", but was: "
                    + (totalBodyLength & 0xFFFFFFFFL).ToString(CultureInfo.InvariantCulture));
        }

        // The protocol defines totalBodyLength as the "Length in bytes of extra + key + value", so the extras and
        // the key on their own can never be longer than the total body.
        int extrasAndKeyLength = (header.ExtrasLength() & 0xFF) + (header.KeyLength() & 0xFFFF);
        if (extrasAndKeyLength > totalBodyLength)
        {
            throw new CorruptedFrameException(
                "extrasLength + keyLength must not be larger than totalBodyLength, but was: "
                    + extrasAndKeyLength.ToString(CultureInfo.InvariantCulture) + " > " +
                    totalBodyLength.ToString(CultureInfo.InvariantCulture));
        }
    }

    /**
     * Helper method to create a message indicating a invalid decoding result.
     *
     * @param cause the cause of the decoding failure.
     * @return a valid message indicating failure.
     */
    private M InvalidMessage(Exception cause)
    {
        state = State.BAD_MESSAGE;
        M message = BuildInvalidMessage();
        message.SetDecoderResult(DecoderResult.Failure(cause));
        return message;
    }

    /**
     * Helper method to create a content chunk indicating a invalid decoding result.
     *
     * @param cause the cause of the decoding failure.
     * @return a valid content chunk indicating failure.
     */
    private IMemcacheContent InvalidChunk(Exception cause)
    {
        state = State.BAD_MESSAGE;
        IMemcacheContent chunk = new DefaultLastMemcacheContent(Unpooled.EmptyBuffer);
        chunk.SetDecoderResult(DecoderResult.Failure(cause));
        return chunk;
    }

    /**
     * When the channel goes inactive, release all frames to prevent data leaks.
     *
     * @param ctx handler context
     * @throws Exception
     */
    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        base.ChannelInactive(ctx);

        ResetDecoder();
    }

    /**
     * Prepare for next decoding iteration.
     */
    protected void ResetDecoder()
    {
        if (currentMessage != null)
        {
            currentMessage.Release();
            currentMessage = default;
        }

        alreadyReadChunkSize = 0;
    }

    /**
     * Decode and return the parsed {@link BinaryMemcacheMessage}.
     *
     * @param in the incoming buffer.
     * @return the decoded header.
     */
    protected abstract M DecodeHeader(ByteBuf @in);

    /**
     * Helper method to create a upstream message when the incoming parsing did fail.
     *
     * @return a message indicating a decoding failure.
     */
    protected abstract M BuildInvalidMessage();

    /**
     * Contains all states this decoder can possibly be in.
     * <p/>
     * Note that most of the states can be optional, the only one required is reading
     * the header ({@link #READ_HEADER}. All other steps depend on the length fields
     * in the header and will be executed conditionally.
     */
    internal enum State
    {
        /**
         * Currently reading the header portion.
         */
        READ_HEADER,

        /**
         * Currently reading the extras portion (optional).
         */
        READ_EXTRAS,

        /**
         * Currently reading the key portion (optional).
         */
        READ_KEY,

        /**
         * Currently reading the value chunks (optional).
         */
        READ_CONTENT,

        /**
         * Something went wrong while decoding the message or chunks.
         */
        BAD_MESSAGE
    }

}
