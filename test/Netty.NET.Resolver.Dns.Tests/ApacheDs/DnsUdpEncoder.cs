// CLR: test-only stand-in for org.apache.directory.server.dns.protocol.DnsUdpEncoder of apacheds-protocol-dns
// 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/protocol/DnsUdpEncoder.java
// encode is an override that subclasses can override again (TestDnsServer replaces it); the out parameter is @out.

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

/**
 * A ProtocolEncoder for use in the MINA framework that uses the
 * DnsMessageEncoder to encode DnsMessages.
 */
public class DnsUdpEncoder : ProtocolEncoderAdapter
{
    private readonly DnsMessageEncoder encoder = new DnsMessageEncoder();

    public override void Encode(IIoSession session, object message, IProtocolEncoderOutput @out)
    {
        IoBuffer buf = IoBuffer.Allocate(1024);
        encoder.Encode(buf, (DnsMessage)message);

        buf.Flip();

        @out.Write(buf);
    }
}
