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

using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * {@link DuplexChannelConfig} for QUIC streams.
 */
// CLR: DuplexChannelConfig is IDuplexChannelConfig. setAllowHalfClosure, setMaxMessagesPerRead, setWriteSpinCount,
// setAllocator, setRecvByteBufAllocator, setAutoRead, setAutoClose, setMessageSizeEstimator, setWriteBufferWaterMark,
// setConnectTimeoutMillis, setWriteBufferHighWaterMark and setWriteBufferLowWaterMark are re-declared only to narrow the
// return type to QuicStreamChannelConfig, so they are inherited from IDuplexChannelConfig/IChannelConfig unchanged
// (docs/transport-porting.md, Decisions; the IDuplexChannelConfig rule).
public interface IQuicStreamChannelConfig : IDuplexChannelConfig
{
    /**
     * Set this to {@code true} if the {@link QuicStreamChannel} should read {@link QuicStreamFrame}s and fire these
     * through the {@link io.netty.channel.ChannelPipeline}, {@code false} if it uses {@link io.netty.buffer.ByteBuf}.
     *
     * @param readFrames    {@code true} if {@link QuicStreamFrame}s should be used, {@code false} if
     *                      {@link io.netty.buffer.ByteBuf} should be used.
     * @return              this instance itself.
     *
     */
    IQuicStreamChannelConfig SetReadFrames(bool readFrames);

    /**
     * Returns {@code true} if the {@link QuicStreamChannel} will read {@link QuicStreamFrame}s and fire these through
     * the {@link io.netty.channel.ChannelPipeline}, {@code false} if it uses {@link io.netty.buffer.ByteBuf}.
     *
     * @return  {@code true} if {@link QuicStreamFrame}s should be used, {@code false} if
     *          {@link io.netty.buffer.ByteBuf} should be used.
     */
    bool IsReadFrames();
}
