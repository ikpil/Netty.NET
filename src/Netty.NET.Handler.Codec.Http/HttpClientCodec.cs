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

using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http.HttpObjectDecoder;

namespace Netty.NET.Handler.Codec.Http;

/**
 * A combination of {@link HttpRequestEncoder} and {@link HttpResponseDecoder}
 * which enables easier client side HTTP implementation. {@link HttpClientCodec}
 * provides additional state management for <tt>HEAD</tt> and <tt>CONNECT</tt>
 * requests, which {@link HttpResponseDecoder} lacks.  Please refer to
 * {@link HttpResponseDecoder} to learn what additional state management needs
 * to be done for <tt>HEAD</tt> and <tt>CONNECT</tt> and why
 * {@link HttpResponseDecoder} can not handle it by itself.
 * <p>
 * If the {@link Channel} is closed and there are missing responses,
 * a {@link PrematureChannelClosureException} is thrown.
 *
 * <h3>Header Validation</h3>
 *
 * It is recommended to always enable header validation.
 * <p>
 * Without header validation, your system can become vulnerable to
 * <a href="https://cwe.mitre.org/data/definitions/113.html">
 *     CWE-113: Improper Neutralization of CRLF Sequences in HTTP Headers ('HTTP Response Splitting')
 * </a>.
 * <p>
 * This recommendation stands even when both peers in the HTTP exchange are trusted,
 * as it helps with defence-in-depth.
 *
 * @see HttpServerCodec
 */
