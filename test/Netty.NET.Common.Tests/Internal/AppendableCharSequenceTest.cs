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
    public void TestSimpleAppend()
    {
        TestSimpleAppend0(new AppendableCharSequence(128));
    }

    [Fact]
    public void TestAppendString()
    {
        TestAppendString0(new AppendableCharSequence(128));
    }

    [Fact]
    public void TestAppendAppendableCharSequence()
    {
        AppendableCharSequence seq = new AppendableCharSequence(128);

        string text = "testdata";
        AppendableCharSequence seq2 = new AppendableCharSequence(128);
        seq2.Append(text);
        seq.Append(seq2);

        Assert.Equal(text, seq.ToString());
        Assert.Equal(text[1..(text.Length - 2)], seq.Substring(1, text.Length - 2));

        AssertEqualChars(text, seq);
    }

    [Fact]
    public void TestSimpleAppendWithExpand()
    {
        TestSimpleAppend0(new AppendableCharSequence(2));
    }

    [Fact]
    public void TestAppendStringWithExpand()
    {
        TestAppendString0(new AppendableCharSequence(2));
    }

    [Fact]
    public void TestSubSequence()
    {
        AppendableCharSequence master = new AppendableCharSequence(26);
        master.Append("abcdefghijlkmonpqrstuvwxyz");
        Assert.Equal("abcdefghij", master.SubSequence(0, 10).ToString());
    }

    [Fact]
    public void TestEmptySubSequence()
    {
        AppendableCharSequence master = new AppendableCharSequence(26);
        master.Append("abcdefghijlkmonpqrstuvwxyz");
        AppendableCharSequence sub = master.SubSequence(0, 0);
        Assert.Equal(0, sub.Length());
        sub.Append('b');
        Assert.Equal('b', sub.CharAt(0));
    }

    private static void TestSimpleAppend0(AppendableCharSequence seq)
    {
        string text = "testdata";
        for (int i = 0; i < text.Length; i++)
        {
            seq.Append(text[i]);
        }

        Assert.Equal(text, seq.ToString());
        Assert.Equal(text[1..(text.Length - 2)], seq.Substring(1, text.Length - 2));

        AssertEqualChars(text, seq);

        seq.Reset();
        Assert.Equal(0, seq.Length());
    }

    private static void TestAppendString0(AppendableCharSequence seq)
    {
        string text = "testdata";
        seq.Append(text);

        Assert.Equal(text, seq.ToString());
        Assert.Equal(text[1..(text.Length - 2)], seq.Substring(1, text.Length - 2));

        AssertEqualChars(text, seq);

        seq.Reset();
        Assert.Equal(0, seq.Length());
    }

    private static void AssertEqualChars(string seq1, ICharSequence seq2)
    {
        Assert.Equal(seq1.Length, seq2.Length());
        for (int i = 0; i < seq1.Length; i++)
        {
            Assert.Equal(seq1[i], seq2.CharAt(i));
        }
    }
}