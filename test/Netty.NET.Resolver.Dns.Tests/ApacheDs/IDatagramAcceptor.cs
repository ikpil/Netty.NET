// CLR: test-only stand-in for org.apache.mina.transport.socket.DatagramAcceptor of Apache MINA 2.2.3:
// https://github.com/apache/mina/blob/2.2.3/mina-core/src/main/java/org/apache/mina/transport/socket/DatagramAcceptor.java
// The interface takes the I prefix. The covariant InetSocketAddress getLocalAddress() hides the IIoAcceptor member
// (new). setDefaultLocalAddress(InetSocketAddress) takes an EndPoint because InetSocketAddress is IPEndPoint when
// resolved and DnsEndPoint when unresolved; binding an unresolved address fails with IOException, as
// DatagramSocket.bind does under MINA.

using System.Net;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

public interface IDatagramAcceptor : IIoAcceptor
{
    /**
     * Returns the local InetSocketAddress which is bound currently.
     * This method overrides the {@link IoAcceptor#getLocalAddress()} method.
     */
    new IPEndPoint GetLocalAddress();

    /**
     * Sets the default local InetSocketAddress to bind when no argument is specified in
     * {@link #bind()} method.
     *
     * @param localAddress The local address
     */
    void SetDefaultLocalAddress(EndPoint localAddress);

    /**
     * @return the default Datagram configuration of the new {@link IoSession}s
     * created by this service.
     */
    IDatagramSessionConfig GetSessionConfig();
}
