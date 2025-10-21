namespace Netty.NET.Common.Functional;

/**
 * Represents a supplier of {@code bool}-valued results.
 */
public interface IBooleanSupplier
{
    /**
     * Gets a bool value.
     * @return a bool value.
     * @throws Exception If an exception occurs.
     */
    bool get();

}