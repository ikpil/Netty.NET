// CLR: test-only stand-in for org.apache.mina.core.filterchain.IoFilterChain of Apache MINA 2.2.3:
// https://github.com/apache/mina/blob/2.2.3/mina-core/src/main/java/org/apache/mina/core/filterchain/IoFilterChain.java
// The interface takes the I prefix and keeps addFirst, the only member a handler calls (DnsProtocolHandler and
// TestDnsServer add their codec in sessionCreated); the fire* methods belong to NioDatagramAcceptor's chain.

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

public interface IIoFilterChain
{
    /**
     * Adds the specified filter with the specified name at the beginning of this chain.
     *
     * @param name The filter's name
     * @param filter The filter to add
     */
    void AddFirst(string name, IIoFilter filter);
}
