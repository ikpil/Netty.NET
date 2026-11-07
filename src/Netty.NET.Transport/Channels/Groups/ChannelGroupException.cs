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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Netty.NET.Transport.Channels.Groups;

/**
 * {@link ChannelException} which holds {@link ChannelFuture}s that failed because of an error.
 */
// CLR aggregation retains the failed channel alongside each original exception.
public sealed class ChannelGroupException : AggregateException, IReadOnlyCollection<KeyValuePair<IChannel, Exception>>
{
    private readonly KeyValuePair<IChannel, Exception>[] _failures;

    public ChannelGroupException(IEnumerable<KeyValuePair<IChannel, Exception>> causes) : this(CopyCauses(causes)) { }

    private ChannelGroupException(KeyValuePair<IChannel, Exception>[] causes)
        : base("One or more channel group operations failed.", causes.Select(pair => pair.Value)) => _failures = causes;

    private static KeyValuePair<IChannel, Exception>[] CopyCauses(IEnumerable<KeyValuePair<IChannel, Exception>> causes)
    {
        ArgumentNullException.ThrowIfNull(causes);
        var copy = causes.ToArray();
        if (copy.Length == 0 || copy.Any(pair => pair.Key == null || pair.Value == null))
            throw new ArgumentException("At least one channel and its failure are required.", nameof(causes));
        return copy;
    }

    public int Count => _failures.Length;

    /**
     * Returns a {@link Iterator} which contains all the {@link Throwable} that was a cause of the failure and the
     * related id of the {@link Channel}.
     */
    public IEnumerator<KeyValuePair<IChannel, Exception>> GetEnumerator() =>
        ((IEnumerable<KeyValuePair<IChannel, Exception>>)_failures).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
