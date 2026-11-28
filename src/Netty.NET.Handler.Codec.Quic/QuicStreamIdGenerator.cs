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

namespace Netty.NET.Handler.Codec.Quic;

/**
 * Generates and hands over the next stream id to use for a QUIC stream.
 */
// CLR: The second local is stream2: C# does not let a local of the method body reuse the name of a local in a
// nested block (CS0136). msquic assigns the stream IDs of System.Net.Quic streams itself (QuicStream.Id, RFC 9000
// 2.1; docs/quic-design.md, probe), so the msquic implementation (stage 2) reads them from the stream; the
// generator is ported as is for code that needs the RFC 9000 numbering.
internal sealed class QuicStreamIdGenerator
{
    private long nextBidirectionalStreamId;
    private long nextUnidirectionalStreamId;

    internal QuicStreamIdGenerator(bool server)
    {
        // See https://quicwg.org/base-drafts/rfc9000.html#name-stream-types-and-identifier
        nextBidirectionalStreamId = server ? 1 : 0;
        nextUnidirectionalStreamId = server ? 3 : 2;
    }

    internal long NextStreamId(bool bidirectional)
    {
        if (bidirectional)
        {
            long stream = nextBidirectionalStreamId;
            nextBidirectionalStreamId += 4;
            return stream;
        }

        long stream2 = nextUnidirectionalStreamId;
        nextUnidirectionalStreamId += 4;
        return stream2;
    }
}
