// CLR: test-only stand-in for org.apache.mina.core.service.IoHandler of Apache MINA 2.2.3:
// https://github.com/apache/mina/blob/2.2.3/mina-core/src/main/java/org/apache/mina/core/service/IoHandler.java
// The interface takes the I prefix; "throws Exception" is only documented. sessionIdle (the stand-in has no idle
// detection), inputClosed (a datagram session has no half-close) and event (no filter emits one) are not declared.

using System;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

public interface IIoHandler
{
    /**
     * Invoked from an I/O processor thread when a new connection has been created.
     *
     * @param session The session being created
     * @throws Exception If we get an exception while processing the create event
     */
    void SessionCreated(IIoSession session);

    /**
     * Invoked when a connection has been opened.  This method is invoked after
     * {@link #sessionCreated(IoSession)}.
     *
     * @param session The session being opened
     * @throws Exception If we get an exception while processing the open event
     */
    void SessionOpened(IIoSession session);

    /**
     * Invoked when a connection is closed.
     *
     * @param session The session being closed
     * @throws Exception If we get an exception while processing the close event
     */
    void SessionClosed(IIoSession session);

    /**
     * Invoked when any exception is thrown by user {@link IoHandler}
     * implementation or by MINA.
     *
     * @param session The session for which we have got an exception
     * @param cause The exception that has been caught
     * @throws Exception If we get an exception while processing the caught exception
     */
    void ExceptionCaught(IIoSession session, Exception cause);

    /**
     * Invoked when a message is received.
     *
     * @param session The session that is receiving a message
     * @param message The received message
     * @throws Exception If we get an exception while processing the received message
     */
    void MessageReceived(IIoSession session, object message);

    /**
     * Invoked when a message written by {@link IoSession#write(Object)} is
     * sent out.
     *
     * @param session The session that has sent a full message
     * @param message The sent message
     * @throws Exception If we get an exception while processing the sent message
     */
    void MessageSent(IIoSession session, object message);
}
