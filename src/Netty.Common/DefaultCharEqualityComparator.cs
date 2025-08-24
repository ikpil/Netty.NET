namespace Netty.NET.Common;

public class DefaultCharEqualityComparator : CharEqualityComparator
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