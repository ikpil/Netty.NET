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
 * HTTP/2 Priority Frame
 */
// CLR: stream(Http2FrameStream) is re-declared only to narrow the return type, so it is inherited from
// IHttp2StreamFrame, not repeated; the classes narrow it with a covariant override (owner decision 2026-11-19;
// docs/transport-porting.md, Decisions).
public interface IHttp2PriorityFrame : IHttp2StreamFrame
{

    /**
     * Parent Stream Id of this Priority request
     */
    int StreamDependency();

    /**
     * Stream weight
     */
    short Weight();

    /**
     * Set to {@code true} if this stream is exclusive else set to {@code false}
     */
    bool Exclusive();

}
