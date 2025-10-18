using System.Collections.Generic;
using System.Text;
using Netty.NET.Common.Collections;

namespace Netty.NET.Common;

public class ImmutableDomainWildcardMapping<T> : IMapping<string, T> where T : class
{
    private static readonly string REPR_HEADER = "ImmutableDomainWildcardMapping(default: ";
    private static readonly string REPR_MAP_OPENING = ", map: ";
    private static readonly string REPR_MAP_CLOSING = ")";

    private readonly T defaultValue;
    private readonly IDictionary<string, T> _map;

    public ImmutableDomainWildcardMapping(T defaultValue, IDictionary<string, T> map)
    {
        this.defaultValue = defaultValue;
        _map = new LinkedHashMap<string, T>(map);
    }

    public T map(string hostname)
    {
        if (hostname != null)
        {
            hostname = normalize(hostname);

            // Let's try an exact match first
            if (_map.TryGetValue(hostname, out var value))
            {
                return value;
            }

            // No exact match, let's try a wildcard match.
            int idx = hostname.IndexOf('.');
            if (idx != -1)
            {
                if (_map.TryGetValue(hostname.Substring(idx), out value))
                {
                    return value;
                }
            }
        }

        return defaultValue;
    }

    public static string normalize(string hostname)
    {
        return DomainNameMapping<T>.normalizeHostname(hostname);
    }

    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append(REPR_HEADER).Append(defaultValue).Append(REPR_MAP_OPENING).Append('{');

        foreach (var entry in _map)
        {
            string hostname = entry.Key;
            if (hostname[0] == '.')
            {
                hostname = '*' + hostname;
            }

            sb.Append(hostname).Append('=').Append(entry.Value).Append(", ");
        }

        sb.Length = sb.Length - 2;
        return sb.Append('}').Append(REPR_MAP_CLOSING).ToString();
    }
}