// CLR: test-only stand-in for org.apache.directory.server.protocol.shared.transport.UdpTransport of
// apacheds-protocol-shared 2.0.0.AM27, with the AbstractTransport state it inherits:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-shared/src/main/java/org/apache/directory/server/protocol/shared/transport/UdpTransport.java
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-shared/src/main/java/org/apache/directory/server/protocol/shared/transport/AbstractTransport.java
// The (String address, int port) constructor creates a NioDatagramAcceptor whose default local address is
// new InetSocketAddress(address, port): the host is looked up with Resolver's DefaultNameResolver
// (InetAddress.getByName, which parses a literal with the JDK rules and looks a name up), keeping an unresolved
// DnsEndPoint when the lookup fails, as InetSocketAddress flags the address unresolved
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/InetSocketAddress.html); a null address is
// the wildcard address. getAcceptor() creates a new acceptor when the current one is disposed, as the source does, so
// TestDnsServer.localAddress() is null after stop(). The covariant DatagramAcceptor getAcceptor() is the public
// IDatagramAcceptor GetAcceptor() with an explicit ITransport.GetAcceptor(). The no-argument and (int port)
// constructors (port 53 on the loopback address for DnsServer.start()) and the thread, backlog and SSL settings of
// AbstractTransport have no caller and are not reproduced. toString keeps the AbstractTransport form.

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Netty.NET.Common.Concurrent;
using Netty.NET.Resolver;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

/**
 * The Transport instance for UDP based protocols.
 */
public class UdpTransport : ITransport
{
    private static readonly DefaultNameResolver NAME_RESOLVER = new DefaultNameResolver(ImmediateEventExecutor.INSTANCE);

    /** The server address */
    private string address;

    /** The service's port */
    private int port = -1;

    /** The IoAcceptor used to accept requests */
    protected IIoAcceptor acceptor;

    /**
     * Creates an instance of the UdpTransport class
     * @param address The address
     * @param udpPort The port
     */
    public UdpTransport(string address, int udpPort)
    {
        this.address = address;
        this.port = udpPort;
        this.acceptor = CreateAcceptor(address, udpPort);
    }

    /**
     * Initialize the Acceptor if needed
     */
    public void Init()
    {
        acceptor = CreateAcceptor(GetAddress(), GetPort());
    }

    public string GetAddress()
    {
        return address;
    }

    public int GetPort()
    {
        return port;
    }

    /**
     * @return The associated DatagramAcceptor
     */
    public IDatagramAcceptor GetAcceptor()
    {
        if ((acceptor != null) && acceptor.IsDisposed())
        {
            acceptor = CreateAcceptor(GetAddress(), GetPort());
        }

        return acceptor == null ? null : (IDatagramAcceptor)acceptor;
    }

    IIoAcceptor ITransport.GetAcceptor()
    {
        return GetAcceptor();
    }

    /**
     * Helper method to create an IoAcceptor
     */
    private IIoAcceptor CreateAcceptor(string address, int port)
    {
        NioDatagramAcceptor acceptor = new NioDatagramAcceptor();

        EndPoint socketAddress = null;

        // The address can be null here, if one want to connect using the wildcard address
        if (address == null)
        {
            // Create a socket listening on the wildcard address
            socketAddress = new IPEndPoint(IPAddress.Any, port);
        }
        else
        {
            socketAddress = NewInetSocketAddress(address, port);
        }

        acceptor.SetDefaultLocalAddress(socketAddress);

        return acceptor;
    }

    // CLR: new InetSocketAddress(String hostname, int port) (see the header).
    private static EndPoint NewInetSocketAddress(string hostname, int port)
    {
        try
        {
            return new IPEndPoint(NAME_RESOLVER.ResolveAsync(hostname).GetAwaiter().GetResult(), port);
        }
        catch (SocketException)
        {
            return new DnsEndPoint(hostname, port);
        }
    }

    public override string ToString()
    {
        return "UdpTransport" + "[<" + address + ':' + port.ToString(CultureInfo.InvariantCulture) +
               ">], backlog=0, nbThreads = 0]";
    }
}
