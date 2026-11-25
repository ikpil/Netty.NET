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

namespace Netty.NET.Handler.Codec.Spdy;

/**
 * A SPDY Protocol HEADERS Frame
 */
// CLR: the Java interface takes the I prefix (owner rule). setStreamId and setLast are re-declared only to narrow the
// return type, so they are inherited from ISpdyStreamFrame; the classes narrow them with covariant override returns
// (owner decision 2026-11-19; docs/transport-porting.md, Decisions).
public interface ISpdyHeadersFrame : ISpdyStreamFrame
{

    /**
     * Returns {@code true} if this header block is invalid.
     * A RST_STREAM frame with code PROTOCOL_ERROR should be sent.
     */
    bool IsInvalid();

    /**
     * Marks this header block as invalid.
     */
    ISpdyHeadersFrame SetInvalid();

    /**
     * Returns {@code true} if this header block has been truncated due to
     * length restrictions.
     */
    bool IsTruncated();

    /**
     * Mark this header block as truncated.
     */
    ISpdyHeadersFrame SetTruncated();

    /**
     * Returns the {@link SpdyHeaders}.
     */
    ISpdyHeaders Headers();
}
