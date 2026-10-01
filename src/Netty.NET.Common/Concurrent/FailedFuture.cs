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
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;


/**
 * The {@link CompleteFuture} which is failed already.  It is
 * recommended to use {@link EventExecutor#newFailedFuture(Throwable)}
 * instead of calling the constructor of this future.
 */
public sealed class FailedFuture<V> : CompleteFuture<V> {

    private readonly Exception _cause;
    public override Task<V> Task { get; }

    /**
     * Creates a new instance.
     *
     * @param executor the {@link EventExecutor} associated with this future
     * @param cause   the cause of failure
     */
    public FailedFuture(IEventExecutor executor, Exception cause) : base(executor) {
        
        _cause = ObjectUtil.checkNotNull(cause, "cause");
        Task = System.Threading.Tasks.Task.FromException<V>(_cause);
    }

    public override Exception cause() {
        return _cause;
    }

    public override bool isSuccess() {
        return false;
    }

    public override IFuture<V> sync() {
        ExceptionDispatchInfo.Capture(_cause).Throw();
        return this;
    }

    public override IFuture<V> syncUninterruptibly() {
        ExceptionDispatchInfo.Capture(_cause).Throw();
        return this;
    }

    public override V getNow() {
        return default;
    }
}

// CLR bridge for callers that use the legacy static factory.
public static class FailedFuture
{
    public static IFuture<T> Create<T>(IEventExecutor executor, Exception cause) => new FailedFuture<T>(executor, cause);
}