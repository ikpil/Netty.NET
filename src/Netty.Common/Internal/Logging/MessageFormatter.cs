/*
 * Copyright 2013 The Netty Project
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
/**
 * Copyright (c) 2004-2011 QOS.ch
 * All rights reserved.
 *
 * Permission is hereby granted, free  of charge, to any person obtaining
 * a  copy  of this  software  and  associated  documentation files  (the
 * "Software"), to  deal in  the Software without  restriction, including
 * without limitation  the rights to  use, copy, modify,  merge, publish,
 * distribute,  sublicense, and/or sell  copies of  the Software,  and to
 * permit persons to whom the Software  is furnished to do so, subject to
 * the following conditions:
 *
 * The  above  copyright  notice  and  this permission  notice  shall  be
 * included in all copies or substantial portions of the Software.
 *
 * THE  SOFTWARE IS  PROVIDED  "AS  IS", WITHOUT  WARRANTY  OF ANY  KIND,
 * EXPRESS OR  IMPLIED, INCLUDING  BUT NOT LIMITED  TO THE  WARRANTIES OF
 * MERCHANTABILITY,    FITNESS    FOR    A   PARTICULAR    PURPOSE    AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
 * LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
 * OF CONTRACT, TORT OR OTHERWISE,  ARISING FROM, OUT OF OR IN CONNECTION
 * WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 *
 */

using System;
using System.Collections.Generic;
using System.Text;

namespace Netty.NET.Common.Internal.Logging;

// contributors: lizongbo: proposed special treatment of array parameter values
// Joern Huxhorn: pointed out double[] omission, suggested deep array copy

/**
 * Formats messages according to very simple substitution rules. Substitutions
 * can be made 1, 2 or more arguments.
 * <p/>
 * <p/>
 * For example,
 * <p/>
 * <pre>
 * MessageFormatter.format(&quot;Hi {}.&quot;, &quot;there&quot;)
 * </pre>
 * <p/>
 * will return the string "Hi there.".
 * <p/>
 * The {} pair is called the <em>formatting anchor</em>. It serves to designate
 * the location where arguments need to be substituted within the message
 * pattern.
 * <p/>
 * In case your message contains the '{' or the '}' character, you do not have
 * to do anything special unless the '}' character immediately follows '{'. For
 * example,
 * <p/>
 * <pre>
 * MessageFormatter.format(&quot;Set {1,2,3} is not equal to {}.&quot;, &quot;1,2&quot;);
 * </pre>
 * <p/>
 * will return the string "Set {1,2,3} is not equal to 1,2.".
 * <p/>
 * <p/>
 * If for whatever reason you need to place the string "{}" in the message
 * without its <em>formatting anchor</em> meaning, then you need to escape the
 * '{' character with '\', that is the backslash character. Only the '{'
 * character should be escaped. There is no need to escape the '}' character.
 * For example,
 * <p/>
 * <pre>
 * MessageFormatter.format(&quot;Set \\{} is not equal to {}.&quot;, &quot;1,2&quot;);
 * </pre>
 * <p/>
 * will return the string "Set {} is not equal to 1,2.".
 * <p/>
 * <p/>
 * The escaping behavior just described can be overridden by escaping the escape
 * character '\'. Calling
 * <p/>
 * <pre>
 * MessageFormatter.format(&quot;File name is C:\\\\{}.&quot;, &quot;file.zip&quot;);
 * </pre>
 * <p/>
 * will return the string "File name is C:\file.zip".
 * <p/>
 * <p/>
 * The formatting conventions are different than those of {@link MessageFormat}
 * which ships with the Java platform. This is justified by the fact that
 * SLF4J's implementation is 10 times faster than that of {@link MessageFormat}.
 * This local performance difference is both measurable and significant in the
 * larger context of the complete logging processing chain.
 * <p/>
 * <p/>
 * See also {@link #format(string, object)},
 * {@link #format(string, object, object)} and
 * {@link #arrayFormat(string, object[])} methods for more details.
 */
public static class MessageFormatter
{
    private static readonly char DELIM_START = '{';
    private static readonly string DELIM_STR = "{}";
    private static readonly char ESCAPE_CHAR = '\\';

