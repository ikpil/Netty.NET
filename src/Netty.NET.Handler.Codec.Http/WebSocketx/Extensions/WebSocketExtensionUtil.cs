/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Netty.NET.Common.Collections;

namespace Netty.NET.Handler.Codec.Http.WebSocketx.Extensions;

// CLR: the final class with a private constructor is a public sealed class; the package-private isWebsocketUpgrade and
// computeMergeExtensionsHeaderValue are internal. List is IList (ArrayList is List, Collections.emptyList() an empty
// array), Map is IDictionary: LinkedHashMap is the Common LinkedHashMap (insertion order, put replaces the value of an
// existing key) and Collections.emptyMap() is ImmutableDictionary<string, string>.Empty
// (https://learn.microsoft.com/en-us/dotnet/api/system.collections.immutable.immutabledictionary-2.empty?view=net-10.0).
// String.split(regex) with the literal "," or ";" keeps Java's result in Split: "Trailing empty strings are therefore
// not included in the resulting array", and "If the expression does not match any part of the input then the resulting
// array has just one element, namely this string"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html#split(java.lang.String)), where
// string.Split keeps trailing empty entries. trim() removes the chars up to U+0020 and is core's HttpUtil.TrimJava.
// java.util.regex.Pattern is System.Text.RegularExpressions.Regex with the same pattern; Matcher.matches() is a match
// of the whole input (the pattern is anchored by ^ and $ and the input is trimmed), and group(n), which "returns null"
// when "the group specified failed to match any part of the input sequence"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/regex/Matcher.html), is the group's Value
// only when Group.Success is true ("true if at least one substring was captured by this group",
// https://learn.microsoft.com/en-us/dotnet/api/system.text.regularexpressions.group.success?view=net-10.0).
// StringBuilder.setLength(n) is the Length setter.
public sealed class WebSocketExtensionUtil
{

    private const string EXTENSION_SEPARATOR = ",";
    private const string PARAMETER_SEPARATOR = ";";
    private const char PARAMETER_EQUAL = '=';

    private static readonly Regex PARAMETER = new Regex("^([^=]+)(=[\\\"]?([^\\\"]+)[\\\"]?)?$");

    internal static bool IsWebsocketUpgrade(HttpHeaders headers)
    {
        //this contains check does not allocate an iterator, and most requests are not upgrades
        //so we do the contains check first before checking for specific values
        return headers.Contains(HttpHeaderNames.UPGRADE, HttpHeaderValues.WEBSOCKET, true) &&
               headers.ContainsValue(HttpHeaderNames.CONNECTION, HttpHeaderValues.UPGRADE, true);
    }

    public static IList<WebSocketExtensionData> ExtractExtensions(string extensionHeader)
    {
        string[] rawExtensions = Split(extensionHeader, EXTENSION_SEPARATOR[0]);
        if (rawExtensions.Length > 0)
        {
            IList<WebSocketExtensionData> extensions = new List<WebSocketExtensionData>(rawExtensions.Length);

            foreach (string rawExtension in rawExtensions)
            {
                string[] extensionParameters = Split(rawExtension, PARAMETER_SEPARATOR[0]);
                string name = HttpUtil.TrimJava(extensionParameters[0]);
                IDictionary<string, string> parameters;

                if (extensionParameters.Length > 1)
                {
                    parameters = new LinkedHashMap<string, string>(extensionParameters.Length - 1);

                    for (int i = 1; i < extensionParameters.Length; i++)
                    {
                        string parameter = HttpUtil.TrimJava(extensionParameters[i]);
                        Match parameterMatcher = PARAMETER.Match(parameter);
                        if (parameterMatcher.Success && parameterMatcher.Groups[1].Success)
                        {
                            parameters[parameterMatcher.Groups[1].Value] = GroupOrNull(parameterMatcher, 3);
                        }
                    }
                }
                else
                {
                    parameters = ImmutableDictionary<string, string>.Empty;
                }

                extensions.Add(new WebSocketExtensionData(name, parameters));
            }

            return extensions;
        }
        else
        {
            return Array.Empty<WebSocketExtensionData>();
        }
    }

    internal static string ComputeMergeExtensionsHeaderValue(string userDefinedHeaderValue,
                                                    IList<WebSocketExtensionData> extraExtensions)
    {
        IList<WebSocketExtensionData> userDefinedExtensions =
          userDefinedHeaderValue != null ?
            ExtractExtensions(userDefinedHeaderValue) :
            Array.Empty<WebSocketExtensionData>();

        foreach (WebSocketExtensionData userDefined in userDefinedExtensions)
        {
            WebSocketExtensionData matchingExtra = null;
            int i;

            for (i = 0; i < extraExtensions.Count; i ++)
            {
                WebSocketExtensionData extra = extraExtensions[i];
                if (extra.Name().Equals(userDefined.Name()))
                {
                    matchingExtra = extra;
                    break;
                }
            }

            if (matchingExtra == null)
            {
                extraExtensions.Add(userDefined);
            }
            else
            {
                // merge with higher precedence to user defined parameters
                IDictionary<string, string> mergedParameters =
                        new LinkedHashMap<string, string>(matchingExtra.Parameters());

                foreach (KeyValuePair<string, string> parameter in userDefined.Parameters())
                {
                    mergedParameters[parameter.Key] = parameter.Value;
                }

                extraExtensions[i] = new WebSocketExtensionData(matchingExtra.Name(), mergedParameters);
            }
        }

        StringBuilder sb = new StringBuilder(150);

        foreach (WebSocketExtensionData data in extraExtensions)
        {
            sb.Append(data.Name());

            foreach (KeyValuePair<string, string> parameter in data.Parameters())
            {
                sb.Append(PARAMETER_SEPARATOR);
                sb.Append(parameter.Key);

                if (parameter.Value != null)
                {
                    sb.Append(PARAMETER_EQUAL);
                    sb.Append(parameter.Value);
                }
            }

            sb.Append(EXTENSION_SEPARATOR);
        }

        if (extraExtensions.Count != 0)
        {
            sb.Length = sb.Length - EXTENSION_SEPARATOR.Length;
        }

        return sb.ToString();
    }

    private static string GroupOrNull(Match matcher, int group)
    {
        Group g = matcher.Groups[group];
        return g.Success ? g.Value : null;
    }

    private static string[] Split(string value, char separator)
    {
        string[] parts = value.Split(separator);
        if (parts.Length == 1)
        {
            return parts;
        }

        int length = parts.Length;

        while (length > 0 && parts[length - 1].Length == 0)
        {
            length--;
        }

        return length == parts.Length ? parts : parts.AsSpan(0, length).ToArray();
    }

    private WebSocketExtensionUtil()
    {
        // Unused
    }
}
