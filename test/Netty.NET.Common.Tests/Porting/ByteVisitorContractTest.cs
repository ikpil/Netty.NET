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

using System;
using System.Collections.Generic;
using System.Threading;

namespace Netty.NET.Common.Tests.Porting;

public class ByteVisitorContractTest
{
    [Fact]
    public void PredefinedPredicatesMatchEveryBytePattern()
    {
        var predicates = new (Func<byte, bool> Visitor, byte[] Stops, bool Inverted)[]
        {
            (ByteProcessor.FIND_NUL, new byte[] { 0 }, false),
            (ByteProcessor.FIND_NON_NUL, new byte[] { 0 }, true),
            (ByteProcessor.FIND_CR, new byte[] { 13 }, false),
            (ByteProcessor.FIND_NON_CR, new byte[] { 13 }, true),
            (ByteProcessor.FIND_LF, new byte[] { 10 }, false),
            (ByteProcessor.FIND_NON_LF, new byte[] { 10 }, true),
            (ByteProcessor.FIND_SEMI_COLON, new byte[] { 59 }, false),
            (ByteProcessor.FIND_COMMA, new byte[] { 44 }, false),
            (ByteProcessor.FIND_ASCII_SPACE, new byte[] { 32 }, false),
            (ByteProcessor.FIND_CRLF, new byte[] { 13, 10 }, false),
            (ByteProcessor.FIND_NON_CRLF, new byte[] { 13, 10 }, true),
            (ByteProcessor.FIND_LINEAR_WHITESPACE, new byte[] { 32, 9 }, false),
            (ByteProcessor.FIND_NON_LINEAR_WHITESPACE, new byte[] { 32, 9 }, true)
        };
        foreach (var predicate in predicates)
            for (int value = 0; value <= byte.MaxValue; value++)
            {
                bool member = Array.IndexOf(predicate.Stops, (byte)value) >= 0;
                Assert.Equal(predicate.Inverted ? member : !member, predicate.Visitor((byte)value));
            }
    }

    [Fact]
    public void OriginalBufferForwardDelimiterScenariosUseTheSameLogicalIndices()
    {
        var text = new AsciiString("abc\r\n\ndef\r\rghi\n\njkl\0\0mno  \t\tx");
        Assert.Equal(3, text.ForEachByte(0, text.Length(), ByteProcessor.FIND_CRLF));
        Assert.Equal(6, text.ForEachByte(3, text.Length() - 3, ByteProcessor.FIND_NON_CRLF));
        Assert.Equal(9, text.ForEachByte(6, text.Length() - 6, ByteProcessor.FIND_CR));
        Assert.Equal(11, text.ForEachByte(9, text.Length() - 9, ByteProcessor.FIND_NON_CR));
        Assert.Equal(14, text.ForEachByte(11, text.Length() - 11, ByteProcessor.FIND_LF));
        Assert.Equal(16, text.ForEachByte(14, text.Length() - 14, ByteProcessor.FIND_NON_LF));
        Assert.Equal(19, text.ForEachByte(16, text.Length() - 16, ByteProcessor.FIND_NUL));
        Assert.Equal(21, text.ForEachByte(19, text.Length() - 19, ByteProcessor.FIND_NON_NUL));
        Assert.Equal(24, text.ForEachByte(19, text.Length() - 19, ByteProcessor.FIND_ASCII_SPACE));
        Assert.Equal(24, text.ForEachByte(21, text.Length() - 21, ByteProcessor.FIND_LINEAR_WHITESPACE));
        Assert.Equal(28, text.ForEachByte(24, text.Length() - 24, ByteProcessor.FIND_NON_LINEAR_WHITESPACE));
        Assert.Equal(-1, text.ForEachByte(28, text.Length() - 28, ByteProcessor.FIND_LINEAR_WHITESPACE));
    }

