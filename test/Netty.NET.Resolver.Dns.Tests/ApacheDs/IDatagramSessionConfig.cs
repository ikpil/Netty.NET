// CLR: test-only stand-in for org.apache.mina.transport.socket.DatagramSessionConfig of Apache MINA 2.2.3:
// https://github.com/apache/mina/blob/2.2.3/mina-core/src/main/java/org/apache/mina/transport/socket/DatagramSessionConfig.java
// The interface takes the I prefix and keeps SO_REUSEADDR, the only option DnsServer and TestDnsServer set
// (SocketOptionName.ReuseAddress, "Allows the socket to be bound to an address that is already in use",
// https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketoptionname?view=net-10.0), and the read buffer
// size the acceptor allocates per datagram (IoSessionConfig; AbstractIoSessionConfig.readBufferSize is 2048 by
// default).

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

public interface IDatagramSessionConfig
{
    /**
     * @return <tt>true</tt> if SO_REUSEADDR is enabled.
     */
    bool IsReuseAddress();

    /**
     * @param reuseAddress Tells if SO_REUSEADDR is enabled or disabled
     */
    void SetReuseAddress(bool reuseAddress);

    /**
     * @return the size of the read buffer that I/O processor allocates
     * per each read.
     */
    int GetReadBufferSize();

    /**
     * Sets the size of the read buffer that I/O processor allocates
     * per each read.
     *
     * @param readBufferSize The size of the read buffer
     */
    void SetReadBufferSize(int readBufferSize);
}
