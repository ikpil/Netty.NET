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

using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * {@link ChannelOption}s specific to QUIC.
 */
// CLR: the Java class is a generic subclass of ChannelOption<T> whose only members are a private constructor and the
// constants, so nothing can create or extend it. A static field of a generic C# class needs a type argument at every
// use, so the constants live on a static class, as codec-http2 Http2StreamChannelOption keeps its constant;
// valueOf(QuicChannelOption.class, name) is ChannelOption.ValueOf<T>(typeof(QuicChannelOption), name), which names the
// option "<type>#<name>" as Java does. Boolean is bool. The private constructor (super(null), @SuppressWarnings) has no
// counterpart on a static class.
public static class QuicChannelOption
{

    /**
     * If set to {@code true} the {@link QuicStreamChannel} will read {@link QuicStreamFrame}s and fire it through
     * the pipeline, if {@code false} it will read {@link io.netty.buffer.ByteBuf} and translate the FIN flag to
     * events.
     */
    public static readonly ChannelOption<bool> READ_FRAMES =
            ChannelOption.ValueOf<bool>(typeof(QuicChannelOption), "READ_FRAMES");

    /**
     * Enable <a href="https://quiclog.github.io/internet-drafts/draft-marx-qlog-main-schema.html">qlog</a>
     * for a {@link QuicChannel}.
     */
    public static readonly ChannelOption<QLogConfiguration> QLOG =
            ChannelOption.ValueOf<QLogConfiguration>(typeof(QuicChannelOption), "QLOG");

    /**
     * Use <a href="https://blog.cloudflare.com/accelerating-udp-packet-transmission-for-quic/">GSO</a>
     * for QUIC packets if possible.
     */
    // CLR: SEGMENTED_DATAGRAM_PACKET_ALLOCATOR is a ChannelOption<SegmentedDatagramPacketAllocator>; that type belongs to
    // the datagram writes of the quiche codec, which msquic performs itself, so SegmentedDatagramPacketAllocator is
    // recorded jvm-only-replaced in stage 2 and the option has no counterpart (docs/codec-classes-quic-porting.md,
    // Stage 2).
}
