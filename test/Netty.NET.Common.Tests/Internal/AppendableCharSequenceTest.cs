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

using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Internal;

public class AppendableCharSequenceTest
{
    [Fact]
    public void testSimpleAppend()
    {
        testSimpleAppend0(new AppendableCharSequence(128));
    }

    [Fact]
    public void testAppendString()
    {
        testAppendString0(new AppendableCharSequence(128));
    }

    [Fact]
    public void testAppendAppendableCharSequence()
    {
        AppendableCharSequence seq = new AppendableCharSequence(128);

        string text = "testdata";
        AppendableCharSequence seq2 = new AppendableCharSequence(128);
        seq2.append(text);
        seq.append(seq2);

        Assert.Equal(text, seq.ToString());
        Assert.Equal(text.substring(1, text.length() - 2), seq.substring(1, text.length() - 2));

        AssertEqualChars(text, seq);
    }

    [Fact]
    public void testSimpleAppendWithExpand()
    {
        testSimpleAppend0(new AppendableCharSequence(2));
    }

    [Fact]
    public void testAppendStringWithExpand()
    {
        testAppendString0(new AppendableCharSequence(2));
    }

    [Fact]
    public void testSubSequence()
    {
        AppendableCharSequence master = new AppendableCharSequence(26);
        master.append("abcdefghijlkmonpqrstuvwxyz");
        Assert.Equal("abcdefghij", master.subSequence(0, 10).ToString());
    }

    [Fact]
    public void testEmptySubSequence()
    {
        AppendableCharSequence master = new AppendableCharSequence(26);
        master.append("abcdefghijlkmonpqrstuvwxyz");
        AppendableCharSequence sub = master.subSequence(0, 0);
        Assert.Equal(0, sub.length());
        sub.append('b');
        Assert.Equal('b', sub.charAt(0));
    }

    private static void testSimpleAppend0(AppendableCharSequence seq)
    {
        string text = "testdata";
        for (int i = 0; i < text.length(); i++)
        {
            seq.append(text.charAt(i));
        }

        Assert.Equal(text, seq.ToString());
        Assert.Equal(text.substring(1, text.length() - 2), seq.substring(1, text.length() - 2));

        AssertEqualChars(text, seq);

        seq.reset();
        Assert.Equal(0, seq.length());
    }

    private static void testAppendString0(AppendableCharSequence seq)
    {
        string text = "testdata";
        seq.append(text);

        Assert.Equal(text, seq.ToString());
        Assert.Equal(text.substring(1, text.length() - 2), seq.substring(1, text.length() - 2));

        AssertEqualChars(text, seq);

        seq.reset();
        Assert.Equal(0, seq.length());
    }

    private static void AssertEqualChars(string seq1, ICharSequence seq2)
    {
        Assert.Equal(seq1.length(), seq2.length());
        for (int i = 0; i < seq1.length(); i++)
        {
            Assert.Equal(seq1.charAt(i), seq2.charAt(i));
        }
    }
}