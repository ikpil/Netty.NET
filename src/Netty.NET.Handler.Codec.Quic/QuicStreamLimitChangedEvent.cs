/*
 * Copyright 2021 The Netty Project
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

namespace Netty.NET.Handler.Codec.Quic;

/**
 * Event fired once the stream limit of a {@link QuicChannel} changes.
 */
// CLR: the package-private INSTANCE is internal. System.Net.Quic reports the peer's stream limit increments through
// QuicConnectionOptions.StreamCapacityCallback (docs/quic-design.md, Mapping), from which the msquic implementation
// (stage 2) fires this event.
public sealed class QuicStreamLimitChangedEvent : IQuicEvent
{

    internal static readonly QuicStreamLimitChangedEvent INSTANCE = new QuicStreamLimitChangedEvent();

    private QuicStreamLimitChangedEvent()
    {
    }
}
