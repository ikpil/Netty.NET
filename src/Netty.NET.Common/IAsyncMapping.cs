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

using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common;


public interface IAsyncMapping<IN, OUT> {

    /**
     * Returns the {@link Future} that will provide the result of the mapping. The given {@link IPromise} will
     * be fulfilled when the result is available.
     */
    IFuture<OUT> map(IN input, IPromise<OUT> promise);
}
