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
 * Exposes the configuration of a {@link Bootstrap}.
 */
public sealed class BootstrapConfig : AbstractBootstrapConfig
{
    private readonly Bootstrap _bootstrap;
    internal BootstrapConfig(Bootstrap bootstrap) => _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
    public override IEventLoopGroup Group => _bootstrap.ConfiguredGroup;
    public IEventLoop RegistrationEventLoop => _bootstrap.ConfiguredEventLoop;
    public override Func<IChannel> ChannelFactory => _bootstrap.ConfiguredFactory;
    public override IChannelHandler Handler => _bootstrap.ConfiguredHandler;
    public override EndPoint LocalAddress => _bootstrap.ConfiguredLocalAddress;
    public override IReadOnlyDictionary<ChannelOption, object> Options => Freeze(_bootstrap.NewOptionsArray());
    public override IReadOnlyDictionary<IAttributeKey, object> Attributes => Freeze(_bootstrap.NewAttributesArray());
    /**
     * Returns the configured remote address or {@code null} if non is configured yet.
     */
    public EndPoint RemoteAddress => _bootstrap.ConfiguredRemoteAddress;
    /**
     * Returns the configured {@link AddressResolverGroup}, {@code null} if resolver was disabled
     * with {@link Bootstrap#disableResolver()}, or the default if not configured yet.
     */
    // The diagnostic identity is heterogeneous because CLR resolver groups have reified endpoint type parameters.
    // Resolution itself remains strongly typed through Bootstrap.Resolver<T>.
    public object Resolver => _bootstrap.ConfiguredResolver;
    protected override void Describe(List<string> values)
    {
        base.Describe(values);
        if (Resolver != null) values.Add("resolver: " + Resolver);
        if (RemoteAddress != null) values.Add("remoteAddress: " + RemoteAddress);
    }
}
