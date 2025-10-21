namespace Netty.NET.Common.Functional;

/**
 * A supplier which always returns {@code true} and never throws.
 */
public class TrueSupplier : ISupplier<bool>
{
    public static readonly TrueSupplier INSTANCE = new TrueSupplier();

    private TrueSupplier()
    {
    }

    public bool get()
    {
        return true;
    }
}