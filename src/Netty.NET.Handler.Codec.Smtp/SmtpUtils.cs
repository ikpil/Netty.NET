/*
 * Copyright 2016 The Netty Project
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
using System.Collections.ObjectModel;
using System.Text;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Smtp;

// CLR: the package-private final utility class is an internal static class. CharSequence is ICharSequence, the
// CharSequence port of Common (AsciiString.Of(ICharSequence)); java.lang.String, a CharSequence in Java, is passed as
// its ICharSequence wrapper StringCharSequence. Collections.emptyList() is ReadOnlyCollection<T>.Empty and
// Collections.unmodifiableList(Arrays.asList(array)) is Array.AsReadOnly(array), a read-only view of the array as in
// Java. IllegalArgumentException is ArgumentException (docs/codec-base-porting.md, Shared CLR adaptations).
internal static class SmtpUtils
{

    internal static IList<ICharSequence> ToUnmodifiableList(params ICharSequence[] sequences)
    {
        if (sequences == null || sequences.Length == 0)
        {
            return ReadOnlyCollection<ICharSequence>.Empty;
        }

        return Array.AsReadOnly(sequences);
    }

    /**
     * Validates SMTP parameters to prevent SMTP command injection.
     * Throws IllegalArgumentException if any parameter contains CRLF sequences.
     */
    internal static void ValidateSMTPParameters(params ICharSequence[] parameters)
    {
        if (parameters != null)
        {
            foreach (ICharSequence parameter in parameters)
            {
                if (parameter != null)
                {
                    ValidateSMTPParameter(parameter);
                }
            }
        }
    }

    /**
     * Validates SMTP parameters to prevent SMTP command injection.
     * Throws IllegalArgumentException if any parameter contains CRLF sequences.
     */
    internal static void ValidateSMTPParameters(IList<ICharSequence> parameters)
    {
        if (parameters != null)
        {
            foreach (ICharSequence parameter in parameters)
            {
                if (parameter != null)
                {
                    ValidateSMTPParameter(parameter);
                }
            }
        }
    }

    // CLR: the java.lang.String branch is the StringCharSequence branch (the wrapper of a CLR string); ICharSequence
    // has IndexOf(char, int start = 0), Length() and CharAt(int).
    internal static void ValidateSMTPParameter(ICharSequence parameter)
    {
        if (parameter is StringCharSequence)
        {
            StringCharSequence paramStr = (StringCharSequence) parameter;
            if (paramStr.IndexOf('\r') != -1 || paramStr.IndexOf('\n') != -1)
            {
                throw new ArgumentException("SMTP parameter contains CRLF characters: " + parameter);
            }
        }
        else
        {
            for (int i = 0; i < parameter.Length(); i++)
            {
                char c = parameter.CharAt(i);
                if (c == '\r' || c == '\n')
                {
                    throw new ArgumentException("SMTP parameter contains CRLF characters: " + parameter);
                }
            }
        }
    }

    // CLR: the List<CharSequence> fields of DefaultSmtpRequest/DefaultSmtpResponse rely on java.util.List equality,
    // hashing and text: two lists are equal if "both lists have the same size, and all corresponding pairs of elements
    // in the two lists are equal", the hash is "int hashCode = 1; for (E e : list) hashCode = 31*hashCode + (e==null ?
    // 0 : e.hashCode());" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/List.html), and
    // toString() lists the elements "enclosed in square brackets" separated by ", ", each "as by
    // String.valueOf(Object)" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/AbstractCollection.html).
    // ReadOnlyCollection<T> inherits Object.Equals/GetHashCode/ToString, so these helpers give the List behaviour.
    internal static bool ListEquals(IList<ICharSequence> a, IList<ICharSequence> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (!Equals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    internal static int ListHashCode(IList<ICharSequence> list)
    {
        int hashCode = 1;

        foreach (ICharSequence e in list)
        {
            hashCode = 31 * hashCode + (e == null ? 0 : e.GetHashCode());
        }

        return hashCode;
    }

    internal static string ListToString(IList<ICharSequence> list)
    {
        StringBuilder sb = new StringBuilder("[");

        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(list[i] == null ? "null" : list[i].ToString());
        }

        return sb.Append(']').ToString();
    }
}
