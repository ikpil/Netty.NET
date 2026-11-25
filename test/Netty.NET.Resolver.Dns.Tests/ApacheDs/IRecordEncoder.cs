// CLR: test-only stand-in for org.apache.directory.server.dns.io.encoder.RecordEncoder of apacheds-protocol-dns
// 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/io/encoder/RecordEncoder.java
// The interface takes the I prefix; "throws IOException" is only documented (C# has no checked exceptions).

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

public interface IRecordEncoder
{
    /**
     * Encodes the {@link ResourceRecord} into the {@link IoBuffer}.
     *
     * @param buffer
     * @param record
     * @throws IOException
     */
    void Put(IoBuffer buffer, IResourceRecord record);
}
