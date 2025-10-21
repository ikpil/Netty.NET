namespace Netty.NET.Common.Functional;

/**
 * A supplier which always returns {@code false} and never throws.
 */
public class FalseSupplier : ISupplier<bool>
{
    public static readonly FalseSupplier INSTANCE = new FalseSupplier();

    private FalseSupplier()
    {
    }

    public bool get()
    {
        return false;
    }
}