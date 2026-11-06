/*
 * Copyright 2024 The Netty Project
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
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Transport.Channels;

/**
 * A registration for IO.
 *
 */
public sealed class IoRegistration
{
    private readonly CompletionIoHandler _handler;
    private readonly TaskCompletionSource _unregistered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _valid = 1;

    internal IoRegistration(CompletionIoHandler handler, IIoHandle handle)
    {
        _handler = handler;
        Handle = handle;
    }

    internal IIoHandle Handle { get; }
    public IEventLoop EventLoop => _handler.Owner;

    /**
     * Returns {@code true} if the registration is still valid. Once {@link #cancel()} is called this
     * will return {@code false}.
     *
     * @return  valid.
     */
    public bool IsValid => Volatile.Read(ref _valid) != 0;

    /// <summary>
    /// Publishes a completed CLR operation. The caller owns the completion payload and may release
    /// it when the returned task finishes. The result is false if the registration was invalidated
    /// before the callback ran.
    /// </summary>
    public async Task<bool> PublishAsync(object completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        if (!IsValid) return false;
        bool delivered = false;
        await _handler.PostAsync(this, () =>
        {
            if (IsValid)
            {
                Handle.HandleCompletion(this, completion);
                delivered = true;
            }
            return (Task.CompletedTask, delivered);
        }).ConfigureAwait(false);
        return delivered;
    }

    /**
     * Cancel the registration.
     *
     * @return {@code true} if cancellation was successful, {@code false} otherwise.
     */
    /// <summary>Invalidates the registration and waits for its owner to notify the handle.</summary>
    public async Task<bool> CancelAsync()
    {
        if (!Invalidate()) return false;
        if (EventLoop.InEventLoop())
            _handler.Unregister(this);
        else
        {
            try
            {
                await _handler.PostAsync(this, () =>
                {
                    _handler.Unregister(this);
                    return (Task.CompletedTask, false);
                }).ConfigureAwait(false);
            }
            catch (RejectedExecutionException) { } // Final owner cleanup will unregister it.
        }
        await _unregistered.Task.ConfigureAwait(false);
        return true;
    }

    internal bool Invalidate() => Interlocked.Exchange(ref _valid, 0) != 0;

    internal void CompleteUnregistration(Exception error = null)
    {
        if (error == null) _unregistered.TrySetResult();
        else
        {
            _unregistered.TrySetException(error);
            _ = _unregistered.Task.Exception;
        }
    }
}
