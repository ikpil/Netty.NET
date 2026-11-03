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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Bootstrapping;

/**
 * Exposes the configuration of an {@link AbstractBootstrap}.
 */
public abstract class AbstractBootstrapConfig
{
    /**
     * Returns the configured local address or {@code null} if non is configured yet.
     */
    public abstract EndPoint LocalAddress { get; }
    /**
     * Returns the configured {@link ChannelFactory} or {@code null} if non is configured yet.
     */
    public abstract Func<IChannel> ChannelFactory { get; }
    /**
     * Returns the configured {@link ChannelHandler} or {@code null} if non is configured yet.
     */
    public abstract IChannelHandler Handler { get; }
    /**
     * Returns a copy of the configured options.
     */
    public abstract IReadOnlyDictionary<ChannelOption, object> Options { get; }
    /**
     * Returns a copy of the configured attributes.
     */
    public abstract IReadOnlyDictionary<IAttributeKey, object> Attributes { get; }
    /**
     * Returns the configured {@link EventLoopGroup} or {@code null} if non is configured yet.
     */
    public abstract IEventLoopGroup Group { get; }
    protected static IReadOnlyDictionary<TKey, object> Freeze<TKey>(IEnumerable<KeyValuePair<TKey, object>> values)
    {
        var snapshot = new OrderedDictionary<TKey, object>();
        foreach (var pair in values) snapshot.Add(pair.Key, pair.Value);
        return new ReadOnlyDictionary<TKey, object>(snapshot);
    }
    private static string Value(object value) => value is bool boolean ? boolean ? "true" : "false"
        : value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value?.ToString();
    protected static string Map<TKey>(IReadOnlyDictionary<TKey, object> values)
        => "{" + string.Join(", ", values.Select(pair => pair.Key + "=" + Value(pair.Value))) + "}";
    protected virtual void Describe(List<string> values)
    {
        if (Group != null) values.Add("group: " + Group.GetType().Name);
        if (ChannelFactory != null) values.Add("channelFactory: " + ChannelFactory);
        if (LocalAddress != null) values.Add("localAddress: " + LocalAddress);
        var options = Options; if (options.Count != 0) values.Add("options: " + Map(options));
        var attrs = Attributes; if (attrs.Count != 0) values.Add("attrs: " + Map(attrs));
        if (Handler != null) values.Add("handler: " + Handler);
    }
    public override string ToString()
    {
        var values = new List<string>();
        Describe(values);
        return GetType().Name + "(" + string.Join(", ", values) + ")";
    }
}
