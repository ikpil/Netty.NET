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

namespace Netty.NET.Common.Concurrent;

public class DefaultFutureListeners<T> where T : IFuture<T>
{
    private IGenericFutureListener<T>[] _listeners;
    private int _size;
    private int _progressiveSize; // the number of progressive listeners

    //@SuppressWarnings("unchecked")
    public DefaultFutureListeners(IGenericFutureListener<T> first, IGenericFutureListener<T> second)
    {
        _listeners = new IGenericFutureListener<T>[2];
        _listeners[0] = first;
        _listeners[1] = second;
        _size = 2;
        if (first is IGenericProgressiveFutureListener<IProgressiveFuture<T>>)
        {
            _progressiveSize++;
        }

        if (second is IGenericProgressiveFutureListener<IProgressiveFuture<T>>)
        {
            _progressiveSize++;
        }
    }

    public void add(IGenericFutureListener<T> l)
    {
        IGenericFutureListener<T>[] listeners = _listeners;
        int size = _size;
        if (size == listeners.Length)
        {
            listeners = _listeners = Arrays.copyOf(listeners, size << 1);
        }

        listeners[size] = l;
        _size = size + 1;

        if (l is IGenericProgressiveFutureListener<T>)
        {
            _progressiveSize++;
        }
    }

    public void remove(IGenericFutureListener<T> l)
    {
        IGenericFutureListener<T>[] listeners = _listeners;
        int size = _size;
        for (int i = 0; i < size; i++)
        {
            if (listeners[i] == l)
            {
                int listenersToMove = size - i - 1;
                if (listenersToMove > 0)
                {
                    Arrays.arraycopy(listeners, i + 1, listeners, i, listenersToMove);
                }

                listeners[--size] = null;
                _size = size;

                if (l is IGenericProgressiveFutureListener<T>)
                {
                    _progressiveSize--;
                }

                return;
            }
        }
    }

    public IGenericFutureListener<T>[] listeners()
    {
        return _listeners;
    }

    public int size()
    {
        return _size;
    }

    public int progressiveSize()
    {
        return _progressiveSize;
    }
}