// CLR: test-only stand-in for org.apache.mina.core.session.IoSession of Apache MINA 2.2.3 (the MINA version that
// apacheds-protocol-dns 2.0.0.AM27 pins; mina.core.version in
// https://github.com/apache/directory-server/blob/2.0.0.AM27/pom.xml):
// https://github.com/apache/mina/blob/2.2.3/mina-core/src/main/java/org/apache/mina/core/session/IoSession.java
// The interface takes the I prefix and keeps the members the stand-in's UDP sessions need. write(Object) returns no
// WriteFuture and closeNow() no CloseFuture: the stand-in writes and closes synchronously on the acceptor thread, and
// no caller waits on either future. SocketAddress is EndPoint.

using System.Net;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

public interface IIoSession
{
    /**
     * @return a unique identifier for this session.  Every session has its own
     * ID which is different from each other.
     */
    long GetId();

    /**
     * @return the filter chain that only affects this session.
     */
    IIoFilterChain GetFilterChain();

    /**
     * Writes the specified <code>message</code> to remote peer.
     *
     * @param message The message to write
     */
    void Write(object message);

    /**
     * Closes this session immediately.
     */
    void CloseNow();

    /**
     * Returns the value of the user-defined attribute of this session.
     *
     * @param key the key of the attribute
     * @return <tt>null</tt> if there is no attribute with the specified key
     */
    object GetAttribute(object key);

    /**
     * Sets a user-defined attribute.
     *
     * @param key the key of the attribute
     * @param value the value of the attribute
     * @return The old value of the attribute.  <tt>null</tt> if it is new.
     */
    object SetAttribute(object key, object value);

    /**
     * @return the socket address of remote peer.
     */
    EndPoint GetRemoteAddress();

    /**
     * @return the socket address of local machine which is associated with this
     * session.
     */
    EndPoint GetLocalAddress();
}
