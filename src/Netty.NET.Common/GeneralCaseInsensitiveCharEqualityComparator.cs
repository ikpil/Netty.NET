namespace Netty.NET.Common;

public class GeneralCaseInsensitiveCharEqualityComparator : ICharEqualityComparator
{
    public static readonly GeneralCaseInsensitiveCharEqualityComparator INSTANCE = new GeneralCaseInsensitiveCharEqualityComparator();
    private GeneralCaseInsensitiveCharEqualityComparator() { }

    public bool equals(char a, char b)
    {
        //For motivation, why we need two checks, see comment in string#regionMatches
        return char.ToUpperInvariant(a) == char.ToUpperInvariant(b) ||
               char.ToLowerInvariant(a) == char.ToLowerInvariant(b);
    }
}