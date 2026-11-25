// CLR: test-only stand-in for org.apache.directory.server.protocol.shared.transport.Transport of apacheds-protocol-shared
// 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-shared/src/main/java/org/apache/directory/server/protocol/shared/transport/Transport.java
// The interface takes the I prefix and keeps the members DnsServer and TestDnsServer use (init, getAddress, getPort,
// getAcceptor); the setters, thread, backlog and SSL settings configure the TCP transport and the LDAP-backed service,
// which the stand-in does not have.

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

public interface ITransport
{
    /**
     * Initialize the Acceptor if needed
     */
    void Init();

    /**
     * @return The associated Address
     */
    string GetAddress();

    /**
     * Gets the port for this service.
     *
     * @return the port for this service
     */
    int GetPort();

    /**
     * @return The associated IoAcceptor
     */
    IIoAcceptor GetAcceptor();
}
