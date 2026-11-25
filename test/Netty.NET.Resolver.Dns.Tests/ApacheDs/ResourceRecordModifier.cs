// CLR: test-only stand-in for org.apache.directory.server.dns.messages.ResourceRecordModifier of apacheds-protocol-dns
// 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/messages/ResourceRecordModifier.java
// The HashMap of attributes is a Dictionary<string, object>; put(id, value) stores the value under
// Strings.toLowerCaseAscii(id), and getEntry() passes this same map (not a copy) to every ResourceRecordImpl it builds,
// as the source does.

using System.Collections.Generic;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

public class ResourceRecordModifier
{
    private string dnsName;
    private RecordType dnsType;
    private RecordClass dnsClass;
    private int dnsTtl;

    private readonly IDictionary<string, object> attributes = new Dictionary<string, object>();

    /**
     * Returns the {@link ResourceRecord} built by this {@link ResourceRecordModifier}.
     *
     * @return The {@link ResourceRecord}.
     */
    public IResourceRecord GetEntry()
    {
        return new ResourceRecordImpl(dnsName, dnsType, dnsClass, dnsTtl, attributes);
    }

    /**
     * @param dnsName The dnsName to set.
     */
    public void SetDnsName(string dnsName)
    {
        this.dnsName = dnsName;
    }

    /**
     * @param dnsType The dnsType to set.
     */
    public void SetDnsType(RecordType dnsType)
    {
        this.dnsType = dnsType;
    }

    /**
     * @param dnsClass The dnsClass to set.
     */
    public void SetDnsClass(RecordClass dnsClass)
    {
        this.dnsClass = dnsClass;
    }

    /**
     * @param dnsTtl The dnsTtl to set.
     */
    public void SetDnsTtl(int dnsTtl)
    {
        this.dnsTtl = dnsTtl;
    }

    /**
     * @param id The id to set
     * @param value The value to set
     */
    public void Put(string id, string value)
    {
        attributes[Strings.ToLowerCaseAscii(id)] = value;
    }
}
