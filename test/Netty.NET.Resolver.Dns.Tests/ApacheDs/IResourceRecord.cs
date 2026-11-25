// CLR: test-only stand-in for org.apache.directory.server.dns.messages.ResourceRecord of apacheds-protocol-dns
// 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/messages/ResourceRecord.java
// The interface takes the I prefix; the getters stay methods.

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

public interface IResourceRecord
{
    /**
     * @return Returns the domainName.
     */
    string GetDomainName();

    /**
     * @return Returns the recordType.
     */
    RecordType GetRecordType();

    /**
     * @return Returns the recordClass.
     */
    RecordClass GetRecordClass();

    /**
     * @return Returns the timeToLive.
     */
    int GetTimeToLive();

    /**
     * @param id
     * @return Returns the value for an id.
     */
    string Get(string id);
}
