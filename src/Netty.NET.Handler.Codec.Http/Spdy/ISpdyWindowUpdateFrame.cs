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
 * A SPDY Protocol WINDOW_UPDATE Frame
 */
// CLR: the Java interface takes the I prefix (owner rule).
public interface ISpdyWindowUpdateFrame : ISpdyFrame
{

    /**
     * Returns the Stream-ID of this frame.
     */
    int StreamId();

    /**
     * Sets the Stream-ID of this frame.  The Stream-ID cannot be negative.
     */
    ISpdyWindowUpdateFrame SetStreamId(int streamID);

    /**
     * Returns the Delta-Window-Size of this frame.
     */
    int DeltaWindowSize();

    /**
     * Sets the Delta-Window-Size of this frame.
     * The Delta-Window-Size must be positive.
     */
    ISpdyWindowUpdateFrame SetDeltaWindowSize(int deltaWindowSize);
}
