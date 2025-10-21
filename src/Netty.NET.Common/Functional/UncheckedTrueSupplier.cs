namespace Netty.NET.Common.Functional;

/**
 * A supplier which always returns {@code true} and never throws.
 */
public class UncheckedTrueSupplier : IUncheckedBooleanSupplier
{
    public static readonly UncheckedTrueSupplier INSTANCE = new UncheckedTrueSupplier();

    private UncheckedTrueSupplier()
    {
    }

    public bool get()
    {
        return true;
    }
}