/*
 * Copyright 2016 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: the @Disabled class has no test methods; its main(String[]) is a standalone diagnostic program. The xunit.v3
// test project generates its own entry point, and a second static Main would be CS0017 ("Program ... has more than
// one entry point defined", https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0017), so main is Main_ (trailing
// underscore, as the owner rule for name clashes). The final class with a private constructor is a static class.
// Field reflection (Class.getFields(), Field.get(null)) is Type.GetFields(BindingFlags.Public | BindingFlags.Static)
// and FieldInfo.GetValue(null); File/BufferedReader/FileReader are a StreamReader over the path; PrintStream
// System.out is Console.Out. List/Map/Set are List/Dictionary/HashSet, Collections.emptyMap() is
// ImmutableDictionary.Empty, the private Function interface is IFunction and its anonymous classes are the nested
// OldHasher and AsciiHasher; the labeled break is a goto to a label after the loop. Java string concatenation of the
// numbers uses the invariant culture.
public static class HashCollisionTest
{

    public static void Main_(string[] args)
    {
        // Big initial size for when all name sources are pulled in.
        List<ICharSequence> strings = new List<ICharSequence>(350000);
        AddHttpHeaderNames(strings);
        AddHttpHeaderValues(strings);
        AddHttp2HeaderNames(strings);
        AddWordsFromFile(new FileInfo("/usr/share/dict/words"), strings);
        // More "english words" can be found here:
        // https://gist.github.com/Scottmitch/de2f03912778016ecee3c140478f07e0#file-englishwords-txt

        IDictionary<int, List<ICharSequence>> dups = CalculateDuplicates(strings, new OldHasher());
        TextWriter writer = Console.Out;
        writer.WriteLine("==Old Duplicates==");
        PrintResults(writer, dups);

        dups = CalculateDuplicates(strings, new AsciiHasher());
        writer.WriteLine();
        writer.WriteLine("==New Duplicates==");
        PrintResults(writer, dups);
    }

    // CLR: the first anonymous Function of main.
    private sealed class OldHasher : IFunction<ICharSequence, int>
    {
        public int Apply(ICharSequence @string)
        {
            int h = 0;

            for (int i = 0; i < @string.Length(); ++i)
            {
                // masking with 0x1F reduces the number of overall bits that impact the hash code but makes the hash
                // code the same regardless of character case (upper case or lower case hash is the same).
                h = h * 31 + (@string.CharAt(i) & 0x1F);
            }

            return h;
        }
    }

    // CLR: the second anonymous Function of main.
    private sealed class AsciiHasher : IFunction<ICharSequence, int>
    {
        public int Apply(ICharSequence @string)
        {
            return PlatformDependent.HashCodeAscii(@string);
        }
    }

    private static void AddHttpHeaderNames(List<ICharSequence> values)
    {
        foreach (FieldInfo f in typeof(HttpHeaderNames).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (f.FieldType == typeof(AsciiString))
            {
                values.Add((AsciiString) f.GetValue(null));
            }
        }
    }

    private static void AddHttpHeaderValues(List<ICharSequence> values)
    {
        foreach (FieldInfo f in typeof(HttpHeaderValues).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (f.FieldType == typeof(AsciiString))
            {
                values.Add((AsciiString) f.GetValue(null));
            }
        }
    }

    private static void AddHttp2HeaderNames(List<ICharSequence> values)
    {
        foreach (IHttp2Headers.PseudoHeaderName name in IHttp2Headers.PseudoHeaderName.Values())
        {
            values.Add(name.Value());
        }
    }

    private static void AddWordsFromFile(FileInfo file, List<ICharSequence> values)
    {
        StreamReader br = new StreamReader(file.FullName);

        try
        {
            string line;

            while ((line = br.ReadLine()) != null)
            {
                // Make a "best effort" to prune input which contains characters that are not valid in HTTP header names
                if (line.IndexOf('\'') < 0)
                {
                    values.Add(new StringCharSequence(line));
                }
            }
        }
        finally
        {
            br.Close();
        }
    }

    private static IDictionary<int, List<ICharSequence>> CalculateDuplicates(List<ICharSequence> strings,
                                                                        IFunction<ICharSequence, int> hasher)
    {
        IDictionary<int, List<ICharSequence>> hashResults = new Dictionary<int, List<ICharSequence>>();
        ISet<int> duplicateHashCodes = new HashSet<int>();

        foreach (ICharSequence str in strings)
        {
            int hash = hasher.Apply(str);
            hashResults.TryGetValue(hash, out List<ICharSequence> results);

            if (results == null)
            {
                results = new List<ICharSequence>(1);
                hashResults[hash] = results;
            }
            else
            {
                duplicateHashCodes.Add(hash);
            }

            results.Add(str);
        }

        if (duplicateHashCodes.Count == 0)
        {
            return ImmutableDictionary<int, List<ICharSequence>>.Empty;
        }

        IDictionary<int, List<ICharSequence>> duplicates =
                new Dictionary<int, List<ICharSequence>>(duplicateHashCodes.Count);

        foreach (int duplicateHashCode in duplicateHashCodes)
        {
            List<ICharSequence> realDups = new List<ICharSequence>(2);
            IEnumerator<ICharSequence> itr = hashResults[duplicateHashCode].GetEnumerator();
            // there should be at least 2 elements in the list ... bcz there may be duplicates
            itr.MoveNext();
            realDups.Add(itr.Current);

            while (itr.MoveNext())
            {
                ICharSequence next = itr.Current;

                foreach (ICharSequence potentialDup in realDups)
                {
                    if (!AsciiString.ContentEqualsIgnoreCase(next, potentialDup))
                    {
                        realDups.Add(next);
                        goto checknext;
                    }
                }
            }
            checknext:

            if (realDups.Count > 1)
            {
                duplicates[duplicateHashCode] = realDups;
            }
        }

        return duplicates;
    }

    private static void PrintResults(TextWriter stream, IDictionary<int, List<ICharSequence>> dups)
    {
        stream.WriteLine("Number duplicates: " + Http2CodecUtil.ToJavaString(dups.Count));

        foreach (KeyValuePair<int, List<ICharSequence>> entry in dups)
        {
            stream.Write(Http2CodecUtil.ToJavaString(entry.Value.Count) + " duplicates for hash: " +
                         Http2CodecUtil.ToJavaString(entry.Key) + " values: ");

            foreach (ICharSequence str in entry.Value)
            {
                stream.Write("[" + str + "] ");
            }

            stream.WriteLine();
        }
    }

    private interface IFunction<P, R>
    {
        R Apply(P param);
    }
}
