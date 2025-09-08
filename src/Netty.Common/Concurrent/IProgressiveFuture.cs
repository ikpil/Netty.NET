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

/**
 * A {@link Future} which is used to indicate the progress of an operation.
 */
public interface IProgressiveFuture<V> : IFuture<V>
{
    IProgressiveFuture<V> addListener(IGenericFutureListener<IFuture<V>> listener);

    IProgressiveFuture<V> addListeners(params IGenericFutureListener<IFuture<V>>[] listeners);

    IProgressiveFuture<V> removeListener(IGenericFutureListener<IFuture<V>> listener);

    IProgressiveFuture<V> removeListeners(params IGenericFutureListener<IFuture<V>>[] listeners);

    IProgressiveFuture<V> sync();

    IProgressiveFuture<V> syncUninterruptibly();

    IProgressiveFuture<V> await();

    IProgressiveFuture<V> awaitUninterruptibly();
}