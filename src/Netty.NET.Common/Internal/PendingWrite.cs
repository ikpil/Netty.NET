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
using Netty.NET.Common.Concurrent;
using Void = Netty.NET.Common.Concurrent.Void;

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
    public static PendingWrite newInstance(object msg, IPromise<Void> promise)
    {
        PendingWrite pending = RECYCLER.get();
        pending._msg = msg;
        pending._promise = promise;
        return pending;
    }

    private readonly IObjectPoolHandle<PendingWrite> _handle;
    private object _msg;
    private IPromise<Void> _promise;

    private PendingWrite(IObjectPoolHandle<PendingWrite> handle)
    {
        _handle = handle;
    }

    /**
     * Clear and recycle this instance.
     */
    public bool recycle()
    {
        _msg = null;
        _promise = null;
        _handle.recycle(this);
        return true;
    }

    /**
     * Fails the underlying {@link IPromise} with the given cause and recycle this instance.
     */
    public bool failAndRecycle(Exception cause)
    {
        ReferenceCountUtil.release(_msg);
        if (_promise != null)
        {
            _promise.setFailure(cause);
        }

        return recycle();
    }

    /**
     * Mark the underlying {@link IPromise} successfully and recycle this instance.
     */
    public bool successAndRecycle()
    {
        if (_promise != null)
        {
            _promise.setSuccess(null);
        }

        return recycle();
    }

    public object msg()
    {
        return _msg;
    }

    public IPromise<Void> promise()
    {
        return _promise;
    }

    /**
     * Recycle this instance and return the {@link IPromise}.
     */
    public IPromise<Void> recycleAndGet()
    {
        IPromise<Void> promise = _promise;
        recycle();
        return promise;
    }
}