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

using System.Collections.Generic;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Ssl;

/**
 * Utility class for application protocol common operations.
 */
// CLR: Iterable is IEnumerable, List is IList (a List<string> instance) and String... is params string[]. The
// class is static: the private constructor only prevented instantiation.
internal static class ApplicationProtocolUtil
{
    private const int DEFAULT_LIST_SIZE = 2;

    internal static IList<string> ToList(IEnumerable<string> protocols)
    {
        return ToList(DEFAULT_LIST_SIZE, protocols);
    }

    internal static IList<string> ToList(int initialListSize, IEnumerable<string> protocols)
    {
        if (protocols == null)
        {
            return null;
        }

        List<string> result = new List<string>(initialListSize);

        foreach (string p in protocols)
        {
            result.Add(ObjectUtil.CheckNonEmpty(p, "p"));
        }

        return (IList<string>) ObjectUtil.CheckNonEmpty((ICollection<string>) result, "result");
    }

    internal static IList<string> ToList(params string[] protocols)
    {
        return ToList(DEFAULT_LIST_SIZE, protocols);
    }

    internal static IList<string> ToList(int initialListSize, params string[] protocols)
    {
        if (protocols == null)
        {
            return null;
        }

        List<string> result = new List<string>(initialListSize);

        foreach (string p in protocols)
        {
            result.Add(ObjectUtil.CheckNonEmpty(p, "p"));
        }

        return (IList<string>) ObjectUtil.CheckNonEmpty((ICollection<string>) result, "result");
    }
}