    /**
     * Performs single argument substitution for the 'messagePattern' passed as
     * parameter.
     * <p/>
     * For example,
     * <p/>
     * <pre>
     * MessageFormatter.format(&quot;Hi {}.&quot;, &quot;there&quot;);
     * </pre>
     * <p/>
     * will return the string "Hi there.".
     * <p/>
     *
     * @param messagePattern The message pattern which will be parsed and formatted
     * @param arg            The argument to be substituted in place of the formatting anchor
     * @return The formatted message
     */
    public static FormattingTuple format(string messagePattern, object arg)
    {
        return arrayFormat(messagePattern, new object[] { arg });
    }

    /**
     * Performs a two argument substitution for the 'messagePattern' passed as
     * parameter.
     * <p/>
     * For example,
     * <p/>
     * <pre>
     * MessageFormatter.format(&quot;Hi {}. My name is {}.&quot;, &quot;Alice&quot;, &quot;Bob&quot;);
     * </pre>
     * <p/>
     * will return the string "Hi Alice. My name is Bob.".
     *
     * @param messagePattern The message pattern which will be parsed and formatted
     * @param argA           The argument to be substituted in place of the first formatting
     *                       anchor
     * @param argB           The argument to be substituted in place of the second formatting
     *                       anchor
     * @return The formatted message
     */
    public static FormattingTuple format(string messagePattern,
        object argA, object argB)
    {
        return arrayFormat(messagePattern, new object[] { argA, argB });
    }

    /**
     * Same principle as the {@link #format(string, object)} and
     * {@link #format(string, object, object)} methods except that any number of
     * arguments can be passed in an array.
     *
     * @param messagePattern The message pattern which will be parsed and formatted
     * @param argArray       An array of arguments to be substituted in place of formatting
     *                       anchors
     * @return The formatted message
     */
    public static FormattingTuple arrayFormat(string messagePattern, object[] argArray)
    {
        if (argArray == null || argArray.Length == 0)
        {
            return new FormattingTuple(messagePattern, null);
        }

        int lastArrIdx = argArray.Length - 1;
        object lastEntry = argArray[lastArrIdx];
        Exception throwable = lastEntry as Exception;

        if (messagePattern == null)
        {
            return new FormattingTuple(null, throwable);
        }

        int j = messagePattern.IndexOf(DELIM_STR, StringComparison.InvariantCulture);
        if (j == -1)
        {
            // this is a simple string
            return new FormattingTuple(messagePattern, throwable);
        }

        StringBuilder sbuf = new StringBuilder(messagePattern.Length + 50);
        int i = 0;
        int L = 0;
        do
        {
            bool notEscaped = j == 0 || messagePattern[j - 1] != ESCAPE_CHAR;
            if (notEscaped)
            {
                // normal case
                sbuf.Append(messagePattern, i, j);
            }
            else
            {
                sbuf.Append(messagePattern, i, j - 1);
                // check that escape char is not is escaped: "abc x:\\{}"
                notEscaped = j >= 2 && messagePattern[j - 2] == ESCAPE_CHAR;
            }

            i = j + 2;
            if (notEscaped)
            {
                deeplyAppendParameter(sbuf, argArray[L], null);
                L++;
                if (L > lastArrIdx)
                {
                    break;
                }
            }
            else
            {
                sbuf.Append(DELIM_STR);
            }

            j = messagePattern.IndexOf(DELIM_STR, i);
        } while (j != -1);

        // append the characters following the last {} pair.
        sbuf.Append(messagePattern, i, messagePattern.Length);
        return new FormattingTuple(sbuf.ToString(), L <= lastArrIdx ? throwable : null);
    }

    // special treatment of array values was suggested by 'lizongbo'
    private static void deeplyAppendParameter(StringBuilder sbuf, object o, ISet<object[]> seenSet)
    {
        if (o == null)
        {
            sbuf.Append("null");
            return;
        }

        if (!o.GetType().IsArray)
        {
            safeObjectAppend(sbuf, o);
        }
        else
        {
            // check for primitive array types because they
            // unfortunately cannot be cast to object[]
            sbuf.Append('[');
            if (o is bool[] boolArray)
            {
                booleanArrayAppend(sbuf, boolArray);
            }
            else if (o is byte[] byteArray)
            {
                byteArrayAppend(sbuf, byteArray);
            }
            else if (o is char[] charArray)
            {
                charArrayAppend(sbuf, charArray);
            }
            else if (o is short[] shortArray)
            {
                shortArrayAppend(sbuf, shortArray);
            }
            else if (o is int[] intArray)
            {
                intArrayAppend(sbuf, intArray);
            }
            else if (o is long[] longArray)
            {
                longArrayAppend(sbuf, longArray);
            }
            else if (o is float[] floatArray)
            {
                floatArrayAppend(sbuf, floatArray);
            }
            else if (o is double[] doubleArray)
            {
                doubleArrayAppend(sbuf, doubleArray);
            }
            else
            {
                objectArrayAppend(sbuf, (object[])o, seenSet);
            }

            sbuf.Append(']');
        }
    }

