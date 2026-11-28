// CLR: test-only stand-in for org.apache.mina.transport.socket.nio.NioDatagramAcceptor of Apache MINA 2.2.3, with the
// session, filter chain and session config it creates:
// https://github.com/apache/mina/blob/2.2.3/mina-core/src/main/java/org/apache/mina/transport/socket/nio/NioDatagramAcceptor.java
// https://github.com/apache/mina/blob/2.2.3/mina-core/src/main/java/org/apache/mina/core/filterchain/DefaultIoFilterChain.java
// https://github.com/apache/mina/blob/2.2.3/mina-core/src/main/java/org/apache/mina/transport/socket/DefaultDatagramSessionConfig.java
// Built on a BCL UDP Socket and one dedicated receive thread, as MINA runs one selector thread per acceptor:
// - bind() opens the socket, applies SO_REUSEADDR from the session config, binds the default local address (the
//   wildcard address with an ephemeral port when none is set) and starts the thread; a bind failure closes the socket
//   and throws IOException("Error while binding on <address>\noriginal message : <message>") as open() does (the BCL
//   SocketException is not an IOException, https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketexception?view=net-10.0,
//   so it becomes the inner exception where MINA copies the cause of the original IOException);
// - every datagram is copied into IoBuffer.allocate(readBufferSize) (2048 bytes): DatagramChannel.receive silently
//   discards what does not fit
//   (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/channels/DatagramChannel.html), so the
//   socket receives into a 64 KiB array and only the first readBufferSize bytes are kept, because Socket.ReceiveFrom
//   throws when a datagram is larger than its buffer
//   (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.receivefrom?view=net-10.0);
// - a session per remote address is created on its first datagram (sessionCreated then sessionOpened on the handler,
//   where TestDnsServer adds its codec), and the buffer is fired through the session's filter chain to the handler,
//   all on the receive thread, so the RecordStore and filterMessage run one request at a time, as in MINA;
// - session.write(message) runs the chain's filterWrite from the last filter to the first and sends a non-empty
//   IoBuffer to the session's remote address at the head; an empty buffer sends nothing; an exception of a filter, of
//   the handler or of the send fires exceptionCaught on the handler (DefaultIoFilterChain), and closeNow() removes the
//   session so the next datagram from that address creates a new one;
// - unbind()/dispose() close the socket, wait for the receive thread and close the sessions (closeOnDeactivation is
//   true by default); getLocalAddress() is null when nothing is bound.
// A receive that fails with WSAECONNRESET is skipped: on Windows it reports an ICMP "Port Unreachable" caused by an
// earlier send to a client socket that is gone
// (https://learn.microsoft.com/en-us/windows/win32/api/winsock2/nf-winsock2-wsarecvfrom), which would otherwise end the
// loop. Not reproduced: the ExpiringSessionRecycler (sessions idle for 60 seconds expire; here they last until closed
// or unbound), idle notification, the IoServiceListener events, write queues and futures (writes are synchronous),
// binding several addresses, and the Linux IPv4-compatible address workaround of localAddress(DatagramChannel). The
// "unexpected exception" reports of MINA's ExceptionMonitor go to Common's InternalLoggerFactory logger.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

/**
 * {@link IoAcceptor} for datagram transport (UDP/IP).
 */
public sealed class NioDatagramAcceptor : IDatagramAcceptor
{
    private static readonly IInternalLogger LOG = InternalLoggerFactory.GetInstance(typeof(NioDatagramAcceptor));

    // AbstractIoSession.idGenerator
    private static long idGenerator;

    private readonly object @lock = new object();
    private readonly DefaultDatagramSessionConfig sessionConfig = new DefaultDatagramSessionConfig();
    private readonly Dictionary<EndPoint, NioSession> sessions = new Dictionary<EndPoint, NioSession>();

    private IIoHandler handler;
    private EndPoint defaultLocalAddress;
    private Socket handle;
    private IPEndPoint localAddress;
    private Thread acceptor;
    private bool disposed;

    /**
     * Creates a new instance.
     */
    public NioDatagramAcceptor()
    {
    }

    public void SetHandler(IIoHandler handler)
    {
        if (handler == null)
        {
            throw new ArgumentException("handler cannot be null");
        }

        lock (@lock)
        {
            if (handle != null)
            {
                throw new InvalidOperationException("handler cannot be set while the service is active.");
            }

            this.handler = handler;
        }
    }

