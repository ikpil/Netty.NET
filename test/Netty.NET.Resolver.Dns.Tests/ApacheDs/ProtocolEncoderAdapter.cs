// CLR: test-only stand-in for org.apache.mina.filter.codec.ProtocolEncoderAdapter of Apache MINA 2.2.3:
// https://github.com/apache/mina/blob/2.2.3/mina-core/src/main/java/org/apache/mina/filter/codec/ProtocolEncoderAdapter.java
// The abstract class leaves encode to subclasses (abstract here, as C# requires the interface member to be declared)
// and makes dispose a virtual no-op.

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

/**
 * An abstract {@link ProtocolEncoder} implementation for those who don't have any
 * resources to dispose.
 */
public abstract class ProtocolEncoderAdapter : IProtocolEncoder
{
    public abstract void Encode(IIoSession session, object message, IProtocolEncoderOutput @out);

    /**
     * Override this method dispose all resources related with this encoder.
     * The default implementation does nothing.
     */
    public virtual void Dispose(IIoSession session)
    {
        // Do nothing
    }
}
