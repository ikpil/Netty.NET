/*
 * Copyright 2017 The Netty Project
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
namespace Netty.NET.Common.Internal;






public final class EmptyPriorityQueue<T> : PriorityQueue<T> {
    private static readonly PriorityQueue<object> INSTANCE = new EmptyPriorityQueue<object>();

    private EmptyPriorityQueue() {
    }

    /**
     * Returns an unmodifiable empty {@link PriorityQueue}.
     */
    @SuppressWarnings("unchecked")
    public static <V> EmptyPriorityQueue<V> instance() {
        return (EmptyPriorityQueue<V>) INSTANCE;
    }

    @Override
    public bool removeTyped(T node) {
        return false;
    }

    @Override
    public bool containsTyped(T node) {
        return false;
    }

    @Override
    public void priorityChanged(T node) {
    }

    @Override
    public int size() {
        return 0;
    }

    @Override
    public bool isEmpty() {
        return true;
    }

    @Override
    public bool contains(object o) {
        return false;
    }

    @Override
    public Iterator<T> iterator() {
        return Collections.<T>emptyList().iterator();
    }

    @Override
    public object[] toArray() {
        return EmptyArrays.EMPTY_OBJECTS;
    }

    @Override
    public <T1> T1[] toArray(T1[] a) {
        if (a.length > 0) {
            a[0] = null;
        }
        return a;
    }

    @Override
    public bool add(T t) {
        return false;
    }

    @Override
    public bool remove(object o) {
        return false;
    }

    @Override
    public bool containsAll(Collection<?> c) {
        return false;
    }

    @Override
    public bool addAll(Collection<? extends T> c) {
        return false;
    }

    @Override
    public bool removeAll(Collection<?> c) {
        return false;
    }

    @Override
    public bool retainAll(Collection<?> c) {
        return false;
    }

    @Override
    public void clear() {
    }

    @Override
    public void clearIgnoringIndexes() {
    }

    @Override
    public bool equals(object o) {
        return o instanceof PriorityQueue && ((PriorityQueue) o).isEmpty();
    }

    @Override
    public int hashCode() {
        return 0;
    }

    @Override
    public bool offer(T t) {
        return false;
    }

    @Override
    public T remove() {
        throw new NoSuchElementException();
    }

    @Override
    public T poll() {
        return null;
    }

    @Override
    public T element() {
        throw new NoSuchElementException();
    }

    @Override
    public T peek() {
        return null;
    }

    @Override
    public string toString() {
        return EmptyPriorityQueue.class.getSimpleName();
    }
}
