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
 * A QUIC {@link ChannelConfig}.
 */
// CLR: every member of the Java interface (setMaxMessagesPerRead, setConnectTimeoutMillis, setWriteSpinCount,
// setAllocator, setRecvByteBufAllocator, setAutoRead, setAutoClose, setWriteBufferHighWaterMark,
// setWriteBufferLowWaterMark, setWriteBufferWaterMark, setMessageSizeEstimator) is re-declared only to narrow the
// return type to QuicChannelConfig, so they are inherited from IChannelConfig unchanged: a C# interface cannot
// narrow an inherited member's return type without hiding it. The @Deprecated setMaxMessagesPerRead keeps its
// [Obsolete] on IChannelConfig.
public interface IQuicChannelConfig : IChannelConfig
{
}
