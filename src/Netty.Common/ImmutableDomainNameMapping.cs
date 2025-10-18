using System;
using System.Collections.Generic;
using System.Text;

namespace Netty.NET.Common;

/**
 * Immutable mapping from domain name pattern to its associated value object.
 * Mapping is represented by two arrays: keys and values. Key domainNamePatterns[i] is associated with values[i].
 *
 * @param <V> concrete type of value objects
 */
public class ImmutableDomainNameMapping<T> : DomainNameMapping<T> where T : class
{
    private static readonly string REPR_HEADER = "ImmutableDomainNameMapping(default: ";
    private static readonly string REPR_MAP_OPENING = ", map: {";
    private static readonly string REPR_MAP_CLOSING = "})";

    private static readonly int REPR_CONST_PART_LENGTH =
        REPR_HEADER.Length + REPR_MAP_OPENING.Length + REPR_MAP_CLOSING.Length;

    private readonly string[] _domainNamePatterns;
    private readonly T[] _values;
    private readonly IDictionary<string, T> _map;

    public ImmutableDomainNameMapping(T defaultValue, IDictionary<string, T> map)
        : base(null, defaultValue)
    {
        var mappings = map;
        int numberOfMappings = mappings.Count;
        _domainNamePatterns = new string[numberOfMappings];
        _values = new T[numberOfMappings];

        IDictionary<string, T> mapCopy = new LinkedHashMap<string, T>(mappings.Count);
        int index = 0;
        foreach (var mapping in map)
        {
            string hostname = normalizeHostname(mapping.Key);
            T value = mapping.Value;
            _domainNamePatterns[index] = hostname;
            _values[index] = value;
            mapCopy.Add(hostname, value);
            ++index;
        }

        _map = mapCopy;
    }

    public override DomainNameMapping<T> add(string hostname, T output)
    {
        throw new NotSupportedException("Immutable DomainNameMapping does not support modification after initial creation");
    }

    public override T map(string hostname)
    {
        if (hostname != null)
        {
            hostname = normalizeHostname(hostname);

            int length = _domainNamePatterns.Length;
            for (int index = 0; index < length; ++index)
            {
                if (matches(_domainNamePatterns[index], hostname))
                {
                    return _values[index];
                }
            }
        }

        return _defaultValue;
    }

    public override IDictionary<string, T> asMap()
    {
        return _map;
    }

    public override string ToString()
    {
        string defaultValueStr = _defaultValue.ToString();

        int numberOfMappings = _domainNamePatterns.Length;
        if (numberOfMappings == 0)
        {
            return REPR_HEADER + defaultValueStr + REPR_MAP_OPENING + REPR_MAP_CLOSING;
        }

        string pattern0 = _domainNamePatterns[0];
        string value0 = _values[0].ToString();
        int oneMappingLength = pattern0.Length + value0.Length + 3; // 2 for separator ", " and 1 for '='
        int estimatedBufferSize = estimateBufferSize(defaultValueStr.Length, numberOfMappings, oneMappingLength);

        StringBuilder sb = new StringBuilder(estimatedBufferSize)
            .Append(REPR_HEADER).Append(defaultValueStr).Append(REPR_MAP_OPENING);

        appendMapping(sb, pattern0, value0);
        for (int index = 1; index < numberOfMappings; ++index)
        {
            sb.Append(", ");
            appendMapping(sb, index);
        }

        return sb.Append(REPR_MAP_CLOSING).ToString();
    }

    /**
     * Estimates the length of string representation of the given instance:
     * est = lengthOfConstantComponents + defaultValueLength + (estimatedMappingLength * numOfMappings) * 1.10
     *
     * @param defaultValueLength     length of string representation of {@link #defaultValue}
     * @param numberOfMappings       number of mappings the given instance holds,
     *                               e.g. {@link #domainNamePatterns#length}
     * @param estimatedMappingLength estimated size taken by one mapping
     * @return estimated length of string returned by {@link #ToString()}
     */
    private static int estimateBufferSize(int defaultValueLength,
        int numberOfMappings,
        int estimatedMappingLength)
    {
        return REPR_CONST_PART_LENGTH + defaultValueLength
                                      + (int)(estimatedMappingLength * numberOfMappings * 1.10);
    }

    private StringBuilder appendMapping(StringBuilder sb, int mappingIndex)
    {
        return appendMapping(sb, _domainNamePatterns[mappingIndex], _values[mappingIndex].ToString());
    }

    private static StringBuilder appendMapping(StringBuilder sb, string domainNamePattern, string value)
    {
        return sb.Append(domainNamePattern).Append('=').Append(value);
    }
}