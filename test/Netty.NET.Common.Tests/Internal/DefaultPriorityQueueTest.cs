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
    public void testPoll() {
        PriorityQueue<TestElement> queue = new DefaultPriorityQueue<TestElement>(TestElementComparator.INSTANCE, 0);
        assertEmptyQueue(queue);

        TestElement a = new TestElement(5);
        TestElement b = new TestElement(10);
        TestElement c = new TestElement(2);
        TestElement d = new TestElement(7);
        TestElement e = new TestElement(6);

        assertOffer(queue, a);
        assertOffer(queue, b);
        assertOffer(queue, c);
        assertOffer(queue, d);

        // Remove the first element
        Assert.Same(c, queue.peek());
        Assert.Same(c, queue.poll());
        Assert.Equal(3, queue.size());

        // Test that offering another element preserves the priority queue semantics.
        assertOffer(queue, e);
        Assert.Equal(4, queue.size());
        Assert.Same(a, queue.peek());
        Assert.Same(a, queue.poll());
        Assert.Equal(3, queue.size());

        // Keep removing the remaining elements
        Assert.Same(e, queue.peek());
        Assert.Same(e, queue.poll());
        Assert.Equal(2, queue.size());

        Assert.Same(d, queue.peek());
        Assert.Same(d, queue.poll());
        Assert.Equal(1, queue.size());

        Assert.Same(b, queue.peek());
        Assert.Same(b, queue.poll());
        assertEmptyQueue(queue);
    }

    [Fact]
    public void testClear() {
        PriorityQueue<TestElement> queue = new DefaultPriorityQueue<TestElement>(TestElementComparator.INSTANCE, 0);
        assertEmptyQueue(queue);

        TestElement a = new TestElement(5);
        TestElement b = new TestElement(10);
        TestElement c = new TestElement(2);
        TestElement d = new TestElement(6);

        assertOffer(queue, a);
        assertOffer(queue, b);
        assertOffer(queue, c);
        assertOffer(queue, d);

        queue.clear();
        assertEmptyQueue(queue);

        // Test that elements can be re-inserted after the clear operation
        assertOffer(queue, a);
        Assert.Same(a, queue.peek());

        assertOffer(queue, b);
        Assert.Same(a, queue.peek());

        assertOffer(queue, c);
        Assert.Same(c, queue.peek());

        assertOffer(queue, d);
        Assert.Same(c, queue.peek());
    }

    [Fact]
    public void testClearIgnoringIndexes() {
        PriorityQueue<TestElement> queue = new DefaultPriorityQueue<TestElement>(TestElementComparator.INSTANCE, 0);
        assertEmptyQueue(queue);

        TestElement a = new TestElement(5);
        TestElement b = new TestElement(10);
        TestElement c = new TestElement(2);
        TestElement d = new TestElement(6);
        TestElement e = new TestElement(11);

        assertOffer(queue, a);
        assertOffer(queue, b);
        assertOffer(queue, c);
        assertOffer(queue, d);

        queue.clearIgnoringIndexes();
        assertEmptyQueue(queue);

        // Elements cannot be re-inserted but new ones can.
        try {
            queue.offer(a);
            Assert.Fail();
        } catch (ArgumentException t) {
            // expected
        }

        assertOffer(queue, e);
        Assert.Same(e, queue.peek());
    }

    [Fact]
    public void testRemoval() {
        testRemoval(false);
    }

    [Fact]
    public void testRemovalTyped() {
        testRemoval(true);
    }

    [Fact]
    public void testRemovalFuzz() {
        ThreadLocalRandom threadLocalRandom = ThreadLocalRandom.current();
        final int numElements = threadLocalRandom.nextInt(0, 30);
        final TestElement[] values = new TestElement[numElements];
        PriorityQueue<TestElement> queue =
                new DefaultPriorityQueue<>(TestElementComparator.INSTANCE, values.length);
        for (int i = 0; i < values.length; ++i) {
            do {
                values[i] = new TestElement(threadLocalRandom.nextInt(0, numElements * 2));
            } while (!queue.add(values[i]));
        }

        for (int i = 0; i < values.length; ++i) {
            try {
                Assert.True(queue.removeTyped(values[i]));
                Assert.Equal(queue.size(), values.length - (i + 1));
            } catch (Exception cause) {
                StringBuilder sb = new StringBuilder(values.length * 2);
                sb.append("error on removal of index: ").append(i).append(" [");
                for (TestElement value : values) {
                    sb.append(value).append(" ");
                }
                sb.append("]");
                throw new AssertionError(sb.ToString(), cause);
            }
        }
        assertEmptyQueue(queue);
    }

    private static void testRemoval(bool typed) {
        PriorityQueue<TestElement> queue = new DefaultPriorityQueue<>(TestElementComparator.INSTANCE, 4);
        assertEmptyQueue(queue);

        TestElement a = new TestElement(5);
        TestElement b = new TestElement(10);
        TestElement c = new TestElement(2);
        TestElement d = new TestElement(6);
        TestElement notInQueue = new TestElement(-1);

        assertOffer(queue, a);
        assertOffer(queue, b);
        assertOffer(queue, c);
        assertOffer(queue, d);

        // Remove an element that isn't in the queue.
        Assert.False(typed ? queue.removeTyped(notInQueue) : queue.remove(notInQueue));
        Assert.Same(c, queue.peek());
        Assert.Equal(4, queue.size());

        // Remove the last element in the array, when the array is non-empty.
        Assert.True(typed ? queue.removeTyped(b) : queue.remove(b));
        Assert.Same(c, queue.peek());
        Assert.Equal(3, queue.size());

        // Re-insert the element after removal
        assertOffer(queue, b);
        Assert.Same(c, queue.peek());
        Assert.Equal(4, queue.size());

        // Repeat remove the last element in the array, when the array is non-empty.
        Assert.True(typed ? queue.removeTyped(d) : queue.remove(d));
        Assert.Same(c, queue.peek());
        Assert.Equal(3, queue.size());

        Assert.True(typed ? queue.removeTyped(b) : queue.remove(b));
        Assert.Same(c, queue.peek());
        Assert.Equal(2, queue.size());

        // Remove the head of the queue.
        Assert.True(typed ? queue.removeTyped(c) : queue.remove(c));
        Assert.Same(a, queue.peek());
        Assert.Equal(1, queue.size());

        Assert.True(typed ? queue.removeTyped(a) : queue.remove(a));
        assertEmptyQueue(queue);
    }

    [Fact]
    public void testZeroInitialSize() {
        PriorityQueue<TestElement> queue = new DefaultPriorityQueue<>(TestElementComparator.INSTANCE, 0);
        assertEmptyQueue(queue);
        TestElement e = new TestElement(1);
        assertOffer(queue, e);
        Assert.Same(e, queue.peek());
        Assert.Equal(1, queue.size());
        Assert.False(queue.isEmpty());
        Assert.Same(e, queue.poll());
        assertEmptyQueue(queue);
    }

    [Fact]
    public void testPriorityChange() {
        PriorityQueue<TestElement> queue = new DefaultPriorityQueue<>(TestElementComparator.INSTANCE, 0);
        assertEmptyQueue(queue);
        TestElement a = new TestElement(10);
        TestElement b = new TestElement(20);
        TestElement c = new TestElement(30);
        TestElement d = new TestElement(25);
        TestElement e = new TestElement(23);
        TestElement f = new TestElement(15);
        queue.add(a);
        queue.add(b);
        queue.add(c);
        queue.add(d);
        queue.add(e);
        queue.add(f);

        e.value = 35;
        queue.priorityChanged(e);

        a.value = 40;
        queue.priorityChanged(a);

        a.value = 31;
        queue.priorityChanged(a);

        d.value = 10;
        queue.priorityChanged(d);

        f.value = 5;
        queue.priorityChanged(f);

        List<TestElement> expectedOrderList = new List<>(queue.size());
        expectedOrderList.addAll(Arrays.asList(a, b, c, d, e, f));
        expectedOrderList.sort(TestElementComparator.INSTANCE);

        Assert.Equal(expectedOrderList.size(), queue.size());
        Assert.Equal(expectedOrderList.isEmpty(), queue.isEmpty());
        Iterator<TestElement> itr = expectedOrderList.iterator();
        while (itr.hasNext()) {
            TestElement next = itr.next();
            TestElement poll = queue.poll();
            Assert.Equal(next, poll);
            itr.remove();
            Assert.Equal(expectedOrderList.size(), queue.size());
            Assert.Equal(expectedOrderList.isEmpty(), queue.isEmpty());
        }
    }

    private static void assertOffer(PriorityQueue<TestElement> queue, TestElement a) {
        Assert.True(queue.offer(a));
        Assert.True(queue.contains(a));
        Assert.True(queue.containsTyped(a));
        try { // An element can not be inserted more than 1 time.
            queue.offer(a);
            Assert.Fail();
        } catch (ArgumentException ignored) {
            // ignored
        }
    }

    private static void assertEmptyQueue(PriorityQueue<TestElement> queue) {
        Assert.Null(queue.peek());
        Assert.Null(queue.poll());
        Assert.Equal(0, queue.size());
        Assert.True(queue.isEmpty());
    }

    private static final class TestElementComparator implements Comparator<TestElement>, Serializable {
        private static final long serialVersionUID = 7930368853384760103L;

        static final TestElementComparator INSTANCE = new TestElementComparator();

        private TestElementComparator() {
        }

        @Override
        public int compare(TestElement o1, TestElement o2) {
            return o1.value - o2.value;
        }
    }

    class TestElement : IPriorityQueueNode 
    {
        internal int value;
        private int priorityQueueIndex = INDEX_NOT_IN_QUEUE;

        TestElement(int value) {
            this.value = value;
        }

        @Override
        public bool equals(Object o) {
            return o instanceof TestElement && ((TestElement) o).value == value;
        }

        @Override
        public int hashCode() {
            return value;
        }

        @Override
        public int priorityQueueIndex(DefaultPriorityQueue queue) {
            return priorityQueueIndex;
        }

        @Override
        public void priorityQueueIndex(DefaultPriorityQueue queue, int i) {
            priorityQueueIndex = i;
        }
    }
}