    public IDatagramSessionConfig GetSessionConfig()
    {
        return sessionConfig;
    }

    public void SetDefaultLocalAddress(EndPoint localAddress)
    {
        lock (@lock)
        {
            defaultLocalAddress = localAddress;
        }
    }

    public IPEndPoint GetLocalAddress()
    {
        lock (@lock)
        {
            return localAddress;
        }
    }

    EndPoint IIoAcceptor.GetLocalAddress()
    {
        return GetLocalAddress();
    }

    public bool IsDisposed()
    {
        lock (@lock)
        {
            return disposed;
        }
    }

    public void Bind()
    {
        lock (@lock)
        {
            if (disposed)
            {
                throw new InvalidOperationException("The Accpetor disposed is being disposed.");
            }

            if (handler == null)
            {
                throw new InvalidOperationException("handler is not set.");
            }

            if (handle != null)
            {
                throw new InvalidOperationException("Already bound to " + localAddress);
            }

            Socket ch = Open(defaultLocalAddress ?? new IPEndPoint(IPAddress.Any, 0));
            handle = ch;
            localAddress = (IPEndPoint)ch.LocalEndPoint;
            acceptor = new Thread(() => Run(ch))
            {
                IsBackground = true,
                Name = "NioDatagramAcceptor-" + localAddress.Port
            };
            acceptor.Start();
        }
    }

    public void Unbind()
    {
        Socket ch;
        Thread thread;
        List<NioSession> managedSessions;

        lock (@lock)
        {
            ch = handle;
            thread = acceptor;
            handle = null;
            localAddress = null;
            acceptor = null;
            managedSessions = new List<NioSession>(sessions.Values);
            sessions.Clear();
        }

        if (ch == null)
        {
            return;
        }

        ch.Dispose();

        if (thread != null && thread != Thread.CurrentThread && !thread.Join(TimeSpan.FromSeconds(30)))
        {
            LOG.Warn("The receive thread of {} did not stop.", thread.Name);
        }

        foreach (NioSession session in managedSessions)
        {
            session.chain.FireSessionClosed();
        }
    }

    public void Dispose()
    {
        lock (@lock)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        Unbind();
    }

    private Socket Open(EndPoint localAddress)
    {
        if (!(localAddress is IPEndPoint ipLocalAddress))
        {
            // CLR: DatagramSocket.bind rejects an unresolved InetSocketAddress ("Unresolved address").
            throw new IOException("Error while binding on " + localAddress + "\n" +
                                  "original message : Unresolved address");
        }

        Socket ch = new Socket(ipLocalAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

        try
        {
            ch.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress,
                               sessionConfig.IsReuseAddress());
            ch.Bind(ipLocalAddress);
        }
        catch (SocketException ioe)
        {
            // Add some info regarding the address we try to bind to the
            // message
            string newMessage = "Error while binding on " + localAddress + "\n" + "original message : " +
                                ioe.Message;

            // And close the channel
            ch.Dispose();

            throw new IOException(newMessage, ioe);
        }

        return ch;
    }

