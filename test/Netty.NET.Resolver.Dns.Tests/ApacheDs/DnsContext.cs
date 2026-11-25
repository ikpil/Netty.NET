// CLR: test-only stand-in for org.apache.directory.server.dns.service.DnsContext of apacheds-protocol-dns 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/service/DnsContext.java
// The per-request state DnsProtocolHandler stores in the session; List/Collection are IList/IEnumerable.
// serialVersionUID has no counterpart.

using System.Collections.Generic;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

public class DnsContext
{
    private DnsServer config;
    private IRecordStore store;
    private DnsMessage reply;
    private readonly List<IResourceRecord> records = new List<IResourceRecord>();

    /**
     * @return Returns the recordEntry.
     */
    public IList<IResourceRecord> GetResourceRecords()
    {
        return records;
    }

    /**
     * @param resourceRecord The resourceRecord to add.
     */
    public void AddResourceRecord(IResourceRecord resourceRecord)
    {
        this.records.Add(resourceRecord);
    }

    /**
     * @param resourceRecords The resourceRecords to add.
     */
    public void AddResourceRecords(IEnumerable<IResourceRecord> resourceRecords)
    {
        this.records.AddRange(resourceRecords);
    }

    /**
     * @return Returns the config.
     */
    public DnsServer GetConfig()
    {
        return config;
    }

    /**
     * @param config The config to set.
     */
    public void SetConfig(DnsServer config)
    {
        this.config = config;
    }

    /**
     * @return Returns the reply.
     */
    public DnsMessage GetReply()
    {
        return reply;
    }

    /**
     * @param reply The reply to set.
     */
    public void SetReply(DnsMessage reply)
    {
        this.reply = reply;
    }

    /**
     * @return Returns the store.
     */
    public IRecordStore GetStore()
    {
        return store;
    }

    /**
     * @param store The store to set.
     */
    public void SetStore(IRecordStore store)
    {
        this.store = store;
    }
}
