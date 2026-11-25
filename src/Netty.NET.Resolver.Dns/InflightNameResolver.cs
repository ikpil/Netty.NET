/*
 * Copyright 2016 The Netty Project
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

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Resolver.Dns;

// FIXME(trustin): Find a better name and move it to the 'resolver' module.
// CLR: the package-private final class is internal sealed and implements Resolver's INameResolver<T> (Resolver API
// policy, docs/resolver-porting-manifest.json contractPolicy): resolve(inetHost)/resolveAll(inetHost) are
// ResolveAsync/ResolveAllAsync with a CancellationToken, and the resolve(inetHost, Promise) overloads fold into them;
// close() is Dispose(). Future<List<T>> is Task<IReadOnlyList<T>> (SimpleNameResolver). The caller's Promise is a
// TaskCompletionSource with RunContinuationsAsynchronously whose CancellationToken stands for Future.cancel(...)
// (DnsNameResolver.NewPromise). ConcurrentMap is ConcurrentDictionary: putIfAbsent(k, v), which returns the previous
// value or null, is GetOrAdd(k, v), which returns "the existing value for the key if the key is already in the
// dictionary, or the new value" (https://learn.microsoft.com/en-us/dotnet/api/system.collections.concurrent.concurrentdictionary-2.getoradd?view=net-10.0),
// so a returned value other than the caller's promise is the early promise; remove(k) is TryRemove. The delegate
// resolves through its Task-returning methods, whose result is transferred to the promise as the delegate's own
// promise would complete it; the listeners are continuations on Common's EventExecutorTaskScheduler over executor (the
// DefaultPromise.notifyListeners() timing, src/Netty.NET.Common/Concurrent/EventExecutorTaskScheduler.cs). The
// unchecked casts between Promise<U> and Promise<T>/Promise<List<T>> go through object. 'delegate' is the C# keyword,
// so the field is @delegate.
internal sealed class InflightNameResolver<T> : INameResolver<T>
{

    private readonly IEventExecutor executor;
    private readonly INameResolver<T> @delegate;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<T>> resolvesInProgress;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IReadOnlyList<T>>> resolveAllsInProgress;
    private readonly EventExecutorTaskScheduler listenerScheduler;

    internal InflightNameResolver(IEventExecutor executor, INameResolver<T> @delegate,
                         ConcurrentDictionary<string, TaskCompletionSource<T>> resolvesInProgress,
                         ConcurrentDictionary<string, TaskCompletionSource<IReadOnlyList<T>>> resolveAllsInProgress)
    {

        this.executor = ObjectUtil.CheckNotNull(executor, "executor");
        this.@delegate = ObjectUtil.CheckNotNull(@delegate, "delegate");
        this.resolvesInProgress = ObjectUtil.CheckNotNull(resolvesInProgress, "resolvesInProgress");
        this.resolveAllsInProgress = ObjectUtil.CheckNotNull(resolveAllsInProgress, "resolveAllsInProgress");
        listenerScheduler = new EventExecutorTaskScheduler(() => this.executor);
    }

    public Task<T> ResolveAsync(string inetHost, CancellationToken cancellationToken = default)
    {
        return Resolve(resolvesInProgress, inetHost, DnsNameResolver.NewPromise<T>(cancellationToken), false,
                cancellationToken).Task;
    }

    public Task<IReadOnlyList<T>> ResolveAllAsync(string inetHost, CancellationToken cancellationToken = default)
    {
        return Resolve(resolveAllsInProgress, inetHost,
                DnsNameResolver.NewPromise<IReadOnlyList<T>>(cancellationToken), true, cancellationToken).Task;
    }

    public void Dispose()
    {
        @delegate.Dispose();
    }

    private TaskCompletionSource<U> Resolve<U>(
            ConcurrentDictionary<string, TaskCompletionSource<U>> resolveMap,
            string inetHost, TaskCompletionSource<U> promise, bool resolveAll, CancellationToken cancellationToken)
    {

        TaskCompletionSource<U> earlyPromise = resolveMap.GetOrAdd(inetHost, promise);
        if (earlyPromise != promise)
        {
            // Name resolution for the specified inetHost is in progress already.
            if (earlyPromise.Task.IsCompleted)
            {
                TransferResult(earlyPromise.Task, promise);
            }
            else
            {
                _ = earlyPromise.Task.ContinueWith(f => TransferResult(f, promise),
                        CancellationToken.None, TaskContinuationOptions.None, listenerScheduler);
            }
        }
        else
        {
            try
            {
                if (resolveAll)
                {
                    TaskCompletionSource<IReadOnlyList<T>> castPromise =
                            (TaskCompletionSource<IReadOnlyList<T>>) (object) promise; // U is List<T>
                    Complete(@delegate.ResolveAllAsync(inetHost, cancellationToken), castPromise);
                }
                else
                {
                    TaskCompletionSource<T> castPromise = (TaskCompletionSource<T>) (object) promise; // U is T
                    Complete(@delegate.ResolveAsync(inetHost, cancellationToken), castPromise);
                }
            }
            finally
            {
                if (promise.Task.IsCompleted)
                {
                    resolveMap.TryRemove(inetHost, out _);
                }
                else
                {
                    _ = promise.Task.ContinueWith(f => resolveMap.TryRemove(inetHost, out _),
                            CancellationToken.None, TaskContinuationOptions.None, listenerScheduler);
                }
            }
        }

        return promise;
    }

    // CLR: delegate.resolve(inetHost, promise) completes the passed promise; the CLR delegate returns its Task, whose
    // result is transferred to the promise at once when it is already complete and otherwise when it completes.
    private void Complete<V>(Task<V> future, TaskCompletionSource<V> promise)
    {
        if (future.IsCompleted)
        {
            TransferResult(future, promise);
        }
        else
        {
            _ = future.ContinueWith(f => TransferResult(f, promise),
                    CancellationToken.None, TaskContinuationOptions.None, listenerScheduler);
        }
    }

    private static void TransferResult<V>(Task<V> src, TaskCompletionSource<V> dst)
    {
        if (src.IsCompletedSuccessfully)
        {
            dst.TrySetResult(src.Result);
        }
        else
        {
            dst.TrySetException(DnsNameResolver.Cause(src));
        }
    }

    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + '(' + @delegate + ')';
    }
}