    [Fact]
    public void OriginalBufferReverseDelimiterScenariosUseTheSameLogicalIndices()
    {
        var text = new AsciiString("abc\r\n\ndef\r\rghi\n\njkl\0\0mno  \t\tx");
        Assert.Equal(27, text.ForEachByteDesc(0, text.Length(), ByteProcessor.FIND_LINEAR_WHITESPACE));
        Assert.Equal(25, text.ForEachByteDesc(0, text.Length(), ByteProcessor.FIND_ASCII_SPACE));
        Assert.Equal(23, text.ForEachByteDesc(0, 28, ByteProcessor.FIND_NON_LINEAR_WHITESPACE));
        Assert.Equal(20, text.ForEachByteDesc(0, 24, ByteProcessor.FIND_NUL));
        Assert.Equal(18, text.ForEachByteDesc(0, 21, ByteProcessor.FIND_NON_NUL));
        Assert.Equal(15, text.ForEachByteDesc(0, 19, ByteProcessor.FIND_LF));
        Assert.Equal(13, text.ForEachByteDesc(0, 16, ByteProcessor.FIND_NON_LF));
        Assert.Equal(10, text.ForEachByteDesc(0, 14, ByteProcessor.FIND_CR));
        Assert.Equal(8, text.ForEachByteDesc(0, 11, ByteProcessor.FIND_NON_CR));
        Assert.Equal(5, text.ForEachByteDesc(0, 9, ByteProcessor.FIND_CRLF));
        Assert.Equal(2, text.ForEachByteDesc(0, 6, ByteProcessor.FIND_NON_CRLF));
        Assert.Equal(-1, text.ForEachByteDesc(0, 3, ByteProcessor.FIND_CRLF));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StatefulVisitorsRunSynchronouslyInSliceOrderAndStopAtTheLogicalIndex(bool descending)
    {
        var text = new AsciiString(new byte[] { 99, 10, 20, 30, 40, 50, 88 }, 1, 5, false);
        var visited = new List<byte>();
        int caller = Thread.CurrentThread.ManagedThreadId;
        Func<byte, bool> visitor = value =>
        {
            Assert.Equal(caller, Thread.CurrentThread.ManagedThreadId);
            visited.Add(value);
            return value != 30;
        };
        int index = descending ? text.ForEachByteDesc(1, 3, visitor) : text.ForEachByte(1, 3, visitor);
        Assert.Equal(2, index);
        Assert.Equal(descending ? new byte[] { 40, 30 } : new byte[] { 20, 30 }, visited);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallbackExceptionsKeepIdentityAndPreventFurtherVisits(bool descending)
    {
        var text = new AsciiString(new byte[] { 1, 2, 3, 4 }, false);
        var failure = new InvalidOperationException("visitor failure");
        int visits = 0;
        Func<byte, bool> visitor = value => { if (++visits == 2) throw failure; return true; };
        var observed = Assert.Throws<InvalidOperationException>(() =>
        {
            if (descending) text.ForEachByteDesc(visitor);
            else text.ForEachByte(visitor);
        });
        Assert.Same(failure, observed);
        Assert.Equal(2, visits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyWholeAndEndWindowsDoNotInvokeAValidVisitor(bool descending)
    {
        var text = new AsciiString(new byte[] { 9, 1, 2, 8 }, 1, 2, false);
        var empty = new AsciiString(Array.Empty<byte>(), false);
        int calls = 0;
        Func<byte, bool> visitor = value => { calls++; return false; };
        Assert.Equal(-1, descending ? empty.ForEachByteDesc(visitor) : empty.ForEachByte(visitor));
        Assert.Equal(-1, descending ? text.ForEachByteDesc(2, 0, visitor) : text.ForEachByte(2, 0, visitor));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void NullVisitorsFailAtEveryPublicBoundaryEvenForEmptyInput(bool descending, bool window, bool empty)
    {
        var text = new AsciiString(empty ? Array.Empty<byte>() : new byte[] { 1 }, false);
        var error = Assert.Throws<ArgumentNullException>(() =>
        {
            if (descending)
            {
                if (window) text.ForEachByteDesc(0, text.Length(), null);
                else text.ForEachByteDesc(null);
            }
            else
            {
                if (window) text.ForEachByte(0, text.Length(), null);
                else text.ForEachByte(null);
            }
        });
        Assert.Equal("visitor", error.ParamName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidWindowsTakePrecedenceOverNullVisitorsWithoutCheckedOverflow(bool descending)
    {
        var text = new AsciiString(new byte[] { 1, 2 }, false);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            if (descending) text.ForEachByteDesc(int.MaxValue, 1, null);
            else text.ForEachByte(int.MaxValue, 1, null);
        });
    }

    [Fact]
    public void MulticastUsesTheNativeFinalReturnValue()
    {
        var text = new AsciiString(new byte[] { 1, 2 }, false);
        var calls = new List<int>();
        Func<byte, bool> visitor = value => { calls.Add(10 + value); return false; };
        visitor += value => { calls.Add(20 + value); return true; };
        Assert.Equal(-1, text.ForEachByte(visitor));
        Assert.Equal(new[] { 11, 21, 12, 22 }, calls);
        calls.Clear();
        Func<byte, bool> stop = value => { calls.Add(10 + value); return true; };
        stop += value => { calls.Add(20 + value); return false; };
        Assert.Equal(0, text.ForEachByte(stop));
        Assert.Equal(new[] { 11, 21 }, calls);
    }

    [Fact]
    public void MulticastExceptionStopsTheInvocationAndTraversal()
    {
        var text = new AsciiString(new byte[] { 1, 2 }, false);
        var calls = new List<int>();
        var failure = new InvalidOperationException("multicast failure");
        Func<byte, bool> visitor = value => { calls.Add(1); return false; };
        visitor += value => { calls.Add(2); throw failure; };
        visitor += value => { calls.Add(3); return true; };
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => text.ForEachByte(visitor)));
        Assert.Equal(new[] { 1, 2 }, calls);
    }

    [Fact]
    public void SharedViewReadsMutationsAndRetainsUnsignedHighBytes()
    {
        byte[] bytes = { 9, 10, 20, 255, 8 };
        var text = new AsciiString(bytes, 1, 3, false);
        var visited = new List<byte>();
        Assert.Equal(-1, text.ForEachByte(value =>
        {
            visited.Add(value);
            if (visited.Count == 1) bytes[2] = 128;
            return true;
        }));
        Assert.Equal(new byte[] { 10, 128, 255 }, visited);
    }

    [Fact]
    public void CapturedStatePersistsAcrossSeparateTraversalCalls()
    {
        int total = 0;
        Func<byte, bool> visitor = value => { total += value; return total < 6; };
        Assert.Equal(-1, new AsciiString(new byte[] { 1, 2 }, false).ForEachByte(visitor));
        Assert.Equal(0, new AsciiString(new byte[] { 3, 4 }, false).ForEachByte(visitor));
        Assert.Equal(6, total);
    }
}
