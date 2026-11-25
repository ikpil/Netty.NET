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
 * A SPDY Protocol SYN_STREAM Frame
 */
// CLR: the Java interface takes the I prefix (owner rule). setStreamId, setLast and setInvalid are re-declared only to
// narrow the return type, so they are inherited from ISpdyStreamFrame and ISpdyHeadersFrame; DefaultSpdySynStreamFrame
// narrows them with covariant override returns (owner decision 2026-11-19; docs/transport-porting.md, Decisions). The
// Java byte priority is the CLR byte (an octet, 0-7 when valid), as the other codec ports map Java byte members.
public interface ISpdySynStreamFrame : ISpdyHeadersFrame
{

    /**
     * Returns the Associated-To-Stream-ID of this frame.
     */
    int AssociatedStreamId();

    /**
     * Sets the Associated-To-Stream-ID of this frame.
     * The Associated-To-Stream-ID cannot be negative.
     */
    ISpdySynStreamFrame SetAssociatedStreamId(int associatedStreamId);

    /**
     * Returns the priority of the stream.
     */
    byte Priority();

    /**
     * Sets the priority of the stream.
     * The priority must be between 0 and 7 inclusive.
     */
    ISpdySynStreamFrame SetPriority(byte priority);

    /**
     * Returns {@code true} if the stream created with this frame is to be
     * considered half-closed to the receiver.
     */
    bool IsUnidirectional();

    /**
     * Sets if the stream created with this frame is to be considered
     * half-closed to the receiver.
     */
    ISpdySynStreamFrame SetUnidirectional(bool unidirectional);
}
