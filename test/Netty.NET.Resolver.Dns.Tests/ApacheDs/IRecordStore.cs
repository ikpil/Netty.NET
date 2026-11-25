// CLR: test-only stand-in for org.apache.directory.server.dns.store.RecordStore of apacheds-protocol-dns 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/store/RecordStore.java
// The interface takes the I prefix; Set<ResourceRecord> is ISet<IResourceRecord> (an ordered Java LinkedHashSet is
// Common's LinkedHashSet, which implements ISet). C# has no checked exceptions, so "throws DnsException" is only
// documented: DomainNameService turns a DnsException, or a null or empty set (NAME_ERROR), into the response code.
// A C# lambda does not convert to an interface, so a Java lambda RecordStore is a class implementing IRecordStore.

using System.Collections.Generic;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

/**
 * The store interface used by the DNS protocol to lookup resource records.
 */
public interface IRecordStore
{
    /**
     * Returns a set of {@link ResourceRecord}s, given a DNS {@link QuestionRecord}.
     * If something bad happens throw the accurate {@link DnsException}
     * @param question
     * @return The set of {@link ResourceRecord}s.
     * @throws DnsException
     */
    ISet<IResourceRecord> GetRecords(QuestionRecord question);
}
