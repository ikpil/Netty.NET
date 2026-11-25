// CLR: test-only stand-in for org.apache.directory.server.dns.protocol.DnsUdpDecoder of apacheds-protocol-dns
// 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/protocol/DnsUdpDecoder.java
// The in/out parameters are @in/@out; "throws IOException" is only documented.

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

/**
 * A ProtocolDecoder for use in the MINA framework that uses the
 * DnsMessageDecoder to decode DnsMessages.
 */
public class DnsUdpDecoder : ProtocolDecoderAdapter
{
    private readonly DnsMessageDecoder decoder = new DnsMessageDecoder();

    public override void Decode(IIoSession session, IoBuffer @in, IProtocolDecoderOutput @out)
    {
        @out.Write(decoder.Decode(@in));
    }
}
