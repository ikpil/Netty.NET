namespace Netty.NET.Common.Internal.Logging;

/**
 * Logger factory which creates an
 * <a href="https://commons.apache.org/logging/">Apache Commons Logging</a>
 * logger.
 *
 * @deprecated Please use {@link Log4J2LoggerFactory} or {@link Log4JLoggerFactory} or
 * {@link Slf4JLoggerFactory}.
 */
public class CommonsLoggerFactory : InternalLoggerFactory
{
    public static readonly InternalLoggerFactory INSTANCE = new CommonsLoggerFactory();

    /**
     * @deprecated Use {@link #INSTANCE} instead.
     */
    public CommonsLoggerFactory()
    {
    }

    public override IInternalLogger newInstance(string name)
    {
        // TODO : ...?
        return new CommonsLogger(null, name);
    }
}