namespace Netty.NET.Common.Internal;

/**
 * Creates a new object which references the given {@link Handle} and calls {@link Handle#recycle(object)} once
 * it can be re-used.
 *
 * @param <T> the type of the pooled object
 */
public interface IObjectCreator<T>
{
    /**
     * Creates an returns a new {@link object} that can be used and later recycled via
     * {@link Handle#recycle(object)}.
     *
     * @param handle can NOT be null.
     */
    T newObject(IObjectPoolHandle<T> handle);
}