/*
 * Copyright 2023 The Netty Project
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

using System;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * Generate
 * <a href="https://www.ietf.org/archive/id/draft-ietf-quic-transport-29.html#name-calculating-a-stateless-res">
 *     stateless reset tokens</a> to use.
 */
// CLR: java.nio.ByteBuffer is ReadOnlyMemory<byte> (see IQuicConnectionIdGenerator). msquic generates its own stateless
// reset tokens (docs/quic-design.md, No counterpart in System.Net.Quic); the interface keeps its Java API (owner
// decision 2026-11-27) for the codec builders of stage 2.
public interface IQuicResetTokenGenerator
{

    /**
     * Generate a reset token to use for the given connection id. The returned token MUST be of length 16.
     * @param cid the connection id
     * @return a newly generated reset token
     */
    ReadOnlyMemory<byte> NewResetToken(ReadOnlyMemory<byte> cid);

    /**
     * Return a {@link QuicResetTokenGenerator} which generates new reset tokens by signing the given input.
     *
     * @return a {@link QuicResetTokenGenerator} which generates new reset tokens by signing the given input.
     */
    static IQuicResetTokenGenerator SignGenerator()
    {
        return HmacSignQuicResetTokenGenerator.INSTANCE;
    }
}
