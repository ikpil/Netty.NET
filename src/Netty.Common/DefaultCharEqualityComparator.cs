namespace Netty.NET.Common;

public class DefaultCharEqualityComparator : ICharEqualityComparator
{
    public static readonly DefaultCharEqualityComparator INSTANCE = new DefaultCharEqualityComparator();

    private DefaultCharEqualityComparator()
    {
    }

    public bool equals(char a, char b)
    {
        return a == b;
    }
}