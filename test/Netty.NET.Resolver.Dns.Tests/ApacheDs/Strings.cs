// CLR: test-only stand-in for the two members of org.apache.directory.api.util.Strings (Apache Directory LDAP API
// 2.1.5, the version apacheds 2.0.0.AM27 pins as org.apache.directory.api.version in
// https://github.com/apache/directory-server/blob/2.0.0.AM27/pom.xml) that apacheds-protocol-dns 2.0.0.AM27 calls:
// https://github.com/apache/directory-ldap-api/blob/2.1.5/util/src/main/java/org/apache/directory/api/util/Strings.java
// toLowerCaseAscii maps every char through the 256-entry TO_LOWER_CASE table (only 'A'..'Z' change, a null or empty
// value gives "", and a char above 0xFF indexes past the table, IndexOutOfRangeException here as
// ArrayIndexOutOfBoundsException there); isEmpty is null or zero length. The static class stands for the final
// utility class with a private constructor.

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

internal static class Strings
{
    private static readonly char[] TO_LOWER_CASE = CreateToLowerCase();

    private static char[] CreateToLowerCase()
    {
        char[] table = new char[256];

        for (int i = 0; i < table.Length; i++)
        {
            table[i] = i >= 'A' && i <= 'Z' ? (char)(i + ('a' - 'A')) : (char)i;
        }

        return table;
    }

    public static bool IsEmpty(string str)
    {
        return str == null || str.Length == 0;
    }

    public static string ToLowerCaseAscii(string value)
    {
        if (null == value || value.Length == 0)
        {
            return "";
        }

        char[] chars = value.ToCharArray();

        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = TO_LOWER_CASE[chars[i]];
        }

        return new string(chars);
    }
}
