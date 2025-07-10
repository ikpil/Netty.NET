namespace Netty.NET.Common;

/**
 * A {@link HashingStrategy} which delegates to java's {@link object#hashCode()}
 * and {@link object#equals(object)}.
 */
public class DefaultHashingStrategy<T> : IHashingStrategy<T>
{
    public int GetHashCode(T obj)
    {
        return obj.GetHashCode();
    }

    public int hashCode(T obj)
    {
        return obj != null ? GetHashCode(obj) : 0;
    }

    public bool Equals(T a, T b)
    {
        return ReferenceEquals(a, b) || (!ReferenceEquals(a, null) && a.Equals(b));
    }
}