namespace Netty.NET.Common.Functional;

/**
 * Represents a supplier of {@code boolean}-valued results.
 */
public interface IBooleanSupplier
{
    /**
     * Gets a boolean value.
     * @return a boolean value.
     * @throws Exception If an exception occurs.
     */
    bool get();

}