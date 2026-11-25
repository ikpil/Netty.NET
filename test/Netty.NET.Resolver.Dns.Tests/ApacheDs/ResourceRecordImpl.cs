// CLR: test-only stand-in for org.apache.directory.server.dns.messages.ResourceRecordImpl of apacheds-protocol-dns
// 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/messages/ResourceRecordImpl.java
// Map<String, Object> is IDictionary<string, object>, kept by reference as in the source (ResourceRecordModifier
// hands its own map to every entry it builds). get(id) looks up Strings.toLowerCaseAscii(id) and casts the value to
// String (InvalidCastException here as ClassCastException there; a missing key gives null). equals keeps the source
// shape: any ResourceRecord passes the instanceof check and is then cast to ResourceRecordImpl, the names compare
// with equalsIgnoreCase (OrdinalIgnoreCase, the same for the ASCII names DNS uses), the type and class by reference
// and the attribute maps with java.util.AbstractMap.equals (same size and every key mapped to an equal value).
// hashCode is the same sum of the name, type and class hash codes (case-sensitive, as in the source); C#'s string and
// object hash codes differ from Java's, which only changes the iteration order of a HashSet, unspecified on both
// platforms (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/HashSet.html,
// https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.hashset-1?view=net-10.0). toString prints
// the map in the java.util.AbstractMap form ({key=value, ...}).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

/**
 * The answer, authority, and additional sections all share the same
 * format: a variable number of resource records, where the number of
 * records is specified in the corresponding count field in the header.
 * Each resource record has the following format:
 *                                     1  1  1  1  1  1
 *       0  1  2  3  4  5  6  7  8  9  0  1  2  3  4  5
 *     +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
 *     |                                               |
 *     /                                               /
 *     /                      NAME                     /
 *     |                                               |
 *     +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
 *     |                      TYPE                     |
 *     +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
 *     |                     CLASS                     |
 *     +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
 *     |                      TTL                      |
 *     |                                               |
 *     +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
 *     |                   RDLENGTH                    |
 *     +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--|
 *     /                     RDATA                     /
 *     /                                               /
 *     +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
 */
public class ResourceRecordImpl : IResourceRecord
{
    /**
     * An owner name, i.e., the name of the node to which this
     * resource record pertains.
     */
    private readonly string domainName;

    /**
     * Two octets containing one of the resource record TYPE codes.
     */
    private readonly RecordType recordType;

    /**
     * Two octets containing one of the resource record CLASS codes.
     * For example, the CLASS field is IN for the Internet.
     */
    private readonly RecordClass recordClass;

    /**
     * A 32 bit signed integer that specifies the time interval
     * that the resource record may be cached before the source
     * of the information should again be consulted.
     */
    private readonly int timeToLive;

    /**
     * A variable length string of octets that describes the
     * resource.  The format of this information varies
     * according to the TYPE and CLASS of the resource record.
     */
    private readonly IDictionary<string, object> attributes;

    /**
     * Creates a new instance of ResourceRecordImpl.
     *
     * @param domainName
     * @param recordType
     * @param recordClass
     * @param timeToLive
     * @param attributes
     */
    public ResourceRecordImpl(string domainName, RecordType recordType, RecordClass recordClass, int timeToLive,
                              IDictionary<string, object> attributes)
    {
        this.domainName = domainName;
        this.recordType = recordType;
        this.recordClass = recordClass;
        this.timeToLive = timeToLive;
        this.attributes = attributes;
    }

    /**
     * @return Returns the domainName.
     */
    public string GetDomainName()
    {
        return domainName;
    }

    /**
     * @return Returns the recordType.
     */
    public RecordType GetRecordType()
    {
        return recordType;
    }

    /**
     * @return Returns the recordClass.
     */
    public RecordClass GetRecordClass()
    {
        return recordClass;
    }

    /**
     * @return Returns the timeToLive.
     */
    public int GetTimeToLive()
    {
        return timeToLive;
    }

    /**
     * @return Returns the value for the id.
     */
    public string Get(string id)
    {
        return attributes.TryGetValue(Strings.ToLowerCaseAscii(id), out object value) ? (string)value : null;
    }

    public override bool Equals(object o)
    {
        if (this == o)
        {
            return true;
        }

        if (!(o is IResourceRecord))
        {
            return false;
        }

        ResourceRecordImpl that = (ResourceRecordImpl)o;

        return this.domainName.Equals(that.domainName, StringComparison.OrdinalIgnoreCase)
               && (this.recordType == that.recordType)
               && (this.recordClass == that.recordClass)
               && MapEquals(this.attributes, that.attributes);
    }

    /**
     * Compute the instance hash code
     * @return the instance's hash code
     */
    public override int GetHashCode()
    {
        return domainName.GetHashCode() + recordType.GetHashCode() + recordClass.GetHashCode();
    }

    public override string ToString()
    {
        return GetType().FullName + " [ " + domainName + " ( " + recordType + " " + recordClass + " " +
               timeToLive.ToString(CultureInfo.InvariantCulture) + " " + MapToString(attributes) + " ) ]";
    }

    // CLR: java.util.AbstractMap.equals.
    private static bool MapEquals(IDictionary<string, object> a, IDictionary<string, object> b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (KeyValuePair<string, object> e in a)
        {
            if (!b.TryGetValue(e.Key, out object other) || !Equals(e.Value, other))
            {
                return false;
            }
        }

        return true;
    }

    // CLR: java.util.AbstractMap.toString.
    internal static string MapToString(IDictionary<string, object> map)
    {
        if (map == null)
        {
            return "null";
        }

        StringBuilder sb = new StringBuilder("{");
        bool first = true;
        foreach (KeyValuePair<string, object> e in map)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append(e.Key).Append('=').Append(e.Value == null ? "null" : e.Value.ToString());
        }

        return sb.Append('}').ToString();
    }
}
