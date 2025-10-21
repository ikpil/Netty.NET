namespace Netty.NET.Common.Concurrent;

/**
 * Chooses the next {@link IEventExecutor} to use.
 */
public interface IEventExecutorChooser
{
    /**
     * Returns the new {@link IEventExecutor} to use.
     */
    IEventExecutor next();
}