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

using System;

namespace Netty.NET.Transport.Channels;

/**
 * Default select strategy.
 */
internal sealed class DefaultSelectStrategy : ISelectStrategy
{
    internal static readonly ISelectStrategy INSTANCE = new DefaultSelectStrategy();

    private DefaultSelectStrategy() { }

    public int CalculateStrategy(Func<int> selectSupplier, bool hasTasks)
    {
        return hasTasks ? selectSupplier() : ISelectStrategy.SELECT;
    }
}
