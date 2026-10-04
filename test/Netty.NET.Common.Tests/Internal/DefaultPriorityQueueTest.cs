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

using System;
using System.Collections.Generic;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Internal;


public class DefaultPriorityQueueTest 
{
    [Fact]
    public void TestPoll() {
        DefaultPriorityQueue<TestElement> queue = new DefaultPriorityQueue<TestElement>(TestElementComparator.INSTANCE, 0);
        AssertEmptyQueue(queue);

        TestElement a = new TestElement(5);
        TestElement b = new TestElement(10);
        TestElement c = new TestElement(2);
        TestElement d = new TestElement(7);
        TestElement e = new TestElement(6);

        AssertOffer(queue, a);
        AssertOffer(queue, b);
        AssertOffer(queue, c);
        AssertOffer(queue, d);

        // Remove the first element
        Assert.Same(c, queue.Peek());
        Assert.Same(c, queue.Poll());
        Assert.Equal(3, queue.Size());

        // Test that offering another element preserves the priority queue semantics.
        AssertOffer(queue, e);
        Assert.Equal(4, queue.Size());
        Assert.Same(a, queue.Peek());
        Assert.Same(a, queue.Poll());
        Assert.Equal(3, queue.Size());

        // Keep removing the remaining elements
        Assert.Same(e, queue.Peek());
        Assert.Same(e, queue.Poll());
        Assert.Equal(2, queue.Size());

        Assert.Same(d, queue.Peek());
        Assert.Same(d, queue.Poll());
        Assert.Equal(1, queue.Size());

        Assert.Same(b, queue.Peek());
        Assert.Same(b, queue.Poll());
        AssertEmptyQueue(queue);
    }

    [Fact]
    public void TestClear() {
        DefaultPriorityQueue<TestElement> queue = new DefaultPriorityQueue<TestElement>(TestElementComparator.INSTANCE, 0);
        AssertEmptyQueue(queue);

        TestElement a = new TestElement(5);
        TestElement b = new TestElement(10);
        TestElement c = new TestElement(2);
        TestElement d = new TestElement(6);

        AssertOffer(queue, a);
        AssertOffer(queue, b);
        AssertOffer(queue, c);
        AssertOffer(queue, d);

        queue.Clear();
        AssertEmptyQueue(queue);

        // Test that elements can be re-inserted after the clear operation
        AssertOffer(queue, a);
        Assert.Same(a, queue.Peek());

        AssertOffer(queue, b);
        Assert.Same(a, queue.Peek());

        AssertOffer(queue, c);
        Assert.Same(c, queue.Peek());

        AssertOffer(queue, d);
        Assert.Same(c, queue.Peek());
    }

    [Fact]
    public void TestClearIgnoringIndexes() {
        DefaultPriorityQueue<TestElement> queue = new DefaultPriorityQueue<TestElement>(TestElementComparator.INSTANCE, 0);
        AssertEmptyQueue(queue);

        TestElement a = new TestElement(5);
        TestElement b = new TestElement(10);
        TestElement c = new TestElement(2);
        TestElement d = new TestElement(6);
        TestElement e = new TestElement(11);

        AssertOffer(queue, a);
        AssertOffer(queue, b);
        AssertOffer(queue, c);
        AssertOffer(queue, d);

        queue.ClearIgnoringIndexes();
        AssertEmptyQueue(queue);

        // Elements cannot be re-inserted but new ones can.
        try {
            queue.Offer(a);
            Assert.Fail();
        } catch (ArgumentException t) {
            // expected
        }

        AssertOffer(queue, e);
        Assert.Same(e, queue.Peek());
    }

    [Fact]
    public void TestRemoval() {
        TestRemoval0(false);
    }

    [Fact]
    public void TestRemovalTyped() {
        TestRemoval0(true);
    }

    [Fact]
    public void TestRemovalFuzz() {
        var threadLocalRandom = Random.Shared;
        int numElements = threadLocalRandom.Next(0, 30);
        TestElement[] values = new TestElement[numElements];
        DefaultPriorityQueue<TestElement> queue =
                new DefaultPriorityQueue<TestElement>(TestElementComparator.INSTANCE, values.Length);
        for (int i = 0; i < values.Length; ++i) {
            do {
                values[i] = new TestElement(threadLocalRandom.Next(0, numElements * 2));
            } while (!queue.Offer(values[i]));
        }

        for (int i = 0; i < values.Length; ++i) {
            try {
                Assert.True(queue.RemoveTyped(values[i]));
                Assert.Equal(queue.Size(), values.Length - (i + 1));
            } catch (Exception cause) {
                StringBuilder sb = new StringBuilder(values.Length * 2);
                sb.Append("error on removal of index: ").Append(i).Append(" [");
                foreach (TestElement value in values) {
                    sb.Append(value).Append(" ");
                }
                sb.Append("]");
                throw new InvalidOperationException(sb.ToString(), cause);
            }
        }
        AssertEmptyQueue(queue);
    }

