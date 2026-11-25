// CLR: test-only stand-in for org.apache.mina.filter.codec.ProtocolEncoder of Apache MINA 2.2.3:
// https://github.com/apache/mina/blob/2.2.3/mina-core/src/main/java/org/apache/mina/filter/codec/ProtocolEncoder.java
// The interface takes the I prefix; "throws Exception" is only documented. dispose(IoSession) is Dispose(IIoSession), a
// plain method because it takes the session (it is not IDisposable.Dispose()); the out parameter is @out.

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

/**
 * Encodes higher-level message objects into binary or protocol-specific data.
 */
public interface IProtocolEncoder
{
    /**
     * Encodes higher-level message objects into binary or protocol-specific data.
     * MINA invokes {@link #encode(IoSession, Object, ProtocolEncoderOutput)}
     * method with message which is popped from the session write queue, and then
     * the encoder implementation puts encoded messages (typically {@link IoBuffer}s)
     * into {@link ProtocolEncoderOutput}.
     *
     * @param session The current Session
     * @param message the message to encode
     * @param out The {@link ProtocolEncoderOutput} that will receive the encoded message
     * @throws Exception if the message violated protocol specification
     */
    void Encode(IIoSession session, object message, IProtocolEncoderOutput @out);

    /**
     * Releases all resources related with this encoder.
     *
     * @param session The current Session
     * @throws Exception if failed to dispose all resources
     */
    void Dispose(IIoSession session);
}
