namespace Netty.NET.Common;

/**
* A {@link IByteProcessor} which finds the first appearance of a specific byte.
*/
public sealed class IndexOfProcessor : IByteProcessor
{
    private readonly byte byteToFind;

    public IndexOfProcessor(byte byteToFind)
    {
        this.byteToFind = byteToFind;
    }

    public bool process(byte value)
    {
        return value != byteToFind;
    }
}