// CLR: test-only stand-in for org.apache.directory.server.dns.DnsException of apacheds-protocol-dns 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/DnsException.java
// The checked java.lang.Exception subclass is a System.Exception subclass whose message is the response code's name();
// getResponseCode() keeps the int value of ResponseCode.convert(). serialVersionUID has no counterpart.

using System;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

/**
 * The root of the DNS exception hierarchy.
 */
public class DnsException : Exception
{
    /**
     * The DNS response code associated with this exception
     */
    private readonly int responseCode;

    /**
     * Creates a DnsException with a response code.
     *
     * @param responseCode the response code associated with this DnsException
     */
    public DnsException(ResponseCode responseCode) : base(responseCode.Name())
    {
        this.responseCode = responseCode.Convert();
    }

    /**
     * Gets the protocol response code associated with this DnsException.
     *
     * @return the response code associated with this DnsException
     */
    public int GetResponseCode()
    {
        return this.responseCode;
    }
}
