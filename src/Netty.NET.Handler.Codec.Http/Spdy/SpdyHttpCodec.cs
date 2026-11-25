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
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Spdy;

/**
 * A combination of {@link SpdyHttpDecoder} and {@link SpdyHttpEncoder}
 */
// CLR: CombinedChannelDuplexHandler is the Transport CombinedChannelDuplexHandler<TInbound, TOutbound>;
// HttpHeadersFactory is IHttpHeadersFactory and HashMap<Integer, FullHttpMessage> is
// Dictionary<int, IFullHttpMessage>; the protected SpdyHttpDecoder constructors are protected internal (Java protected
// also admits the package). The @Deprecated constructor (version, maxContentLength, validateHttpHeaders) has no
// @deprecated text, so it is ported with [Obsolete] (owner decision 2026-11-16); it calls the deprecated
// SpdyHttpDecoder(SpdyVersion, int, boolean), which names its replacement and is not ported, so it passes the body of
// that constructor chain through the replacement: the headers and trailers factories with withValidation(validate).
public sealed class SpdyHttpCodec : CombinedChannelDuplexHandler<SpdyHttpDecoder, SpdyHttpEncoder>
{
    /**
     * Creates a new instance with the specified decoder options.
     */
    public SpdyHttpCodec(SpdyVersion version, int maxContentLength)
        : base(new SpdyHttpDecoder(version, maxContentLength), new SpdyHttpEncoder(version))
    {
    }

    /**
     * Creates a new instance with the specified decoder options.
     */
    [Obsolete]
    public SpdyHttpCodec(SpdyVersion version, int maxContentLength, bool validateHttpHeaders)
        : base(new SpdyHttpDecoder(version, maxContentLength, new Dictionary<int, IFullHttpMessage>(),
                DefaultHttpHeadersFactory.HeadersFactory().WithValidation(validateHttpHeaders),
                DefaultHttpHeadersFactory.TrailersFactory().WithValidation(validateHttpHeaders)),
               new SpdyHttpEncoder(version))
    {
    }

    /**
     * Creates a new instance with the specified decoder options.
     */
    public SpdyHttpCodec(SpdyVersion version, int maxContentLength,
                         IHttpHeadersFactory headersFactory, IHttpHeadersFactory trailersFactory)
        : base(new SpdyHttpDecoder(version, maxContentLength, new Dictionary<int, IFullHttpMessage>(),
                headersFactory, trailersFactory), new SpdyHttpEncoder(version))
    {
    }

    /**
     * Creates a new instance with the specified decoder options.
     */
    public SpdyHttpCodec(SpdyVersion version, int maxHeaderSize, int maxContentLength,
                         IHttpHeadersFactory headersFactory, IHttpHeadersFactory trailersFactory)
        : base(new SpdyHttpDecoder(version, maxHeaderSize, maxContentLength, new Dictionary<int, IFullHttpMessage>(),
                headersFactory, trailersFactory), new SpdyHttpEncoder(version))
    {
    }
}
