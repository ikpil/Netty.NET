// CLR: test-only stand-in for org.apache.directory.server.dns.service.DomainNameService of apacheds-protocol-dns
// 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/service/DomainNameService.java
// The reply the tests receive is built here: every question's records come from RecordStore.getRecords; a null or
// empty set throws DnsException(NAME_ERROR) (DnsProtocolHandler turns any DnsException into an empty reply with that
// code); otherwise the reply is a RESPONSE/QUERY message with the request's transaction id, recursion-desired flag and
// question list, response code NO_ERROR, AA/TC/RA/Z/AD clear, the records in the set's iteration order as the answer
// section, and new empty lists as the authority and additional sections. The final class with a private constructor
// is a static class; the debug monitors keep their text (the I18n error keys ERR_153..155 are plain messages) and run
// only when debug logging is enabled, through Common's InternalLoggerFactory logger.

using System;
using System.Collections.Generic;
using System.Text;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

/**
 * Domain Name Service (DNS) Protocol (RFC 1034, 1035)
 */
public static class DomainNameService
{
    /** the log for this class */
    private static readonly IInternalLogger LOG = InternalLoggerFactory.GetInstance(typeof(DomainNameService));

    /**
     * Creates a new instance of DomainNameService.
     */
    public static void Execute(DnsContext dnsContext, DnsMessage request)
    {
        if (LOG.IsDebugEnabled())
        {
            MonitorRequest(request);
        }

        GetResourceRecords(dnsContext, request);

        if (LOG.IsDebugEnabled())
        {
            MonitorContext(dnsContext);
        }

        BuildReply(dnsContext, request);

        if (LOG.IsDebugEnabled())
        {
            MonitorReply(dnsContext);
        }
    }

    private static void MonitorRequest(DnsMessage request)
    {
        try
        {
            if (LOG.IsDebugEnabled())
            {
                LOG.Debug(MonitorMessage(request, "request"));
            }
        }
        catch (Exception e)
        {
            // This is a monitor.  No exceptions should bubble up.
            LOG.Error("ERR_153", e);
        }
    }

    private static void GetResourceRecords(DnsContext dnsContext, DnsMessage request)
    {
        IRecordStore store = dnsContext.GetStore();

        IList<QuestionRecord> questions = request.GetQuestionRecords();

        foreach (QuestionRecord question in questions)
        {
            dnsContext.AddResourceRecords(GetEntry(store, question));
        }
    }

    /**
     * Returns a set of {@link ResourceRecord}s from a {@link RecordStore}, given a DNS {@link QuestionRecord}.
     *
     * @param store
     * @param question
     * @return The set of {@link ResourceRecord}s.
     * @throws DNSException
     */
    private static ISet<IResourceRecord> GetEntry(IRecordStore store, QuestionRecord question)
    {
        ISet<IResourceRecord> records = null;

        records = store.GetRecords(question);

        if (records == null || records.Count == 0)
        {
            LOG.Debug("The domain name referenced in the query does not exist.");

            throw new DnsException(ResponseCode.NAME_ERROR);
        }

        return records;
    }

    private static void MonitorContext(DnsContext dnsContext)
    {
        try
        {
            IRecordStore store = dnsContext.GetStore();
            IList<IResourceRecord> records = dnsContext.GetResourceRecords();

            StringBuilder sb = new StringBuilder();
            sb.Append("Monitoring context:");
            sb.Append("\n\t" + "store:                     " + store);
            sb.Append("\n\t" + "records:                   " + string.Join(", ", records));

            LOG.Debug(sb.ToString());
        }
        catch (Exception e)
        {
            // This is a monitor.  No exceptions should bubble up.
            LOG.Error("ERR_154", e);
        }
    }

    private static void BuildReply(DnsContext dnsContext, DnsMessage request)
    {
        IList<IResourceRecord> records = dnsContext.GetResourceRecords();

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
        modifier.SetResponseCode(ResponseCode.NO_ERROR);
        modifier.SetQuestionRecords(request.GetQuestionRecords());

        modifier.SetAnswerRecords(records);
        modifier.SetAuthorityRecords(new List<IResourceRecord>());
        modifier.SetAdditionalRecords(new List<IResourceRecord>());

        dnsContext.SetReply(modifier.GetDnsMessage());
    }

    private static void MonitorReply(DnsContext dnsContext)
    {
        try
        {
            DnsMessage reply = dnsContext.GetReply();

            if (LOG.IsDebugEnabled())
            {
                LOG.Debug(MonitorMessage(reply, "reply"));
            }
        }
        catch (Exception e)
        {
            // This is a monitor.  No exceptions should bubble up.
            LOG.Error("ERR_155", e);
        }
    }

    private static string MonitorMessage(DnsMessage message, string direction)
    {
        MessageType messageType = message.GetMessageType();
        OpCode opCode = message.GetOpCode();
        ResponseCode responseCode = message.GetResponseCode();
        int transactionId = message.GetTransactionId();

        StringBuilder sb = new StringBuilder();
        sb.Append("Monitoring " + direction + ":");
        sb.Append("\n\t" + "messageType                " + messageType);
        sb.Append("\n\t" + "opCode                     " + opCode);
        sb.Append("\n\t" + "responseCode               " + responseCode);
        sb.Append("\n\t" + "transactionId              " + transactionId);
        sb.Append("\n\t" + "authoritativeAnswer        " + message.IsAuthoritativeAnswer());
        sb.Append("\n\t" + "truncated                  " + message.IsTruncated());
        sb.Append("\n\t" + "recursionDesired           " + message.IsRecursionDesired());
        sb.Append("\n\t" + "recursionAvailable         " + message.IsRecursionAvailable());
        sb.Append("\n\t" + "reserved                   " + message.IsReserved());
        sb.Append("\n\t" + "acceptNonAuthenticatedData " + message.IsAcceptNonAuthenticatedData());

        IList<QuestionRecord> questions = message.GetQuestionRecords();

        sb.Append("\n\t" + "questions:                 " + string.Join(", ", questions));

        return sb.ToString();
    }
}