    private static void TestRemoval0(bool typed) {
        DefaultPriorityQueue<TestElement> queue = new DefaultPriorityQueue<TestElement>(TestElementComparator.INSTANCE, 4);
        AssertEmptyQueue(queue);

        TestElement a = new TestElement(5);
        TestElement b = new TestElement(10);
        TestElement c = new TestElement(2);
        TestElement d = new TestElement(6);
        TestElement notInQueue = new TestElement(-1);

        AssertOffer(queue, a);
        AssertOffer(queue, b);
        AssertOffer(queue, c);
        AssertOffer(queue, d);

        // Remove an element that isn't in the queue.
        Assert.False(typed ? queue.RemoveTyped(notInQueue) : queue.Remove(notInQueue));
        Assert.Same(c, queue.Peek());
        Assert.Equal(4, queue.Size());

        // Remove the last element in the array, when the array is non-empty.
        Assert.True(typed ? queue.RemoveTyped(b) : queue.Remove(b));
        Assert.Same(c, queue.Peek());
        Assert.Equal(3, queue.Size());

        // Re-insert the element after removal
        AssertOffer(queue, b);
        Assert.Same(c, queue.Peek());
        Assert.Equal(4, queue.Size());

        // Repeat remove the last element in the array, when the array is non-empty.
        Assert.True(typed ? queue.RemoveTyped(d) : queue.Remove(d));
        Assert.Same(c, queue.Peek());
        Assert.Equal(3, queue.Size());

        Assert.True(typed ? queue.RemoveTyped(b) : queue.Remove(b));
        Assert.Same(c, queue.Peek());
        Assert.Equal(2, queue.Size());

        // Remove the head of the queue.
        Assert.True(typed ? queue.RemoveTyped(c) : queue.Remove(c));
        Assert.Same(a, queue.Peek());
        Assert.Equal(1, queue.Size());

        Assert.True(typed ? queue.RemoveTyped(a) : queue.Remove(a));
        AssertEmptyQueue(queue);
    }

    [Fact]
    public void TestZeroInitialSize() {
        DefaultPriorityQueue<TestElement> queue = new DefaultPriorityQueue<TestElement>(TestElementComparator.INSTANCE, 0);
        AssertEmptyQueue(queue);
        TestElement e = new TestElement(1);
        AssertOffer(queue, e);
        Assert.Same(e, queue.Peek());
        Assert.Equal(1, queue.Size());
        Assert.False(queue.IsEmpty());
        Assert.Same(e, queue.Poll());
        AssertEmptyQueue(queue);
    }

    [Fact]
    public void TestPriorityChange() {
        DefaultPriorityQueue<TestElement> queue = new DefaultPriorityQueue<TestElement>(TestElementComparator.INSTANCE, 0);
        AssertEmptyQueue(queue);
        TestElement a = new TestElement(10);
        TestElement b = new TestElement(20);
        TestElement c = new TestElement(30);
        TestElement d = new TestElement(25);
        TestElement e = new TestElement(23);
        TestElement f = new TestElement(15);
        queue.Offer(a);
        queue.Offer(b);
        queue.Offer(c);
        queue.Offer(d);
        queue.Offer(e);
        queue.Offer(f);

        e.value = 35;
        queue.PriorityChanged(e);

        a.value = 40;
        queue.PriorityChanged(a);

        a.value = 31;
        queue.PriorityChanged(a);

        d.value = 10;
        queue.PriorityChanged(d);

        f.value = 5;
        queue.PriorityChanged(f);

        var expectedOrderList = new List<TestElement> { a, b, c, d, e, f };
        expectedOrderList.Sort(TestElementComparator.INSTANCE);
        Assert.Equal(expectedOrderList.Count, queue.Size());
        Assert.Equal(expectedOrderList.Count == 0, queue.IsEmpty());
        foreach (TestElement expected in expectedOrderList.ToArray())
        {
            Assert.Equal(expected, queue.Poll());
            expectedOrderList.RemoveAt(0);
            Assert.Equal(expectedOrderList.Count, queue.Size());
            Assert.Equal(expectedOrderList.Count == 0, queue.IsEmpty());
        }
    }
    private static void AssertOffer(DefaultPriorityQueue<TestElement> queue, TestElement a) {
        Assert.True(queue.Offer(a));
        Assert.True(queue.Contains(a));
        Assert.True(queue.ContainsTyped(a));
        try { // An element can not be inserted more than 1 time.
            queue.Offer(a);
            Assert.Fail();
        } catch (ArgumentException ignored) {
            // ignored
        }
    }

    private static void AssertEmptyQueue(DefaultPriorityQueue<TestElement> queue) {
        Assert.Null(queue.Peek());
        Assert.Null(queue.Poll());
        Assert.Equal(0, queue.Size());
        Assert.True(queue.IsEmpty());
    }

    private sealed class TestElementComparator : IComparer<TestElement>
    {
        public static readonly TestElementComparator INSTANCE = new();
        public int Compare(TestElement o1, TestElement o2) => o1.value.CompareTo(o2.value);
    }

    private sealed class TestElement : IPriorityQueueNode<TestElement>
    {
        internal int value;
        private int _index = -1;
        public TestElement(int value) => this.value = value;
        public override bool Equals(object o) => o is TestElement element && element.value == value;
        public override int GetHashCode() => value;
        public int PriorityQueueIndex(DefaultPriorityQueue<TestElement> queue) => _index;
        public void PriorityQueueIndex(DefaultPriorityQueue<TestElement> queue, int i) => _index = i;
    }
}
