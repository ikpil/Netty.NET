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

using System.Collections.Generic;
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Memcache;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Memcache.Binary;

/**
 * The client codec that combines the proper encoder and decoder.
 * <p/>
 * Use this codec if you want to implement a memcache client that speaks the binary protocol. It
 * combines both the {@link BinaryMemcacheResponseDecoder} and the {@link BinaryMemcacheRequestEncoder}.
 * <p/>
 * Optionally, it counts the number of outstanding responses and raises an exception if - on connection
 * close - the list is not 0 (this is turned off by default). You can also define a chunk size for the
 * content, which defaults to 8192. This chunk size is the maximum, so if smaller chunks arrive they
 * will be passed up the pipeline and not queued up to the chunk size.
 */
// CLR: the inner classes Encoder and Decoder read fields of the enclosing codec; a C# nested class has no enclosing
// instance, so the codec is passed to their constructors
// (https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/nested-types). The static
// DEFAULT_MAX_CHUNK_SIZE of the generic AbstractBinaryMemcacheDecoder is reached through the closed type that this
// codec decodes (https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/generics/generics-in-the-run-time).
// AtomicLong is the Common port's Netty.NET.Common.Concurrent.AtomicLong.
[UnstableApi]
public sealed class BinaryMemcacheClientCodec :
        CombinedChannelDuplexHandler<BinaryMemcacheResponseDecoder, BinaryMemcacheRequestEncoder>
{

    private readonly bool failOnMissingResponse;
    private readonly AtomicLong requestResponseCounter = new AtomicLong();

    /**
     * Create a new {@link BinaryMemcacheClientCodec} with the default settings applied.
     */
    public BinaryMemcacheClientCodec() : this(AbstractBinaryMemcacheDecoder<IBinaryMemcacheResponse>.DEFAULT_MAX_CHUNK_SIZE)
    {
    }

    /**
     * Create a new {@link BinaryMemcacheClientCodec} and set a custom chunk size.
     *
     * @param decodeChunkSize the maximum chunk size.
     */
    public BinaryMemcacheClientCodec(int decodeChunkSize) : this(decodeChunkSize, false)
    {
    }

    /**
     * Create a new {@link BinaryMemcacheClientCodec} with custom settings.
     *
     * @param decodeChunkSize       the maximum chunk size.
     * @param failOnMissingResponse report if after close there are outstanding requests.
     */
    public BinaryMemcacheClientCodec(int decodeChunkSize, bool failOnMissingResponse)
    {
        this.failOnMissingResponse = failOnMissingResponse;
        Init(new Decoder(this, decodeChunkSize), new Encoder(this));
    }

    private sealed class Encoder : BinaryMemcacheRequestEncoder
    {
        private readonly BinaryMemcacheClientCodec codec;

        internal Encoder(BinaryMemcacheClientCodec codec)
        {
            this.codec = codec;
        }

        protected override void Encode(IChannelHandlerContext ctx, object msg, IList<object> @out)
        {
            base.Encode(ctx, msg, @out);

            if (codec.failOnMissingResponse && msg is ILastMemcacheContent)
            {
                codec.requestResponseCounter.IncrementAndGet();
            }
        }
    }

    private sealed class Decoder : BinaryMemcacheResponseDecoder
    {
        private readonly BinaryMemcacheClientCodec codec;

        internal Decoder(BinaryMemcacheClientCodec codec, int chunkSize) : base(chunkSize)
        {
            this.codec = codec;
        }

        protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
        {
            int oldSize = @out.Count;
            base.Decode(ctx, @in, @out);

            if (codec.failOnMissingResponse)
            {
                int size = @out.Count;

                for (int i = oldSize; i < size; i ++)
                {
                    object msg = @out[i];
                    if (msg is ILastMemcacheContent)
                    {
                        codec.requestResponseCounter.DecrementAndGet();
                    }
                }
            }
        }

        // CLR: the count in the message uses the invariant culture, because Java's decimal text does not depend on a
        // culture while the integral ToString() formats with the current culture
        // (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            base.ChannelInactive(ctx);

            if (codec.failOnMissingResponse)
            {
                long missingResponses = codec.requestResponseCounter.Get();
                if (missingResponses > 0)
                {
                    ctx.FireExceptionCaught(new PrematureChannelClosureException(
                        "channel gone inactive with " + missingResponses.ToString(CultureInfo.InvariantCulture) +
                            " missing response(s)"));
                }
            }
        }
    }
}
