// CLR: test-only stand-in for org.apache.mina.core.service.IoAcceptor, with the IoService members it uses, of
// Apache MINA 2.2.3:
// https://github.com/apache/mina/blob/2.2.3/mina-core/src/main/java/org/apache/mina/core/service/IoAcceptor.java
// https://github.com/apache/mina/blob/2.2.3/mina-core/src/main/java/org/apache/mina/core/service/IoService.java
// The interface takes the I prefix. IoService.dispose() is IDisposable.Dispose() (the resolver-dns mapping of
// close() to Dispose()); bind() throws IOException when the socket cannot be bound; getLocalAddress() returns null
// while nothing is bound. SocketAddress is EndPoint. getSessionConfig() is declared by IDatagramAcceptor, the only
// acceptor of the stand-in.

using System;
using System.Net;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

public interface IIoAcceptor : IDisposable
{
    /**
     * Sets the handler which will handle all connections managed by this service.
     *
     * @param handler The IoHandler to use
     */
    void SetHandler(IIoHandler handler);

    /**
     * Binds to the default local address(es) and start to accept incoming
     * connections.
     *
     * @throws IOException if failed to bind
     */
    void Bind();

    /**
     * Unbinds from all local addresses that this service is bound to and stops
     * to accept incoming connections.
     */
    void Unbind();

    /**
     * Returns the local address which is bound currently.
     *
     * @return The bound LocalAddress
     */
    EndPoint GetLocalAddress();

    /**
     * @return <tt>true</tt> if and if only all resources of this processor
     * have been disposed.
     */
    bool IsDisposed();
}
