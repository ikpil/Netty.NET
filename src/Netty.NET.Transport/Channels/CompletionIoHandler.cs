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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Transport.Channels;

/// <summary>Owns registrations for CLR asynchronous I/O completions on one event loop.</summary>
public class CompletionIoHandler
{
    private static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance(typeof(CompletionIoHandler));
    private readonly Dictionary<IIoHandle, IoRegistration> _registrations = new(ReferenceEqualityComparer.Instance);
    private IEventLoop _owner;
    private Func<IoRegistration, Func<(Task Completion, bool Handled)>, Task> _post;
    private int _count;

    internal IEventLoop Owner => _owner;
    internal int Count => Volatile.Read(ref _count);

    /**
     * Returns {@code true} if the given type is compatible with this {@link IoHandler} and so can be registered,
     * {@code false} otherwise.
     *
     * @param handleType the type of the {@link IoHandle}.
     * @return if compatible of not.
     */
    /// <summary>Returns whether this backend accepts a handle with the specified runtime type.</summary>
    public virtual bool IsCompatible(Type handleType) => typeof(IIoHandle).IsAssignableFrom(handleType);

    internal void Attach(IEventLoop loop, Func<IoRegistration, Func<(Task Completion, bool Handled)>, Task> post)
    {
        if (Interlocked.CompareExchange(ref _owner, loop, null) != null)
            throw new InvalidOperationException("An I/O handler can only be used by one event loop.");
        _post = post;
    }

    internal Task PostAsync(IoRegistration registration, Func<(Task Completion, bool Handled)> dispatch) =>
        _post(registration, dispatch);

    internal Task<IoRegistration> RegisterAsync(IIoHandle handle, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (token.IsCancellationRequested) return Task.FromCanceled<IoRegistration>(token);
        try
        {
            return Owner.InEventLoop() ? Task.FromResult(Register(handle)) : Owner.SubmitAsync(() => Register(handle), token);
        }
        catch (Exception error) { return Task.FromException<IoRegistration>(error); }
    }

    /**
     * Register a {@link IoHandle} for IO.
     *
     * @param handle        the {@link IoHandle} to register.
     * @throws Exception    thrown if an error happens during registration.
     */
    private IoRegistration Register(IIoHandle handle)
    {
        if (Owner.IsShutdown()) throw new RejectedExecutionException("The event loop is shut down.");
        if (!IsCompatible(handle.GetType()))
            throw new ArgumentException("The I/O handler does not support this handle type.", nameof(handle));

        var registration = new IoRegistration(this, handle);
        if (!_registrations.TryAdd(handle, registration))
            throw new InvalidOperationException("The I/O handle is already registered.");
        Volatile.Write(ref _count, _registrations.Count);
        try
        {
            handle.Registered(registration);
            return registration;
        }
        catch
        {
            registration.Invalidate();
            Remove(registration);
            registration.CompleteUnregistration();
            throw;
        }
    }

    internal void Unregister(IoRegistration registration)
    {
        if (!Remove(registration)) return;
        try
        {
            registration.Handle.Unregistered();
            registration.CompleteUnregistration();
        }
        catch (Exception error)
        {
            registration.CompleteUnregistration(error);
            Logger.Warn("An I/O handle failed while being unregistered.", error);
        }
    }

    private bool Remove(IoRegistration registration)
    {
        if (!_registrations.TryGetValue(registration.Handle, out var current) || !ReferenceEquals(current, registration))
            return false;
        _registrations.Remove(registration.Handle);
        Volatile.Write(ref _count, _registrations.Count);
        return true;
    }

    internal void CloseAll()
    {
        foreach (var registration in _registrations.Values.ToArray())
        {
            try
            {
                if (registration.Invalidate()) registration.Handle.Dispose();
            }
            catch (Exception error) { Logger.Warn("An I/O handle failed while closing.", error); }
            finally { Unregister(registration); }
        }
    }
}
