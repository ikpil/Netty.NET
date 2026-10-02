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
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Common.Internal;

/**
 * Some pending write which should be picked up later.
 */
public sealed class PendingWrite
{
    private static readonly ObjectPool<PendingWrite> RECYCLER = ObjectPool.newPool(
        new AnonymousObjectCreator<PendingWrite>(x => new PendingWrite(x))
    );

    /**
     * Create a new empty {@link RecyclableArrayList} instance
     */
    // CLR: this is a queue-owned pooled node, not an asynchronous result object.
    // The caller supplies its producer-owned TCS and exposes only its Task to observers.
    // Do not retain this node after recycling or across an asynchronous handoff;
    // capture Message and transfer the completion source before returning it to the pool.
    public static PendingWrite Rent(object message, TaskCompletionSource completion = null)
    {
        PendingWrite pending = RECYCLER.get();
        pending._msg = message;
        pending._completion = completion;
        Volatile.Write(ref pending._active, 1);
        return pending;
    }

    private readonly IObjectPoolHandle<PendingWrite> _handle;
    private object _msg;
    private TaskCompletionSource _completion;
    private int _active;

    private PendingWrite(IObjectPoolHandle<PendingWrite> handle)
    {
        _handle = handle;
    }

    /**
     * Clear and recycle this instance.
     */
    // Returning a node alone does not release its message or settle its Task.
    // Both responsibilities must already have been transferred to the next owner.
    public bool Recycle()
    {
        Claim();
        ClearAndRecycle();
        return true;
    }

    private void Claim()
    {
        if (Interlocked.Exchange(ref _active, 0) == 0)
            throw new InvalidOperationException("The pending write has already been recycled.");
    }

    private void ClearAndRecycle()
    {
        _msg = null;
        _completion = null;
        _handle.recycle(this);
    }

    /**
     * Fails the underlying {@link Promise} with the given cause and recycle this instance.
     */
    // CLR: a canceled/already-completed producer cannot prevent message cleanup.
    // An OperationCanceledException passed as a failure remains a fault; cancellation
    // must be published explicitly by the operation owner with TrySetCanceled(token).
    public bool FailAndRecycle(Exception cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        Claim();
        try
        {
            ReferenceCountUtil.release(_msg);
            _completion?.TrySetException(cause);
        }
        finally { ClearAndRecycle(); }
        return true;
    }

    /**
     * Mark the underlying {@link Promise} successfully and recycle this instance.
     */
    // Success does not release the message: the write consumer now owns it.
    public bool SucceedAndRecycle()
    {
        Claim();
        try { _completion?.TrySetResult(); }
        finally { ClearAndRecycle(); }
        return true;
    }

    public object Message => _msg;
    public Task Completion => _completion?.Task;

    /**
     * Recycle this instance and return the {@link Promise}.
     */
    // Only the next producer receives mutation authority; observers retain Completion.
    public TaskCompletionSource RecycleAndGetCompletionSource()
    {
        Claim();
        TaskCompletionSource completion = _completion;
        ClearAndRecycle();
        return completion;
    }
}
