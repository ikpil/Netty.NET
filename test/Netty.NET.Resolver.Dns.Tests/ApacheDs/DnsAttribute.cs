// CLR: test-only stand-in for org.apache.directory.server.dns.store.DnsAttribute of apacheds-protocol-dns 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/store/DnsAttribute.java
// The final class with a private constructor that only holds String constants is a static class of const strings
// with the same names and values.

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

/**
 * Constants representing the DNS attribute ids as defined by the Apache DNS schema.
 */
public static class DnsAttribute
{
    /** the apachedns schema name for an apacheDnsAbstractRecord */
    public const string NAME = "apacheDnsName";

    /** the apachedns schema type for an apacheDnsAbstractRecord */
    public const string TYPE = "apacheDnsType";

    /** the apachedns schema class for an apacheDnsAbstractRecord */
    public const string CLASS = "apacheDnsClass";

    /** the apachedns schema TTL for an apacheDnsAbstractRecord */
    public const string TTL = "apacheDnsTtl";

    /** the apachedns schema apacheDnsSoaMName for an apacheDnsStartOfAuthorityRecord */
    public const string SOA_M_NAME = "apacheDnsSoaMName";

    /** the apachedns schema apacheDnsSoaRName for an apacheDnsStartOfAuthorityRecord */
    public const string SOA_R_NAME = "apacheDnsSoaRName";

    /** the apachedns schema apacheDnsSoaSerial for an apacheDnsStartOfAuthorityRecord */
    public const string SOA_SERIAL = "apacheDnsSoaSerial";

    /** the apachedns schema apacheDnsSoaRefresh for an apacheDnsStartOfAuthorityRecord */
    public const string SOA_REFRESH = "apacheDnsSoaRefresh";

    /** the apachedns schema apacheDnsSoaRetry for an apacheDnsStartOfAuthorityRecord */
    public const string SOA_RETRY = "apacheDnsSoaRetry";

    /** the apachedns schema apacheDnsSoaExpire for an apacheDnsStartOfAuthorityRecord */
    public const string SOA_EXPIRE = "apacheDnsSoaExpire";

    /** the apachedns schema apacheDnsSoaMinimum for an apacheDnsStartOfAuthorityRecord */
    public const string SOA_MINIMUM = "apacheDnsSoaMinimum";

    /** the apachedns schema apacheDnsDomainName */
    public const string DOMAIN_NAME = "apacheDnsDomainName";

    /** the apachedns schema apacheDnsIpAddress */
    public const string IP_ADDRESS = "apacheDnsIpAddress";

    /** the apachedns schema apacheDnsMxPreference */
    public const string MX_PREFERENCE = "apacheDnsMxPreference";

    /** the apachedns schema apacheDnsCharacterString */
    public const string CHARACTER_STRING = "apacheDnsCharacterString";

    /** the apachedns schema apacheDnsServicePriority */
    public const string SERVICE_PRIORITY = "apacheDnsServicePriority";

    /** the apachedns schema apacheDnsServiceWeight */
    public const string SERVICE_WEIGHT = "apacheDnsServiceWeight";

    /** the apachedns schema apacheDnsServicePort */
    public const string SERVICE_PORT = "apacheDnsServicePort";
}
