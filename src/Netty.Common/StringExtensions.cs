using StringComparison = System.StringComparison;

namespace Netty.NET.Common;

public static class StringExtensions
{
    public static bool EqualsIgnoreCase(this string a, string b)
    {
        return a.Equals(b, StringComparison.InvariantCultureIgnoreCase);
    }

    public static char charAt(this string s, int index)
    {
        return s[index];
    }

    public static string substring(this string s, int start)
    {
        return s.Substring(start);
    }
    
    public static string substring(this string s, int start, int length)
    {
        return s.Substring(start, length);
    }
    
    public static int length(this string s)
    {
        return s.Length;
    }

    public static int indexOf(this string s, char c)
    {
        return s.IndexOf(c);
    }
    
    public static int indexOf(this string s, char c, int startIndex)
    {
        return s.IndexOf(c, startIndex);
    }

}