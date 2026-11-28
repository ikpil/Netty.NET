/*
 * Copyright 2012 The Netty Project
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
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http.Multipart;

// CLR: A Java String is a CharSequence, so the same comparator orders the String keys of the decoders' TreeMaps; a
// C# string is not an ICharSequence, so the class also implements IComparer<string> with the same loop over the
// string's chars. Character.toUpperCase(char)/toLowerCase(char) "using case mapping information from the
// UnicodeData file" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Character.html) are
// char.ToUpperInvariant/ ToLowerInvariant, which use "the casing rules of the invariant culture"
// (https://learn.microsoft.com/en-us/dotnet/api/system.char.toupperinvariant?view=net-10.0). Serializable,
// serialVersionUID and readResolve() are JVM serialization members without a CLR counterpart; the single INSTANCE
// is kept by the private constructor.
internal sealed class CaseIgnoringComparator : IComparer<ICharSequence>, IComparer<string>
{

    internal static readonly CaseIgnoringComparator INSTANCE = new CaseIgnoringComparator();

    private CaseIgnoringComparator()
    {
    }

    public int Compare(ICharSequence o1, ICharSequence o2)
    {
        int o1Length = o1.Length();
        int o2Length = o2.Length();
        int min = Math.Min(o1Length, o2Length);

        for (int i = 0; i < min; i++)
        {
            char c1 = o1.CharAt(i);
            char c2 = o2.CharAt(i);
            if (c1 != c2)
            {
                c1 = char.ToUpperInvariant(c1);
                c2 = char.ToUpperInvariant(c2);
                if (c1 != c2)
                {
                    c1 = char.ToLowerInvariant(c1);
                    c2 = char.ToLowerInvariant(c2);
                    if (c1 != c2)
                    {
                        return c1 - c2;
                    }
                }
            }
        }

        return o1Length - o2Length;
    }

    // CLR: compare(CharSequence, CharSequence) for String keys (see the class comment).
    public int Compare(string o1, string o2)
    {
        int o1Length = o1.Length;
        int o2Length = o2.Length;
        int min = Math.Min(o1Length, o2Length);

        for (int i = 0; i < min; i++)
        {
            char c1 = o1[i];
            char c2 = o2[i];
            if (c1 != c2)
            {
                c1 = char.ToUpperInvariant(c1);
                c2 = char.ToUpperInvariant(c2);
                if (c1 != c2)
                {
                    c1 = char.ToLowerInvariant(c1);
                    c2 = char.ToLowerInvariant(c2);
                    if (c1 != c2)
                    {
                        return c1 - c2;
                    }
                }
            }
        }

        return o1Length - o2Length;
    }
}
