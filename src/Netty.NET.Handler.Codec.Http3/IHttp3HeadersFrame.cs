/*
 * Copyright 2020 The Netty Project
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

namespace Netty.NET.Handler.Codec.Http3;

/**
 * See <a href="https://tools.ietf.org/html/draft-ietf-quic-http-32#section-7.2.2">HEADERS</a>.
 */
// CLR: the interface takes the I prefix (docs/codec-http2-porting.md, Shared CLR adaptations). The default method type()
// that overrides Http3Frame.type() is an explicit default interface member of IHttp3Frame.Type(), so it is reached
// through an interface reference.
public interface IHttp3HeadersFrame : IHttp3RequestStreamFrame, IHttp3PushStreamFrame
{

    long IHttp3Frame.Type()
    {
        return Http3CodecUtils.HTTP3_HEADERS_FRAME_TYPE;
    }

    /**
     * Returns the carried headers.
     *
     * @return the carried headers.
     */
    IHttp3Headers Headers();
}
