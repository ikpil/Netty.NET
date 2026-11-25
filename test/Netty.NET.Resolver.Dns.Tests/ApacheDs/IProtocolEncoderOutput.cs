// CLR: test-only stand-in for org.apache.mina.filter.codec.ProtocolEncoderOutput of Apache MINA 2.2.3:
// https://github.com/apache/mina/blob/2.2.3/mina-core/src/main/java/org/apache/mina/filter/codec/ProtocolEncoderOutput.java
// The interface takes the I prefix.

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

/**
 * Callback for {@link ProtocolEncoder} to generate encoded messages such as
 * {@link IoBuffer}s. {@link ProtocolEncoder} must call {@link #write(Object)}
 * for each encoded message.
 */
public interface IProtocolEncoderOutput
{
    /**
     * Callback for {@link ProtocolEncoder} to generate an encoded message such as
     * an {@link IoBuffer}. {@link ProtocolEncoder} must call {@link #write(Object)}
     * for each encoded message.
     *
     * @param message the encoded message, typically an {@link IoBuffer} or a
     *                {@link FileRegion}.
     */
    void Write(object message);
}
