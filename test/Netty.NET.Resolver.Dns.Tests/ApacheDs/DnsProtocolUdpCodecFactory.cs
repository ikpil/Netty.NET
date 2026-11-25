// CLR: test-only stand-in for org.apache.directory.server.dns.protocol.DnsProtocolUdpCodecFactory of
// apacheds-protocol-dns 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/protocol/DnsProtocolUdpCodecFactory.java
// The singleton codec that DnsProtocolHandler.sessionCreated installs for a datagram session (TestDnsServer replaces
// that method with its own codec).

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

public sealed class DnsProtocolUdpCodecFactory : IProtocolCodecFactory
{
    private static readonly DnsProtocolUdpCodecFactory INSTANCE = new DnsProtocolUdpCodecFactory();

    /**
     * Returns the singleton instance of {@link DnsProtocolUdpCodecFactory}.
     *
     * @return The singleton instance of {@link DnsProtocolUdpCodecFactory}.
     */
    public static DnsProtocolUdpCodecFactory GetInstance()
    {
        return INSTANCE;
    }

    private DnsProtocolUdpCodecFactory()
    {
        // Private constructor prevents instantiation outside this class.
    }

    public IProtocolEncoder GetEncoder(IIoSession session)
    {
        // Create a new encoder.
        return new DnsUdpEncoder();
    }

    public IProtocolDecoder GetDecoder(IIoSession session)
    {
        // Create a new decoder.
        return new DnsUdpDecoder();
    }
}
