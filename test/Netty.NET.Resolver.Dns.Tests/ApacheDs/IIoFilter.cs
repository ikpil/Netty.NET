// CLR: MINA's WriteRequest (message, future, destination) is the message itself: the stand-in writes every message
// to the session's remote address and has no WriteFuture. The lifecycle events (sessionCreated, sessionClosed,...)
// pass through a codec filter unchanged in MINA (IoFilterAdapter), so the stand-in's chain delivers them to the
// handler directly.

using System;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

public interface IIoFilter
{
    /**
     * Filters {@link IoHandler#messageReceived(IoSession,Object)} event.
     *
     * @param nextFilter the {@link NextFilter} for this filter.
     * @param session The {@link IoSession} which has received this event
     * @param message The event's message
     */
    void MessageReceived(INextFilter nextFilter, IIoSession session, object message);

    /**
     * Filters {@link IoSession#write(Object)} method invocation.
     *
     * @param nextFilter the {@link NextFilter} for this filter.
     * @param session The {@link IoSession} which has to process this invocation
     * @param message The message to write
     */
    void FilterWrite(INextFilter nextFilter, IIoSession session, object message);

    /**
     * Represents the next {@link IoFilter} in {@link IoFilterChain}.
     */
    public interface INextFilter
    {
        /**
         * Forwards <tt>messageReceived</tt> event to next filter.
         *
         * @param session The {@link IoSession} which has to process this invocation
         * @param message The received message
         */
        void MessageReceived(IIoSession session, object message);

        /**
         * Forwards <tt>filterWrite</tt> event to next filter.
         *
         * @param session The {@link IoSession} which has to process this invocation
         * @param message The message to write
         */
        void FilterWrite(IIoSession session, object message);

        /**
         * Forwards <tt>exceptionCaught</tt> event to next filter.
         *
         * @param session The {@link IoSession} which has to process this invocation
         * @param cause The exception that cause this event to be received
         */
        void ExceptionCaught(IIoSession session, Exception cause);
    }
}
