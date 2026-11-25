// CLR: test-only stand-in for org.apache.mina.filter.codec.ProtocolCodecFactory of Apache MINA 2.2.3:
// https://github.com/apache/mina/blob/2.2.3/mina-core/src/main/java/org/apache/mina/filter/codec/ProtocolCodecFactory.java
// The interface takes the I prefix; "throws Exception" is only documented. ProtocolCodecFilter asks the factory for
// an encoder on every write and for a decoder on every received buffer, as MINA 2.2.3 does.

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

/**
 * Provides {@link ProtocolEncoder} and {@link ProtocolDecoder} which translates
 * binary or protocol specific data into message object and vice versa.
 */
public interface IProtocolCodecFactory
{
    /**
     * Returns a new (or reusable) instance of {@link ProtocolEncoder} which
     * encodes message objects into binary or protocol-specific data.
     *
     * @param session The current session
     * @return The encoder instance
     * @throws Exception If an error occurred while retrieving the encoder
     */
    IProtocolEncoder GetEncoder(IIoSession session);

    /**
     * Returns a new (or reusable) instance of {@link ProtocolDecoder} which
     * decodes binary or protocol-specific data into message objects.
     *
     * @param session The current session
     * @return The decoder instance
     * @throws Exception If an error occurred while retrieving the decoder
     */
    IProtocolDecoder GetDecoder(IIoSession session);
}
