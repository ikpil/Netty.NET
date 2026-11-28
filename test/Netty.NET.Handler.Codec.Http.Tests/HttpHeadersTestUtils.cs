/*
 * Copyright 2015 The Netty Project
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
using System.Diagnostics;
using System.Text;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.StringUtil;

namespace Netty.NET.Handler.Codec.Http.Tests;

/**
 * Utility methods for {@link HttpHeaders} related unit tests.
 */
// CLR: the final class with a private constructor is a static class. The enum HeaderValue has a constructor, fields
// and methods, so it is a sealed class with static readonly instances (docs/codec-base-porting.md, as Base64Dialect);
// values() is the VALUES array in declaration order and ToString() returns the Java toString() value. The Strings that
// Java stores in List<CharSequence> and returns from of(String) are wrapped in the Common StringCharSequence, because a
// C# string is not an ICharSequence (as the codec-stomp tests do); the lists are IList<ICharSequence>. COMMA and
// DOUBLE_QUOTE are the Common StringUtil char constants; assert is Debug.Assert.
public static class HttpHeadersTestUtils
{
    internal sealed class HeaderValue
    {
        public static readonly HeaderValue UNKNOWN = new HeaderValue("Unknown", 0);
        public static readonly HeaderValue ONE = new HeaderValue("One", 1);
        public static readonly HeaderValue TWO = new HeaderValue("Two", 2);
        public static readonly HeaderValue THREE = new HeaderValue("Three", 3);
        public static readonly HeaderValue FOUR = new HeaderValue("Four", 4);
        public static readonly HeaderValue FIVE = new HeaderValue("Five", 5);
        public static readonly HeaderValue SIX_QUOTED = new HeaderValue("Six,", 6);
        public static readonly HeaderValue SEVEN_QUOTED = new HeaderValue("Seven; , GMT", 7);
        public static readonly HeaderValue EIGHT = new HeaderValue("Eight", 8);

        private static readonly HeaderValue[] VALUES =
        {
            UNKNOWN, ONE, TWO, THREE, FOUR, FIVE, SIX_QUOTED, SEVEN_QUOTED, EIGHT
        };

        private readonly int nr;
        private readonly string value;
        private IList<ICharSequence> array;

        private HeaderValue(string value, int nr)
        {
            this.nr = nr;
            this.value = value;
        }

        public override string ToString()
        {
            return value;
        }

        public IList<ICharSequence> AsList()
        {
            if (array == null)
            {
                IList<ICharSequence> list = new List<ICharSequence>(nr);

                for (int i = 1; i <= nr; i++)
                {
                    list.Add(new StringCharSequence(Of(i).ToString()));
                }

                array = list;
            }

            return array;
        }

        public IList<ICharSequence> Subset(int from)
        {
            Debug.Assert(from > 0);
            --from;
            int size = nr - from;
            int end = from + size;
            IList<ICharSequence> list = new List<ICharSequence>(size);
            IList<ICharSequence> fullList = AsList();

            for (int i = from; i < end; ++i)
            {
                list.Add(fullList[i]);
            }

            return list;
        }

        public string SubsetAsCsvString(int from)
        {
            IList<ICharSequence> subset = Subset(from);
            return AsCsv(subset);
        }

        public string AsCsv(IList<ICharSequence> arr)
        {
            if (arr == null || arr.Count == 0)
            {
                return "";
            }

            StringBuilder sb = new StringBuilder(arr.Count * 10);
            int end = arr.Count - 1;

            for (int i = 0; i < end; ++i)
            {
                Quoted(sb, arr[i]).Append(COMMA);
            }

            Quoted(sb, arr[end]);

            return sb.ToString();
        }

        public ICharSequence AsCsv()
        {
            return new StringCharSequence(AsCsv(AsList()));
        }

        private static StringBuilder Quoted(StringBuilder sb, ICharSequence value)
        {
            if (Contains(value, COMMA) && !Contains(value, DOUBLE_QUOTE))
            {
                return sb.Append(DOUBLE_QUOTE).Append(value).Append(DOUBLE_QUOTE);
            }

            return sb.Append(value);
        }

        private static bool Contains(ICharSequence value, char c)
        {
            for (int i = 0; i < value.Length(); ++i)
            {
                if (value.CharAt(i) == c)
                {
                    return true;
                }
            }

            return false;
        }

        private static readonly Dictionary<int, HeaderValue> MAP;

        static HeaderValue()
        {
            Dictionary<int, HeaderValue> map = new Dictionary<int, HeaderValue>();

            foreach (HeaderValue v in VALUES)
            {
                int nr = v.nr;
                map[nr] = v;
            }

            MAP = map;
        }

        public static HeaderValue Of(int nr)
        {
            return MAP.TryGetValue(nr, out HeaderValue v) ? v : UNKNOWN;
        }
    }

    public static ICharSequence Of(string s)
    {
        return new StringCharSequence(s);
    }
}
