// CLR: C# has no checked exceptions, so "throws DnsException" is only documented: DomainNameService turns a
// DnsException, or a null or empty set (NAME_ERROR), into the response code. A C# lambda does not convert to an
// interface, so a Java lambda RecordStore is a class implementing IRecordStore.

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