    private void Run(Socket ch)
    {
        byte[] datagram = new byte[65536];

        for (;;)
        {
            EndPoint remoteAddress = new IPEndPoint(
                ch.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
            int length;

            try
            {
                length = ch.ReceiveFrom(datagram, ref remoteAddress);
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset)
            {
                // CLR: ICMP "Port Unreachable" for an earlier send (see the header).
                continue;
            }
            catch (Exception e) when (e is SocketException || e is ObjectDisposedException)
            {
                if (IsBound(ch))
                {
                    LOG.Warn("Unexpected exception.", e);
                    continue;
                }

                // The socket was closed by unbind() or dispose().
                break;
            }

            try
            {
                ReadHandle(ch, datagram, length, remoteAddress);
            }
            catch (Exception e)
            {
                LOG.Warn("Unexpected exception.", e);
            }
        }
    }

    private bool IsBound(Socket ch)
    {
        lock (@lock)
        {
            return handle == ch;
        }
    }

    private void ReadHandle(Socket ch, byte[] datagram, int length, EndPoint remoteAddress)
    {
        IoBuffer readBuf = IoBuffer.Allocate(sessionConfig.GetReadBufferSize());
        readBuf.Put(datagram, 0, Math.Min(length, readBuf.Remaining()));

        NioSession session = NewSessionWithoutLock(ch, remoteAddress);
        if (session == null)
        {
            return;
        }

        readBuf.Flip();

        session.chain.FireMessageReceived(readBuf);
    }

    private NioSession NewSessionWithoutLock(Socket ch, EndPoint remoteAddress)
    {
        NioSession session;

        lock (@lock)
        {
            if (handle != ch)
            {
                return null;
            }

            if (sessions.TryGetValue(remoteAddress, out session))
            {
                return session;
            }

            // If a new session needs to be created.
            session = new NioSession(this, ch, remoteAddress, localAddress, handler);
            sessions[remoteAddress] = session;
        }

        session.chain.FireSessionCreated();
        session.chain.FireSessionOpened();

        return session;
    }

    private bool RemoveSession(NioSession session)
    {
        lock (@lock)
        {
            if (sessions.TryGetValue(session.remoteAddress, out NioSession current) && current == session)
            {
                return sessions.Remove(session.remoteAddress);
            }

            return false;
        }
    }

    private static void Send(NioSession session, IoBuffer buf)
    {
        if (buf.Remaining() == 0)
        {
            return;
        }

        session.handle.SendTo(buf.Array(), buf.Position(), buf.Remaining(), SocketFlags.None,
                              session.remoteAddress);
        buf.Position(buf.Limit());
    }

    // CLR: org.apache.mina.transport.socket.nio.NioSession / NioDatagramSession.
    private sealed class NioSession : IIoSession
    {
        private readonly NioDatagramAcceptor service;
        private readonly long id = Interlocked.Increment(ref idGenerator);
        private readonly EndPoint localAddress;
        private readonly Dictionary<object, object> attributes = new Dictionary<object, object>();
        internal readonly Socket handle;
        internal readonly EndPoint remoteAddress;
        internal readonly DefaultIoFilterChain chain;

        internal NioSession(NioDatagramAcceptor service, Socket handle, EndPoint remoteAddress,
                            EndPoint localAddress, IIoHandler handler)
        {
            this.service = service;
            this.handle = handle;
            this.remoteAddress = remoteAddress;
            this.localAddress = localAddress;
            chain = new DefaultIoFilterChain(this, handler);
        }

        public long GetId()
        {
            return id;
        }

        public IIoFilterChain GetFilterChain()
        {
            return chain;
        }

        public void Write(object message)
        {
            chain.FireFilterWrite(message);
        }

        public void CloseNow()
        {
            if (service.RemoveSession(this))
            {
                chain.FireSessionClosed();
            }
        }

        public object GetAttribute(object key)
        {
            lock (attributes)
            {
                return attributes.TryGetValue(key, out object value) ? value : null;
            }
        }

        public object SetAttribute(object key, object value)
        {
            lock (attributes)
            {
                attributes.TryGetValue(key, out object oldValue);
                attributes[key] = value;
                return oldValue;
            }
        }

        public EndPoint GetRemoteAddress()
        {
            return remoteAddress;
        }

        public EndPoint GetLocalAddress()
        {
            return localAddress;
        }

        public override string ToString()
        {
            return "(0x" + id.ToString("X8") + ": nio datagram, server, " + remoteAddress + " => " + localAddress +
                   ')';
        }
    }

    // CLR: org.apache.mina.core.filterchain.DefaultIoFilterChain: the head sends, the tail calls the handler.
    private sealed class DefaultIoFilterChain : IIoFilterChain
    {
        private readonly NioSession session;
        private readonly IIoHandler handler;
        private readonly List<KeyValuePair<string, IIoFilter>> entries = new List<KeyValuePair<string, IIoFilter>>();

        internal DefaultIoFilterChain(NioSession session, IIoHandler handler)
        {
            this.session = session;
            this.handler = handler;
        }

        public void AddFirst(string name, IIoFilter filter)
        {
            lock (entries)
            {
                foreach (KeyValuePair<string, IIoFilter> entry in entries)
                {
                    if (entry.Key == name)
                    {
                        throw new ArgumentException("Other filter is using the same name '" + name + "'");
                    }
                }

                entries.Insert(0, new KeyValuePair<string, IIoFilter>(name, filter));
            }
        }

        private IIoFilter[] Filters()
        {
            lock (entries)
            {
                IIoFilter[] filters = new IIoFilter[entries.Count];

                for (int i = 0; i < filters.Length; i++)
                {
                    filters[i] = entries[i].Value;
                }

                return filters;
            }
        }

        internal void FireSessionCreated()
        {
            try
            {
                handler.SessionCreated(session);
            }
            catch (Exception e)
            {
                FireExceptionCaught(e);
            }
        }

        internal void FireSessionOpened()
        {
            try
            {
                handler.SessionOpened(session);
            }
            catch (Exception e)
            {
                FireExceptionCaught(e);
            }
        }

        internal void FireSessionClosed()
        {
            try
            {
                handler.SessionClosed(session);
            }
            catch (Exception e)
            {
                FireExceptionCaught(e);
            }
        }

        internal void FireMessageReceived(object message)
        {
            CallNextMessageReceived(Filters(), 0, message);
        }

        internal void FireFilterWrite(object message)
        {
            IIoFilter[] filters = Filters();
            bool[] sent = new bool[1];
            CallPreviousFilterWrite(filters, filters.Length - 1, message, sent);

            if (sent[0])
            {
                try
                {
                    handler.MessageSent(session, message);
                }
                catch (Exception e)
                {
                    FireExceptionCaught(e);
                }
            }
        }

        internal void FireExceptionCaught(Exception cause)
        {
            try
            {
                handler.ExceptionCaught(session, cause);
            }
            catch (Exception e)
            {
                LOG.Warn("Unexpected exception from exceptionCaught handler.", e);
            }
        }

        private void CallNextMessageReceived(IIoFilter[] filters, int index, object message)
        {
            try
            {
                if (index < filters.Length)
                {
                    filters[index].MessageReceived(new NextFilter(this, filters, index, null), session, message);
                }
                else
                {
                    // TailFilter
                    handler.MessageReceived(session, message);
                }
            }
            catch (Exception e)
            {
                FireExceptionCaught(e);
            }
        }

        private void CallPreviousFilterWrite(IIoFilter[] filters, int index, object message, bool[] sent)
        {
            try
            {
                if (index >= 0)
                {
                    filters[index].FilterWrite(new NextFilter(this, filters, index, sent), session, message);
                }
                else
                {
                    // HeadFilter
                    Send(session, (IoBuffer)message);
                    sent[0] = true;
                }
            }
            catch (Exception e)
            {
                FireExceptionCaught(e);
            }
        }

        private sealed class NextFilter : IIoFilter.INextFilter
        {
            private readonly DefaultIoFilterChain chain;
            private readonly IIoFilter[] filters;
            private readonly int index;
            private readonly bool[] sent;

            internal NextFilter(DefaultIoFilterChain chain, IIoFilter[] filters, int index, bool[] sent)
            {
                this.chain = chain;
                this.filters = filters;
                this.index = index;
                this.sent = sent;
            }

            public void MessageReceived(IIoSession session, object message)
            {
                chain.CallNextMessageReceived(filters, index + 1, message);
            }

            public void FilterWrite(IIoSession session, object message)
            {
                chain.CallPreviousFilterWrite(filters, index - 1, message, sent ?? new bool[1]);
            }

            public void ExceptionCaught(IIoSession session, Exception cause)
            {
                chain.FireExceptionCaught(cause);
            }
        }
    }

    // CLR: org.apache.mina.transport.socket.DefaultDatagramSessionConfig (reuseAddress false by default) with
    // AbstractIoSessionConfig.readBufferSize (2048 by default).
    private sealed class DefaultDatagramSessionConfig : IDatagramSessionConfig
    {
        private volatile bool reuseAddress;
        private volatile int readBufferSize = 2048;

        public bool IsReuseAddress()
        {
            return reuseAddress;
        }

        public void SetReuseAddress(bool reuseAddress)
        {
            this.reuseAddress = reuseAddress;
        }

        public int GetReadBufferSize()
        {
            return readBufferSize;
        }

        public void SetReadBufferSize(int readBufferSize)
        {
            if (readBufferSize <= 0)
            {
                throw new ArgumentException("readBufferSize: " + readBufferSize + " (expected: 1+)");
            }

            this.readBufferSize = readBufferSize;
        }
    }
}
