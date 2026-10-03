namespace Netty.NET.Common;

public class AsciiCaseInsensitiveCharEqualityComparator : ICharEqualityComparator 
{
    public static readonly AsciiCaseInsensitiveCharEqualityComparator INSTANCE = new AsciiCaseInsensitiveCharEqualityComparator();
    private AsciiCaseInsensitiveCharEqualityComparator() { }

    public bool Equals(char a, char b)
    {
        return a == b || AsciiString.ToLowerCase(a) == AsciiString.ToLowerCase(b);
    }
}
