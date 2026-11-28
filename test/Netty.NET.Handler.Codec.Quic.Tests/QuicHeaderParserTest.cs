/*
 * Copyright 2026 The Netty Project
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

using System.Net;
using Netty.NET.Buffer;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: InetSocketAddress is IPEndPoint. The lambda QuicHeaderProcessor is the private FailingProcessor class,
// whose Process calls Assert.Fail as JUnit's fail(...) does. try-with-resources over the AutoCloseable parser is a
// using statement (QuicHeaderParser is IDisposable, Dispose() calls Close()), and try/finally releases the packet
// as in Java. assertThrows(QuicException.class,...) is Assert.ThrowsAny<QuicException>; the throws clauses have no
// counterpart. writeByte/writeInt are WriteByte/WriteInt and buffer(5, 5) is Unpooled.Buffer(5, 5).
public class QuicHeaderParserTest
{

    private static readonly IPEndPoint SENDER = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 9999);
    private static readonly IPEndPoint RECIPIENT = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 8888);

    private static readonly QuicHeaderParser.IQuicHeaderProcessor FAILING_PROCESSOR = new FailingProcessor();

    private sealed class FailingProcessor : QuicHeaderParser.IQuicHeaderProcessor
    {
        public void Process(IPEndPoint sender, IPEndPoint recipient, ByteBuf packet, QuicPacketType type,
                            long version, ByteBuf scid, ByteBuf dcid, ByteBuf token)
        {
            Assert.Fail("Should not be able to parse the header out of a truncated packet");
        }
    }

    // Long header packet with header form (1) + fixed bit (1) + INITIAL packet type (00), version 1, but the
    // buffer stops right before the "Destination Connection ID Length" byte is present.
    //
    // The buffer capacity exactly matches what is written (no spare capacity), so that a read which is not
    // correctly bounds-checked reads past the backing array and fails with a raw IndexOutOfBoundsException
    // rather than the expected QuicException.
    private static ByteBuf TruncatedBeforeDestinationConnectionIdLength()
    {
        ByteBuf buffer = Unpooled.Buffer(5, 5);
        buffer.WriteByte(0x80); // header form = 1, fixed bit = 0, type-specific bits = 0 (INITIAL)
        buffer.WriteInt(1); // version
        return buffer;
    }

    // Same as above but includes a (zero-length) destination connection id, and stops right before the
    // "Source Connection ID Length" byte is present.
    private static ByteBuf TruncatedBeforeSourceConnectionIdLength()
    {
        ByteBuf buffer = Unpooled.Buffer(6, 6);
        buffer.WriteByte(0x80);
        buffer.WriteInt(1);
        buffer.WriteByte(0); // destination connection id length == 0
        return buffer;
    }

    [Fact]
    public void TestThrowsOnTruncatedDestinationConnectionIdLength()
    {
        ByteBuf packet = TruncatedBeforeDestinationConnectionIdLength();

        try
        {
            using (QuicHeaderParser parser = new QuicHeaderParser(20))
            {
                Assert.ThrowsAny<QuicException>(() =>
                        parser.Parse(SENDER, RECIPIENT, packet, FAILING_PROCESSOR));
            }
        }
        finally
        {
            packet.Release();
        }
    }

    [Fact]
    public void TestThrowsOnTruncatedSourceConnectionIdLength()
    {
        ByteBuf packet = TruncatedBeforeSourceConnectionIdLength();

        try
        {
            using (QuicHeaderParser parser = new QuicHeaderParser(20))
            {
                Assert.ThrowsAny<QuicException>(() =>
                        parser.Parse(SENDER, RECIPIENT, packet, FAILING_PROCESSOR));
            }
        }
        finally
        {
            packet.Release();
        }
    }
}
