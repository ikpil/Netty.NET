namespace Netty.NET.Common;

/**
 * A {@link IByteProcessor} which finds the first appearance which is not of a specific byte.
 */
public sealed class IndexNotOfProcessor : IByteProcessor
{
    private readonly byte byteToNotFind;

    public IndexNotOfProcessor(byte byteToNotFind)
    {
        this.byteToNotFind = byteToNotFind;
    }

    public bool process(byte value)
    {
        return value == byteToNotFind;
    }
}