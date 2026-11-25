// CLR: test-only stand-in for org.apache.directory.server.dns.messages.QuestionRecord of apacheds-protocol-dns
// 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/messages/QuestionRecord.java
// equals/hashCode compare the domain name (ordinal, case-sensitive), the class and the type, as the commons-lang
// EqualsBuilder/HashCodeBuilder of the source do; HashCode.Combine replaces the HashCodeBuilder arithmetic (the value
// is not observable by the tests). toString keeps the field order of the ToStringBuilder without its identity
// prefix. The domain name is the decoded name without the trailing dot of the root label (DnsMessageDecoder).

using System;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

/**
 * The question section is used to carry the "question" in most queries,
 * i.e., the parameters that define what is being asked.  The section
 * contains QDCOUNT (usually 1) entries, each of the following format:
 *
 *                                     1  1  1  1  1  1
 *       0  1  2  3  4  5  6  7  8  9  0  1  2  3  4  5
 *     +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
 *     |                                               |
 *     /                     QNAME                     /
 *     /                                               /
 *     +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
 *     |                     QTYPE                     |
 *     +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
 *     |                     QCLASS                    |
 *     +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
 */
public class QuestionRecord
{
    /**
     * A domain name represented as a sequence of labels, where
     * each label consists of a length octet followed by that
     * number of octets.  The domain name terminates with the
     * zero length octet for the null label of the root.  Note
     * that this field may be an odd number of octets; no
     * padding is used.
     */
    private readonly string domainName;

    /**
     * A two octet code which specifies the type.
     */
    private readonly RecordType recordType;

    /**
     * A two octet code that specifies the class.
     * For example, the CLASS field is IN for the Internet.
     */
    private readonly RecordClass recordClass;

    /**
     * Creates a new instance of QuestionRecord.
     *
     * @param domainName
     * @param recordType
     * @param recordClass
     */
    public QuestionRecord(string domainName, RecordType recordType, RecordClass recordClass)
    {
        this.domainName = domainName;
        this.recordType = recordType;
        this.recordClass = recordClass;
    }

    /**
     * The domain name of this query.
     * For example, www.example.com.
     *
     * @return The domain name.
     */
    public string GetDomainName()
    {
        return domainName;
    }

    /**
     * The type of the query.
     * For example, the type is A for address records.
     *
     * @return The {@link RecordType}.
     */
    public RecordType GetRecordType()
    {
        return recordType;
    }

    /**
     * The class for this query.
     * For example, the class is IN for the Internet.
     *
     * @return The {@link RecordClass}.
     */
    public RecordClass GetRecordClass()
    {
        return recordClass;
    }

    public override bool Equals(object @object)
    {
        if (@object == this)
        {
            return true;
        }

        if (!(@object is QuestionRecord))
        {
            return false;
        }

        QuestionRecord rhs = (QuestionRecord)@object;
        return string.Equals(this.domainName, rhs.domainName, StringComparison.Ordinal) &&
               this.recordClass == rhs.recordClass && this.recordType == rhs.recordType;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(domainName, recordClass, recordType);
    }

    public override string ToString()
    {
        return "QuestionRecord[domainName=" + domainName + ",recordClass=" + recordClass + ",recordType=" +
               recordType + "]";
    }
}
