// CLR: test-only stand-in for org.apache.directory.server.dns.DnsServer of apacheds-protocol-dns 2.0.0.AM27, with the
// transport members of its base classes DirectoryBackedService and AbstractProtocolService (apacheds-protocol-shared):
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/DnsServer.java
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-shared/src/main/java/org/apache/directory/server/protocol/shared/AbstractProtocolService.java
// Kept: the protected transports set (a HashSet), setTransports(Transport...) that adds every transport and calls
// init() on one without an acceptor, getTransports() as an array copy of the set, stop() that disposes the acceptor
// of every transport, and toString. Java's HashSet of identity-hashed transports has no defined order, nor has the
// BCL HashSet (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/HashSet.html,
// https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.hashset-1?view=net-10.0); with one transport,
// as TestDnsServer starts it, getTransports()[0] is that transport on both. start() reads the records from the LDAP
// directory (JndiRecordStoreImpl over getDirectoryService()), which the stand-in does not have, so the virtual Start()
// throws NotSupportedException and TestDnsServer overrides it, as it does in Java. The service id/name, search base and
// directory service of DirectoryBackedService/AbstractProtocolService and serialVersionUID are not reproduced; the
// SLF4J logger is Common's InternalLoggerFactory logger.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

/**
 * Contains the configuration parameters for the DNS protocol provider.
 */
public class DnsServer
{
    /** logger for this class */
    private static readonly IInternalLogger LOG = InternalLoggerFactory.GetInstance(typeof(DnsServer));

    /** The default service name. */
    private const string SERVICE_NAME_DEFAULT = "ApacheDS DNS Service";

    /** The service transports. We may have more than one */
    protected ISet<ITransport> transports = new HashSet<ITransport>();

    /**
     * Creates a new instance of DnsConfiguration.
     */
    public DnsServer()
    {
    }

    /**
     * @throws IOException if we cannot bind to the specified ports
     */
    public virtual void Start()
    {
        throw new NotSupportedException(
            "DnsServer.start() reads the records from an LDAP directory (JndiRecordStoreImpl), which the stand-in " +
            "does not have; override Start() as TestDnsServer does.");
    }

    public virtual void Stop()
    {
        foreach (ITransport transport in GetTransports())
        {
            IIoAcceptor acceptor = transport.GetAcceptor();

            if (acceptor != null)
            {
                acceptor.Dispose();
            }
        }

        LOG.Info("DNS service stopped.");
    }

    /**
     * @return the transport
     */
    public ITransport[] GetTransports()
    {
        return transports.ToArray();
    }

    /**
     * Set the underlying transports
     * @param transports The transports
     */
    public void SetTransports(params ITransport[] transports)
    {
        foreach (ITransport transport in transports)
        {
            this.transports.Add(transport);

            if (transport.GetAcceptor() == null)
            {
                transport.Init();
            }
        }
    }

    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();

        sb.Append("DNSServer[").Append(SERVICE_NAME_DEFAULT).Append("], listening on :").Append('\n');

        foreach (ITransport transport in GetTransports())
        {
            sb.Append("    ").Append(transport).Append('\n');
        }

        return sb.ToString();
    }
}
