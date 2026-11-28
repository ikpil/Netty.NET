
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