    private static void safeObjectAppend(StringBuilder sbuf, object o)
    {
        try
        {
            string oAsString = o.ToString();
            sbuf.Append(oAsString);
        }
        catch (Exception t)
        {
            Console.Error.WriteLine("SLF4J: Failed toString() invocation on an object of type ["
                                    + o.GetType().Name + ']');
            if (null != t.StackTrace)
            {
                Console.Error.WriteLine(t.StackTrace);
            }

            sbuf.Append("[FAILED toString()]");
        }
    }

    private static void objectArrayAppend(StringBuilder sbuf, object[] a, ISet<object[]> seenSet)
    {
        if (a.Length == 0)
        {
            return;
        }

        if (seenSet == null)
        {
            seenSet = new HashSet<object[]>(a.Length);
        }

        if (seenSet.Add(a))
        {
            deeplyAppendParameter(sbuf, a[0], seenSet);
            for (int i = 1; i < a.Length; i++)
            {
                sbuf.Append(", ");
                deeplyAppendParameter(sbuf, a[i], seenSet);
            }

            // allow repeats in siblings
            seenSet.Remove(a);
        }
        else
        {
            sbuf.Append("...");
        }
    }

    private static void booleanArrayAppend(StringBuilder sbuf, bool[] a)
    {
        if (a.Length == 0)
        {
            return;
        }

        sbuf.Append(a[0]);
        for (int i = 1; i < a.Length; i++)
        {
            sbuf.Append(", ");
            sbuf.Append(a[i]);
        }
    }

    private static void byteArrayAppend(StringBuilder sbuf, byte[] a)
    {
        if (a.Length == 0)
        {
            return;
        }

        sbuf.Append(a[0]);
        for (int i = 1; i < a.Length; i++)
        {
            sbuf.Append(", ");
            sbuf.Append(a[i]);
        }
    }

    private static void charArrayAppend(StringBuilder sbuf, char[] a)
    {
        if (a.Length == 0)
        {
            return;
        }

        sbuf.Append(a[0]);
        for (int i = 1; i < a.Length; i++)
        {
            sbuf.Append(", ");
            sbuf.Append(a[i]);
        }
    }

    private static void shortArrayAppend(StringBuilder sbuf, short[] a)
    {
        if (a.Length == 0)
        {
            return;
        }

        sbuf.Append(a[0]);
        for (int i = 1; i < a.Length; i++)
        {
            sbuf.Append(", ");
            sbuf.Append(a[i]);
        }
    }

    private static void intArrayAppend(StringBuilder sbuf, int[] a)
    {
        if (a.Length == 0)
        {
            return;
        }

        sbuf.Append(a[0]);
        for (int i = 1; i < a.Length; i++)
        {
            sbuf.Append(", ");
            sbuf.Append(a[i]);
        }
    }

    private static void longArrayAppend(StringBuilder sbuf, long[] a)
    {
        if (a.Length == 0)
        {
            return;
        }

        sbuf.Append(a[0]);
        for (int i = 1; i < a.Length; i++)
        {
            sbuf.Append(", ");
            sbuf.Append(a[i]);
        }
    }

    private static void floatArrayAppend(StringBuilder sbuf, float[] a)
    {
        if (a.Length == 0)
        {
            return;
        }

        sbuf.Append(a[0]);
        for (int i = 1; i < a.Length; i++)
        {
            sbuf.Append(", ");
            sbuf.Append(a[i]);
        }
    }

    private static void doubleArrayAppend(StringBuilder sbuf, double[] a)
    {
        if (a.Length == 0)
        {
            return;
        }

        sbuf.Append(a[0]);
        for (int i = 1; i < a.Length; i++)
        {
            sbuf.Append(", ");
            sbuf.Append(a[i]);
        }
    }
}