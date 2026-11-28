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

namespace Netty.NET.Handler.Codec.Http2;

/**
 * HTTP/2 Push Promise Frame
 */
// CLR: stream(Http2FrameStream) is re-declared only to narrow the return type, so it is inherited from
// IHttp2StreamFrame, not repeated; the classes narrow it with a covariant override.
public interface IHttp2PushPromiseFrame : IHttp2StreamFrame
{

    /**
     * Set the Promise {@link Http2FrameStream} object for this frame.
     */
    IHttp2StreamFrame PushStream(IHttp2FrameStream stream);

    /**
     * Returns the Promise {@link Http2FrameStream} object for this frame, or {@code null} if the
     * frame has yet to be associated with a stream.
     */
    IHttp2FrameStream PushStream();

    /**
     * {@link Http2Headers} sent in Push Promise
     */
    IHttp2Headers Http2Headers();

    /**
     * Frame padding to use. Will be non-negative and less than 256.
     */
    int Padding();

    /**
     * Promised Stream ID
     */
    int PromisedStreamId();

}
