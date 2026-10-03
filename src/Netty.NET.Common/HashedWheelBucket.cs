/*
 * Copyright 2012 The Netty Project
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
using System.Diagnostics;

namespace Netty.NET.Common;

/**
     * Bucket that stores HashedWheelTimeouts. These are stored in a linked-list like datastructure to allow easy
     * removal of HashedWheelTimeouts in the middle. Also the HashedWheelTimeout act as nodes themself and so no
     * extra object creation is needed.
     */
internal sealed class HashedWheelBucket
{
    // Used for the linked-list datastructure
    private HashedWheelTimeout _head;
    private HashedWheelTimeout _tail;

    /**
         * Add {@link HashedWheelTimeout} to this bucket.
         */
    public void AddTimeout(HashedWheelTimeout timeout)
    {
        Debug.Assert(timeout._bucket == null);
        timeout._bucket = this;
        if (_head == null)
        {
            _head = _tail = timeout;
        }
        else
        {
            _tail._next = timeout;
            timeout._prev = _tail;
            _tail = timeout;
        }
    }

    /**
         * Expire all {@link HashedWheelTimeout}s for the given {@code deadline}.
         */
    public void ExpireTimeouts(long deadline)
    {
        HashedWheelTimeout timeout = _head;

        // process all timeouts
        while (timeout != null)
        {
            HashedWheelTimeout next = timeout._next;
            if (timeout._remainingRounds <= 0)
            {
                if (timeout._deadline <= deadline)
                {
                    timeout.Expire();
                }
                else
                {
                    // The timeout was placed into a wrong slot. This should never happen.
                    throw new InvalidOperationException($"timeout.deadline ({timeout._deadline}) > deadline ({deadline})");
                }
            }
            else if (!timeout.IsCancelled())
            {
                timeout._remainingRounds--;
            }

            timeout = next;
        }
    }

    public HashedWheelTimeout Remove(HashedWheelTimeout timeout)
    {
        HashedWheelTimeout next = timeout._next;
        // remove timeout that was either processed or cancelled by updating the linked-list
        if (timeout._prev != null)
        {
            timeout._prev._next = next;
        }

        if (timeout._next != null)
        {
            timeout._next._prev = timeout._prev;
        }

        if (timeout == _head)
        {
            // if timeout is also the tail we need to adjust the entry too
            if (timeout == _tail)
            {
                _tail = null;
                _head = null;
            }
            else
            {
                _head = next;
            }
        }
        else if (timeout == _tail)
        {
            // if the timeout is the tail modify the tail to be the prev node.
            _tail = timeout._prev;
        }

        // null out prev, next and bucket to allow for GC.
        timeout._prev = null;
        timeout._next = null;
        timeout._bucket = null;
        return next;
    }

    /**
         * Clear this bucket and return all not expired / cancelled {@link Timeout}s.
         */
    public void ClearTimeouts(ISet<ITimeout> set)
    {
        for (;;)
        {
            HashedWheelTimeout timeout = PollTimeout();
            if (timeout == null)
            {
                return;
            }

            if (timeout.IsExpired() || timeout.IsCancelled())
            {
                continue;
            }

            set.Add(timeout);
        }
    }

    private HashedWheelTimeout PollTimeout()
    {
        HashedWheelTimeout head = this._head;
        if (head == null)
        {
            return null;
        }

        HashedWheelTimeout next = head._next;
        if (next == null)
        {
            _tail = this._head = null;
        }
        else
        {
            this._head = next;
            next._prev = null;
        }

        // null out prev and next to allow for GC.
        head._next = null;
        head._prev = null;
        head._bucket = null;
        return head;
    }
}
