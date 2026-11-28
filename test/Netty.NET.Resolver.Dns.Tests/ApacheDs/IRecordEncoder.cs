// Test stand-in for ApacheDS 2.0.0.AM27's RecordEncoder.
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/io/encoder/RecordEncoder.java

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
