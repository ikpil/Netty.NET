/*
 * Copyright 2014 The Netty Project
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
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec;

/**
 * Raised by {@link MessageAggregator} when aggregation fails due to an unexpected message sequence.
 */
// CLR: IllegalStateException is InvalidOperationException (as IllegalReferenceCountException); serialVersionUID is JVM
// serialization metadata and has no counterpart.
public class MessageAggregationException : InvalidOperationException
{
    public MessageAggregationException()
    {
    }

    public MessageAggregationException(string s) : base(s)
    {
    }

    public MessageAggregationException(string message, Exception cause) : base(message, cause)
    {
    }

    // CLR: IllegalStateException(Throwable) uses cause.toString() as the detail message (Common's
    // ThrowableUtil.ThrowableToString).
    public MessageAggregationException(Exception cause) : base(ThrowableUtil.ThrowableToString(cause), cause)
    {
    }
}
