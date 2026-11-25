// CLR: test-only stand-in for org.apache.mina.filter.codec.ProtocolDecoder of Apache MINA 2.2.3:
// https://github.com/apache/mina/blob/2.2.3/mina-core/src/main/java/org/apache/mina/filter/codec/ProtocolDecoder.java
// The interface takes the I prefix; "throws Exception" is only documented. dispose(IoSession) is Dispose(IIoSession), a
// plain method because it takes the session; the in/out parameters are @in/@out.

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

/**
 * Decodes binary or protocol-specific data into higher-level message objects.
 */
public interface IProtocolDecoder
{
    /**
     * Decodes binary or protocol-specific content into higher-level message objects.
     * MINA invokes {@link #decode(IoSession, IoBuffer, ProtocolDecoderOutput)}
     * method with read data, and then the decoder implementation puts decoded
     * messages into {@link ProtocolDecoderOutput}.
     *
     * @param session The current Session
     * @param in the buffer to decode
     * @param out The {@link ProtocolDecoderOutput} that will receive the decoded message
     * @throws Exception if the read data violated protocol specification
     */
    void Decode(IIoSession session, IoBuffer @in, IProtocolDecoderOutput @out);

    /**
     * Invoked when the specified <code>session</code> is closed.
     *
     * @param session The current Session
     * @param out The {@link ProtocolDecoderOutput} that contains the decoded message
     * @throws Exception if the read data violated protocol specification
     */
    void FinishDecode(IIoSession session, IProtocolDecoderOutput @out);

    /**
     * Releases all resources related with this decoder.
     *
     * @param session The current Session
     * @throws Exception if failed to dispose all resources
     */
    void Dispose(IIoSession session);
}
