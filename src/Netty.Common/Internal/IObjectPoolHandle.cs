namespace Netty.NET.Common.Internal;

/**
 * Handle for an pooled {@link object} that will be used to notify the {@link ObjectPool} once it can
 * reuse the pooled {@link object} again.
 * @param <T>
 */
public interface IObjectPoolHandle<T>
{
    /**
     * Recycle the {@link object} if possible and so make it ready to be reused.
     */
    void recycle(T self);
}