// CLR: test-only stand-in for org.apache.directory.server.dns.protocol.DnsProtocolHandler of apacheds-protocol-dns
// 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/protocol/DnsProtocolHandler.java
// It implements IIoHandler directly (MINA's IoHandlerAdapter only adds no-op defaults, and this class overrides every
// callback); the callbacks are virtual because TestDnsServer overrides sessionCreated. sessionCreated installs the
// DnsProtocolUdpCodecFactory codec: the source picks the TCP codec (DnsProtocolTcpCodecFactory) for a session whose
// transport metadata is not connectionless, but the stand-in has only the datagram transport (UdpTransport), so that
// branch has no counterpart. messageReceived keeps the source exactly: DomainNameService builds the reply, and any
// exception is cast to DnsException (InvalidCastException here as ClassCastException there, which escapes to
// exceptionCaught and closes the session without a response) to answer with an empty reply carrying its response code.
// exceptionCaught logs and closes the session (closeNow). sessionIdle and inputClosed are not reproduced (IIoHandler).
// The SLF4J logger is Common's InternalLoggerFactory logger.

using System;
using System.Collections.Generic;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

public class DnsProtocolHandler : IIoHandler
{
    private static readonly IInternalLogger LOG = InternalLoggerFactory.GetInstance(typeof(DnsProtocolHandler));

    private readonly DnsServer config;
    private readonly IRecordStore store;
    private readonly string contextKey = "context";

    /**
     * Creates a new instance of DnsProtocolHandler.
     *
     * @param config
     * @param store
     */
    public DnsProtocolHandler(DnsServer config, IRecordStore store)
    {
        this.config = config;
        this.store = store;
    }

    public virtual void SessionCreated(IIoSession session)
    {
        if (LOG.IsDebugEnabled())
        {
            LOG.Debug("{} CREATED:  {}", session.GetRemoteAddress(), "nio datagram");
        }

        session.GetFilterChain().AddFirst("codec",
            new ProtocolCodecFilter(DnsProtocolUdpCodecFactory.GetInstance()));
    }

    public virtual void SessionOpened(IIoSession session)
    {
        LOG.Debug("{} OPENED", session.GetRemoteAddress());
    }

    public virtual void SessionClosed(IIoSession session)
    {
        LOG.Debug("{} CLOSED", session.GetRemoteAddress());
    }

    public virtual void ExceptionCaught(IIoSession session, Exception cause)
    {
        LOG.Error(session.GetRemoteAddress() + " EXCEPTION", cause);
        session.CloseNow();
    }

    public virtual void MessageReceived(IIoSession session, object message)
    {
        LOG.Debug("{} RCVD:  {}", session.GetRemoteAddress(), message);

        try
        {
            DnsContext dnsContext = new DnsContext();
            dnsContext.SetConfig(config);
            dnsContext.SetStore(store);
            session.SetAttribute(GetContextKey(), dnsContext);

            DomainNameService.Execute(dnsContext, (DnsMessage)message);

            DnsMessage response = dnsContext.GetReply();

            session.Write(response);
        }
        catch (Exception e)
        {
            LOG.Error(e.Message, e);

            DnsMessage request = (DnsMessage)message;
            DnsException de = (DnsException)e;

            DnsMessageModifier modifier = new DnsMessageModifier();

            modifier.SetTransactionId(request.GetTransactionId());
            modifier.SetMessageType(MessageType.RESPONSE);
            modifier.SetOpCode(OpCode.QUERY);
            modifier.SetAuthoritativeAnswer(false);
            modifier.SetTruncated(false);
            modifier.SetRecursionDesired(request.IsRecursionDesired());
            modifier.SetRecursionAvailable(false);
            modifier.SetReserved(false);
            modifier.SetAcceptNonAuthenticatedData(false);
            modifier.SetResponseCode(ResponseCode.Convert((byte)de.GetResponseCode()));
            modifier.SetQuestionRecords(request.GetQuestionRecords());
            modifier.SetAnswerRecords(new List<IResourceRecord>());
            modifier.SetAuthorityRecords(new List<IResourceRecord>());
            modifier.SetAdditionalRecords(new List<IResourceRecord>());

            session.Write(modifier.GetDnsMessage());
        }
    }

    public virtual void MessageSent(IIoSession session, object message)
    {
        LOG.Debug("{} SENT:  {}", session.GetRemoteAddress(), message);
    }

    protected virtual string GetContextKey()
    {
        return (this.contextKey);
    }
}
