namespace Netty.NET.Common.Functional;

/**
 * A supplier which always returns {@code false} and never throws.
 */
public class UncheckedFalseSupplier : IUncheckedBooleanSupplier
{
    public static readonly UncheckedFalseSupplier INSTANCE = new UncheckedFalseSupplier();

    private UncheckedFalseSupplier()
    {
    }

    public bool get()
    {
        return false;
    }
}