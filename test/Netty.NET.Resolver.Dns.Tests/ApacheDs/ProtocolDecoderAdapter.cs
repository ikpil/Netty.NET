// CLR: test-only stand-in for org.apache.mina.filter.codec.ProtocolDecoderAdapter of Apache MINA 2.2.3:
// https://github.com/apache/mina/blob/2.2.3/mina-core/src/main/java/org/apache/mina/filter/codec/ProtocolDecoderAdapter.java
// The abstract class leaves decode to subclasses (abstract here, as C# requires the interface member to be declared)
// and makes finishDecode and dispose virtual no-ops.

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

/**
 * An abstract {@link ProtocolDecoder} implementation for those who don't need
 * {@link ProtocolDecoder#finishDecode(IoSession, ProtocolDecoderOutput)} nor
 * {@link ProtocolDecoder#dispose(IoSession)} method.
 */
public abstract class ProtocolDecoderAdapter : IProtocolDecoder
{
    public abstract void Decode(IIoSession session, IoBuffer @in, IProtocolDecoderOutput @out);

    /**
     * Override this method to deal with the closed connection.
     * The default implementation does nothing.
     */
    public virtual void FinishDecode(IIoSession session, IProtocolDecoderOutput @out)
    {
        // Do nothing
    }

    /**
     * Override this method to dispose all resources related with this decoder.
     * The default implementation does nothing.
     */
    public virtual void Dispose(IIoSession session)
    {
        // Do nothing
    }
}
