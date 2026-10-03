namespace Netty.NET.Common;

public class DefaultCharEqualityComparator : ICharEqualityComparator
{
    public static readonly DefaultCharEqualityComparator INSTANCE = new DefaultCharEqualityComparator();

    private DefaultCharEqualityComparator()
    {
    }

    public bool Equals(char a, char b)
    {
        return a == b;
    }
}