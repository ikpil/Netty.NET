/*
 * Copyright 2015 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Provides an extensibility point for users to define the validity of push requests.
 * @see <a href="https://tools.ietf.org/html/rfc7540#section-8.2">[RFC 7540], Section 8.2</a>.
 */
// CLR: the interface constant ALWAYS_VERIFY is a static readonly interface field and its anonymous class a private
// nested class (as codec-http ILastHttpContent.EMPTY_LAST_CONTENT).
public interface IHttp2PromisedRequestVerifier
{
    /**
     * Determine if a {@link Http2Headers} are authoritative for a particular {@link ChannelHandlerContext}.
     * @param ctx The context on which the {@code headers} where received on.
     * @param headers The headers to be verified.
     * @return {@code true} if the {@code ctx} is authoritative for the {@code headers}, {@code false} otherwise.
     * @see
     * <a href="https://tools.ietf.org/html/rfc7540#section-10.1">[RFC 7540], Section 10.1</a>.
     */
    bool IsAuthoritative(IChannelHandlerContext ctx, IHttp2Headers headers);

    /**
     * Determine if a request is cacheable.
     * @param headers The headers for a push request.
     * @return {@code true} if the request associated with {@code headers} is known to be cacheable,
     * {@code false} otherwise.
     * @see <a href="https://tools.ietf.org/html/rfc7231#section-4.2.3">[RFC 7231], Section 4.2.3</a>.
     */
    bool IsCacheable(IHttp2Headers headers);

    /**
     * Determine if a request is safe.
     * @param headers The headers for a push request.
     * @return {@code true} if the request associated with {@code headers} is known to be safe,
     * {@code false} otherwise.
     * @see <a href="https://tools.ietf.org/html/rfc7231#section-4.2.1">[RFC 7231], Section 4.2.1</a>.
     */
    bool IsSafe(IHttp2Headers headers);

    /**
     * A default implementation of {@link Http2PromisedRequestVerifier} which always returns positive responses for
     * all verification challenges.
     */
    public static readonly IHttp2PromisedRequestVerifier ALWAYS_VERIFY = new AlwaysVerify();

    private sealed class AlwaysVerify : IHttp2PromisedRequestVerifier
    {
        public bool IsAuthoritative(IChannelHandlerContext ctx, IHttp2Headers headers)
        {
            return true;
        }

        public bool IsCacheable(IHttp2Headers headers)
        {
            return true;
        }

        public bool IsSafe(IHttp2Headers headers)
        {
            return true;
        }
    }
}
