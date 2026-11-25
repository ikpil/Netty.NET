// CLR: test-only stand-in for org.apache.mina.filter.codec.ProtocolDecoderOutput of Apache MINA 2.2.3:
// https://github.com/apache/mina/blob/2.2.3/mina-core/src/main/java/org/apache/mina/filter/codec/ProtocolDecoderOutput.java
// The interface takes the I prefix; NextFilter is IIoFilter.INextFilter.

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

/**
 * Callback for {@link ProtocolDecoder} to generate decoded messages.
 * {@link ProtocolDecoder} must call {@link #write(Object)} for each decoded
 * messages.
 */
public interface IProtocolDecoderOutput
{
    /**
     * Callback for {@link ProtocolDecoder} to generate decoded messages.
     * {@link ProtocolDecoder} must call {@link #write(Object)} for each
     * decoded messages.
     *
     * @param message the decoded message
     */
    void Write(object message);

    /**
     * Flushes all messages you wrote via {@link #write(Object)} to
     * the next filter.
     *
     * @param nextFilter the next Filter
     * @param session The current Session
     */
    void Flush(IIoFilter.INextFilter nextFilter, IIoSession session);
}