// CLR: CombinedChannelDuplexHandler's protected init(...) is Init(...) and inboundHandler()/outboundHandler() are the
// InboundHandler/OutboundHandler properties of the Transport port (docs/transport-porting.md). The inner classes Encoder
// and Decoder read the codec's fields through the codec they receive. Queue<HttpMethod> (ArrayDeque) is Queue<HttpMethod>
// (offer is Enqueue, poll is TryDequeue with null for an empty queue), AtomicLong is a long updated with
// Interlocked/Volatile (https://learn.microsoft.com/en-us/dotnet/api/system.threading.interlocked?view=net-10.0). The seven
// @Deprecated constructors whose @deprecated texts name HttpClientCodec(int, int, int, boolean) or
// HttpClientCodec(HttpDecoderConfig, boolean, boolean) are not ported (owner decision 2026-11-16). The missing response
// count prints with the invariant culture.
public sealed class HttpClientCodec : CombinedChannelDuplexHandler<HttpResponseDecoder, HttpRequestEncoder>,
        HttpClientUpgradeHandler.ISourceCodec
{
    public const bool DEFAULT_FAIL_ON_MISSING_RESPONSE = false;
    public const bool DEFAULT_PARSE_HTTP_AFTER_CONNECT_REQUEST = false;

    /** A queue that is used for correlating a request and a response. */
    private readonly Queue<HttpMethod> queue = new Queue<HttpMethod>();
    private readonly bool parseHttpAfterConnectRequest;

    /** If true, decoding stops (i.e. pass-through) */
    private bool done;

    private long requestResponseCounter;
    private readonly bool failOnMissingResponse;

    /**
     * Creates a new instance with the default decoder options
     * ({@code maxInitialLineLength (4096)}, {@code maxHeaderSize (8192)}, and
     * {@code maxChunkSize (8192)}).
     */
    public HttpClientCodec()
        : this(new HttpDecoderConfig(),
                DEFAULT_PARSE_HTTP_AFTER_CONNECT_REQUEST,
                DEFAULT_FAIL_ON_MISSING_RESPONSE)
    {
    }

    /**
     * Creates a new instance with the specified decoder options.
     */
    public HttpClientCodec(int maxInitialLineLength, int maxHeaderSize, int maxChunkSize)
        : this(new HttpDecoderConfig()
                        .SetMaxInitialLineLength(maxInitialLineLength)
                        .SetMaxHeaderSize(maxHeaderSize)
                        .SetMaxChunkSize(maxChunkSize),
                DEFAULT_PARSE_HTTP_AFTER_CONNECT_REQUEST,
                DEFAULT_FAIL_ON_MISSING_RESPONSE)
    {
    }

    /**
     * Creates a new instance with the specified decoder options.
     */
    public HttpClientCodec(
            int maxInitialLineLength, int maxHeaderSize, int maxChunkSize, bool failOnMissingResponse)
        : this(new HttpDecoderConfig()
                        .SetMaxInitialLineLength(maxInitialLineLength)
                        .SetMaxHeaderSize(maxHeaderSize)
                        .SetMaxChunkSize(maxChunkSize),
                DEFAULT_PARSE_HTTP_AFTER_CONNECT_REQUEST,
                failOnMissingResponse)
    {
    }

    /**
     * Creates a new instance with the specified decoder options.
     */
    public HttpClientCodec(
            HttpDecoderConfig config, bool parseHttpAfterConnectRequest, bool failOnMissingResponse)
    {
        Init(new Decoder(this, config), new Encoder(this));
        this.parseHttpAfterConnectRequest = parseHttpAfterConnectRequest;
        this.failOnMissingResponse = failOnMissingResponse;
    }

    /**
     * Prepares to upgrade to another protocol from HTTP. Disables the {@link Encoder}.
     */
    public void PrepareUpgradeFrom(IChannelHandlerContext ctx)
    {
        ((Encoder) OutboundHandler).upgraded = true;
    }

    /**
     * Upgrades to another protocol from HTTP. Removes the {@link Decoder} and {@link Encoder} from
     * the pipeline.
     */
    public void UpgradeFrom(IChannelHandlerContext ctx)
    {
        IChannelPipeline p = ctx.Pipeline;
        p.Remove(this);
    }

    public void SetSingleDecode(bool singleDecode)
    {
        InboundHandler.SetSingleDecode(singleDecode);
    }

    public bool IsSingleDecode()
    {
        return InboundHandler.IsSingleDecode();
    }

    private sealed class Encoder : HttpRequestEncoder
    {
        private readonly HttpClientCodec codec;

        internal bool upgraded;

        internal Encoder(HttpClientCodec codec)
        {
            this.codec = codec;
        }

        protected override void Encode(
                IChannelHandlerContext ctx, object msg, IList<object> @out)
        {

            if (upgraded)
            {
                // HttpObjectEncoder overrides .write and does not release msg, so we don't need to retain it here
                @out.Add(msg);
                return;
            }

            if (msg is IHttpRequest)
            {
                codec.queue.Enqueue(((IHttpRequest) msg).Method());
            }

            base.Encode(ctx, msg, @out);

            if (codec.failOnMissingResponse && !codec.done)
            {
                // check if the request is chunked if so do not increment
                if (msg is ILastHttpContent)
                {
                    // increment as its the last chunk
                    Interlocked.Increment(ref codec.requestResponseCounter);
                }
            }
        }
    }

    private sealed class Decoder : HttpResponseDecoder
    {
        private readonly HttpClientCodec codec;

        internal Decoder(HttpClientCodec codec, HttpDecoderConfig config) : base(config)
        {
            this.codec = codec;
        }

        protected override void Decode(
                IChannelHandlerContext ctx, ByteBuf buffer, IList<object> @out)
        {
            if (codec.done)
            {
                int readable = ActualReadableBytes();
                if (readable == 0)
                {
                    // if non is readable just return null
                    // https://github.com/netty/netty/issues/1159
                    return;
                }
                @out.Add(buffer.ReadBytes(readable));
            }
            else
            {
                int oldSize = @out.Count;
                base.Decode(ctx, buffer, @out);
                if (codec.failOnMissingResponse)
                {
                    int size = @out.Count;
                    for (int i = oldSize; i < size; i++)
                    {
                        Decrement(@out[i]);
                    }
                }
            }
        }

        private void Decrement(object msg)
        {
            if (msg == null)
            {
                return;
            }

            // check if it's an Header and its transfer encoding is not chunked.
            if (msg is ILastHttpContent)
            {
                Interlocked.Decrement(ref codec.requestResponseCounter);
            }
        }

        protected override bool IsContentAlwaysEmpty(IHttpMessage msg)
        {
            HttpResponseStatus status = ((IHttpResponse) msg).Status();
            HttpStatusClass statusClass = status.CodeClass();
            int statusCode = status.Code();
            if (statusClass == HttpStatusClass.INFORMATIONAL)
            {
                // An informational response should be excluded from paired comparison.
                // Just delegate to super method which has all the needed handling.
                return base.IsContentAlwaysEmpty(msg);
            }

            // Get the method of the HTTP request that corresponds to the
            // current response.
            codec.queue.TryDequeue(out HttpMethod method);

            // If the remote peer did for example send multiple responses for one request (which is not allowed per
            // spec but may still be possible) method will be null so guard against it.
            if (method != null)
            {
                char firstChar = method.Name()[0];
                switch (firstChar)
                {
                    case 'H':
                        // According to 4.3, RFC2616:
                        // All responses to the HEAD request method MUST NOT include a
                        // message-body, even though the presence of entity-header fields
                        // might lead one to believe they do.
                        if (HttpMethod.HEAD.Equals(method))
                        {
                            return true;

                            // The following code was inserted to work around the servers
                            // that behave incorrectly.  It has been commented out
                            // because it does not work with well behaving servers.
                            // Please note, even if the 'Transfer-Encoding: chunked'
                            // header exists in the HEAD response, the response should
                            // have absolutely no content.
                            //
                            //// Interesting edge case:
                            //// Some poorly implemented servers will send a zero-byte
                            //// chunk if Transfer-Encoding of the response is 'chunked'.
                            ////
                            //// return !msg.isChunked();
                        }
                        break;
                    case 'C':
                        // Successful CONNECT request results in a response with empty body.
                        if (statusCode == 200)
                        {
                            if (HttpMethod.CONNECT.Equals(method))
                            {
                                // Proxy connection established - Parse HTTP only if configured by
                                // parseHttpAfterConnectRequest, else pass through.
                                if (!codec.parseHttpAfterConnectRequest)
                                {
                                    codec.done = true;
                                    codec.queue.Clear();
                                }
                                return true;
                            }
                        }
                        break;
                    default:
                        break;
                }
            }
            return base.IsContentAlwaysEmpty(msg);
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            base.ChannelInactive(ctx);

            if (codec.failOnMissingResponse)
            {
                long missingResponses = Volatile.Read(ref codec.requestResponseCounter);
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
