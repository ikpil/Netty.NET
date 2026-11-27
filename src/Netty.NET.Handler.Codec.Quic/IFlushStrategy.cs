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

using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * Allows to configure a strategy for when flushes should be happening.
 */
// CLR: interface FlushStrategy is IFlushStrategy; DEFAULT is a static readonly interface field and afterNumBytes/
// afterNumPackets are static interface methods returning a private implementation of the lambda. The builders take it
// (QuicCodecBuilder.FlushStrategy), so the API is ported; System.Net.Quic (msquic) sends the datagrams itself, so the
// msquic implementation accepts only DEFAULT and reports any other strategy as NotSupportedException when the codec is
// built (docs/codec-classes-quic-porting.md, Stage 2).
public interface IFlushStrategy
{

    /**
     * Default {@link FlushStrategy} implementation.
     */
    public static readonly IFlushStrategy DEFAULT = AfterNumBytes(20 * Quic.MAX_DATAGRAM_SIZE);

    /**
     * Returns {@code true} if a flush should happen now, {@code false} otherwise.
     *
     * @param numPackets    the number of packets that were written since the last flush.
     * @param numBytes      the number of bytes that were written since the last flush.
     * @return              {@code true} if a flush should be done now, {@code false} otherwise.
     */
    bool ShouldFlushNow(int numPackets, int numBytes);

    /**
     * Implementation that flushes after a number of bytes.
     *
     * @param bytes the number of bytes after which we should issue a flush.
     * @return the {@link FlushStrategy}.
     */
    public static IFlushStrategy AfterNumBytes(int bytes)
    {
        ObjectUtil.CheckPositive(bytes, "bytes");
        return new LambdaFlushStrategy((numPackets, numBytes) => numBytes > bytes);
    }

    /**
     * Implementation that flushes after a number of packets.
     *
     * @param packets the number of packets after which we should issue a flush.
     * @return the {@link FlushStrategy}.
     */
    public static IFlushStrategy AfterNumPackets(int packets)
    {
        ObjectUtil.CheckPositive(packets, "packets");
        return new LambdaFlushStrategy((numPackets, numBytes) => numPackets > packets);
    }

    // CLR-only: the lambda of afterNumBytes/afterNumPackets.
    private sealed class LambdaFlushStrategy : IFlushStrategy
    {
        private readonly System.Func<int, int, bool> shouldFlushNow;

        internal LambdaFlushStrategy(System.Func<int, int, bool> shouldFlushNow)
        {
            this.shouldFlushNow = shouldFlushNow;
        }

        public bool ShouldFlushNow(int numPackets, int numBytes) => shouldFlushNow(numPackets, numBytes);
    }
}
