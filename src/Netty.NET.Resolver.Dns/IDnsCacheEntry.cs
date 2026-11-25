/*
 * Copyright 2017 The Netty Project
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
using System.Net;

namespace Netty.NET.Resolver.Dns;

/**
 * Represents the results from a previous DNS query which can be cached.
 */
// CLR: InetAddress is IPAddress and Throwable is Exception; the accessors stay methods (porting rule 2).
public interface IDnsCacheEntry
{
    /**
     * Get the resolved address.
     * <p>
     * This may be null if the resolution failed, and in that case {@link #cause()} will describe the failure.
     * @return the resolved address.
     */
    IPAddress Address();

    /**
     * If the DNS query failed this will provide the rational.
     * @return the rational for why the DNS query failed, or {@code null} if the query hasn't failed.
     */
    Exception Cause();
}
