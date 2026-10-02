/*
 * Copyright 2015 The Netty Project
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

using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Common;

/// <summary>Resolves an input asynchronously using a producer-owned Task.</summary>
/// <remarks>
/// The mapping owns completion; callers receive only its Task. Task does not bind continuations
/// to a Netty executor. Consumers must dispatch executor-owned state changes explicitly.
/// Input contravariance is supported; the output is invariant because Task&lt;T&gt; is invariant.
/// </remarks>
public interface IAsyncMapping<in TInput, TOutput>
{
    // Upstream contract reference; the CLR completion ownership is documented below.
    /**
     * Returns the {@link Future} that will provide the result of the mapping. The given {@link Promise} will
     * be fulfilled when the result is available.
     */
    // CLR adaptation: the mapper owns its Task/TCS instead of completing a caller-provided Promise.
    /// <summary>Returns a non-null Task that supplies the mapping result.</summary>
    /// <remarks>
    /// The Task may already be complete. Null input/result handling is provider-specific;
    /// SNI consumers can pass a null hostname to select their default configuration.
    /// The token requests cooperative producer cancellation. Canceling a WaitAsync wait
    /// does not cancel this mapping. Providers can throw during invocation or fault the
    /// returned Task; consumers must handle both paths. A requested token alone does not
    /// imply a canceled result if the provider successfully completes its work.
    /// </remarks>
    Task<TOutput> MapAsync(TInput input, CancellationToken cancellationToken = default);
}
