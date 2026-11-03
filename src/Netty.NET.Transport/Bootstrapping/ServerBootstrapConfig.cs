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
using System.Net;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Bootstrapping;

/**
 * Exposes the configuration of a {@link ServerBootstrapConfig}.
 */
public sealed class ServerBootstrapConfig : AbstractBootstrapConfig
{
    private readonly ServerBootstrap _bootstrap;
    internal ServerBootstrapConfig(ServerBootstrap bootstrap) => _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
    public override IEventLoopGroup Group => _bootstrap.ConfiguredGroup;
    public override Func<IChannel> ChannelFactory => _bootstrap.ConfiguredFactory;
    public override IChannelHandler Handler => _bootstrap.ConfiguredHandler;
    public override EndPoint LocalAddress => _bootstrap.ConfiguredLocalAddress;
    public override IReadOnlyDictionary<ChannelOption, object> Options => Freeze(_bootstrap.NewOptionsArray());
    public override IReadOnlyDictionary<IAttributeKey, object> Attributes => Freeze(_bootstrap.NewAttributesArray());
    /**
     * Returns the configured {@link EventLoopGroup} which will be used for the child channels or {@code null}
     * if non is configured yet.
     */
    public IEventLoopGroup ChildGroup => _bootstrap.ConfiguredChildGroup;
    /**
     * Returns the configured {@link ChannelHandler} be used for the child channels or {@code null}
     * if non is configured yet.
     */
    public IChannelHandler ChildHandler => _bootstrap.ConfiguredChildHandler;
    /**
     * Returns a copy of the configured options which will be used for the child channels.
     */
    public IReadOnlyDictionary<ChannelOption, object> ChildOptions => Freeze(_bootstrap.NewChildOptionsArray());
    /**
     * Returns a copy of the configured attributes which will be used for the child channels.
     */
    public IReadOnlyDictionary<IAttributeKey, object> ChildAttributes => Freeze(_bootstrap.NewChildAttributesArray());
    protected override void Describe(List<string> values)
    {
        base.Describe(values);
        if (ChildGroup != null) values.Add("childGroup: " + ChildGroup.GetType().Name);
        var options = ChildOptions; if (options.Count != 0) values.Add("childOptions: " + Map(options));
        var attrs = ChildAttributes; if (attrs.Count != 0) values.Add("childAttrs: " + Map(attrs));
        if (ChildHandler != null) values.Add("childHandler: " + ChildHandler);
    }
}
