namespace Netty.NET.Common;

public class AsciiCaseInsensitiveCharEqualityComparator : CharEqualityComparator 
{
    public static readonly AsciiCaseInsensitiveCharEqualityComparator INSTANCE = new AsciiCaseInsensitiveCharEqualityComparator();
    private AsciiCaseInsensitiveCharEqualityComparator() { }

    public bool equals(char a, char b) {
        return equalsIgnoreCase(a, b);
    }
}
