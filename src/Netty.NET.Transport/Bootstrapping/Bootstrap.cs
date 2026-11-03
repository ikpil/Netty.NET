/*
 * Copyright 2012 The Netty Project
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
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Resolver;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Bootstrapping;

/// <summary>Creates and initializes a channel before registration, address resolution and connect/bind.</summary>
/**
 * A {@link Bootstrap} that makes it easy to bootstrap a {@link Channel} to use
 * for clients.
 *
 * <p>The {@link #bind()} methods are useful in combination with connectionless transports such as datagram (UDP).
 * For regular TCP connections, please use the provided {@link #connect()} methods.</p>
 */
public class Bootstrap
{
    public Bootstrap() => Config = new BootstrapConfig(this);
    public BootstrapConfig Config { get; }
    internal IEventLoopGroup ConfiguredGroup => _group;
    internal IEventLoop ConfiguredEventLoop => _registrationLoop;
    internal Func<IChannel> ConfiguredFactory => _factory;
    internal IChannelHandler ConfiguredHandler => _handler;
    internal EndPoint ConfiguredLocalAddress => _local;
    internal EndPoint ConfiguredRemoteAddress => _remote;
    internal object ConfiguredResolver => _disableResolver ? null : _resolverConfiguration.Group;
    private volatile IEventLoopGroup _group;
    private volatile IEventLoop _registrationLoop;
    private volatile Func<IChannel> _factory;
    private volatile IChannelHandler _handler;
    private volatile EndPoint _remote;
    private volatile EndPoint _local;
    private volatile bool _disableResolver;
    // Publish the resolver identity and its typed resolution delegate together.
    private sealed record ResolverConfiguration(object Group, Func<IEventExecutor, EndPoint, CancellationToken, Task<EndPoint>> Resolve);
    private volatile ResolverConfiguration _resolverConfiguration = new(DefaultAddressResolverGroup.INSTANCE, DefaultResolveAsync);
    private readonly OrderedDictionary<ChannelOption, object> _options = new();
    private readonly object _optionsGate = new();
    private readonly ConcurrentDictionary<IAttributeKey, (object Value, Action<IChannel> Apply)> _attributes = new();
    internal KeyValuePair<ChannelOption, object>[] NewOptionsArray() { lock (_optionsGate) return _options.ToArray(); }
    internal KeyValuePair<IAttributeKey, object>[] NewAttributesArray()
        => _attributes.Select(pair => new KeyValuePair<IAttributeKey, object>(pair.Key, pair.Value.Value)).ToArray();
    // Select the configured owner without pretending that a native EventLoop is an EventLoopGroup.
    internal IEventLoop NextEventLoop() => _registrationLoop ?? (_group ?? throw new InvalidOperationException("Group not set.")).Next();
    /**
     * The {@link EventLoopGroup} which is used to handle all the events for the to-be-created
     * {@link Channel}
     */
    public Bootstrap Group(IEventLoopGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (_group != null)
            throw new InvalidOperationException("Group already set.");
        _group = group;
        return this;
    }
    /**
     * @deprecated Use {@link #channelFactory(io.netty.channel.ChannelFactory)} instead.
     */
    /**
     * {@link io.netty.channel.ChannelFactory} which is used to create {@link Channel} instances from
     * when calling {@link #bind()}. This method is usually only used if {@link #channel(Class)}
     * is not working for you because of some more complex needs. If your {@link Channel} implementation
     * has a no-args constructor, its highly recommend to just use {@link #channel(Class)} to
     * simplify your code.
     */
    public Bootstrap ChannelFactory(Func<IChannel> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (_factory != null)
            throw new InvalidOperationException("Channel factory already set.");
        _factory = factory;
        return this;
    }
    public Bootstrap Channel<T>() where T : IChannel, new() => ChannelFactory(() => new T());
    /**
     * the {@link ChannelHandler} to use for serving the requests.
     */
    public Bootstrap Handler(IChannelHandler handler) { ArgumentNullException.ThrowIfNull(handler); _handler = handler; return this; }
    /**
     * The {@link SocketAddress} to connect to once the {@link #connect()} method
     * is called.
     */
    public Bootstrap RemoteAddress(EndPoint address) { _remote = address ?? throw new ArgumentNullException(nameof(address)); return this; }
    /**
     * @see #remoteAddress(SocketAddress)
     */
    public Bootstrap RemoteAddress(string host, int port) => RemoteAddress(new DnsEndPoint(host, port));
    /**
     * The {@link SocketAddress} which is used to bind the local "end" to.
     */
    /**
     * @see #localAddress(SocketAddress)
     */
    public Bootstrap LocalAddress(EndPoint address) { _local = address ?? throw new ArgumentNullException(nameof(address)); return this; }
    /**
     * Allow to specify a {@link ChannelOption} which is used for the {@link Channel} instances once they got
     * created. Use a value of {@code null} to remove a previous set {@link ChannelOption}.
     */
    public Bootstrap Option<T>(ChannelOption<T> option, T value)
    { ArgumentNullException.ThrowIfNull(option); lock (_optionsGate) { if (value is null) _options.Remove(option); else _options[option] = value; } return this; }
    /**
     * Allow to specify an initial attribute of the newly created {@link Channel}.  If the {@code value} is
     * {@code null}, the attribute of the specified {@code key} is removed.
     */
    public Bootstrap Attr<T>(AttributeKey<T> key, T value) where T : class
    {
        ArgumentNullException.ThrowIfNull(key);
        if (value == null) _attributes.TryRemove(key, out _);
        else _attributes[key] = (value, channel => channel.Attr(key).Set(value));
        return this;
    }
    /**
     * Disables address name resolution. Name resolution may be re-enabled with
     * {@link Bootstrap#resolver(AddressResolverGroup)}
     */
    public Bootstrap DisableResolver() { _disableResolver = true; return this; }
    /**
     * Sets the {@link NameResolver} which will resolve the address of the unresolved named address.
     *
     * @param resolver the {@link NameResolver} for this {@code Bootstrap}; may be {@code null}, in which case a default
     *                 resolver will be used
     *
     * @see io.netty.resolver.DefaultAddressResolverGroup
     */
    public Bootstrap Resolver<T>(AddressResolverGroup<T> group) where T : EndPoint
    {
        if (group == null)
        {
            _resolverConfiguration = new ResolverConfiguration(DefaultAddressResolverGroup.INSTANCE, DefaultResolveAsync);
        }
        else
        {
            _resolverConfiguration = new ResolverConfiguration(group, async (executor, address, token) =>
            {
                var resolver = group.GetResolver(executor);
                return !resolver.IsSupported(address) || resolver.IsResolved(address) ? address : await resolver.ResolveAsync(address, token).ConfigureAwait(false);
            });
        }
        _disableResolver = false;
        return this;
    }
    private static async Task<EndPoint> DefaultResolveAsync(IEventExecutor executor, EndPoint address, CancellationToken token)
    {
        var resolver = DefaultAddressResolverGroup.INSTANCE.GetResolver(executor);
        return !resolver.IsSupported(address) || resolver.IsResolved(address) ? address : await resolver.ResolveAsync(address, token).ConfigureAwait(false);
    }
    /**
     * Validate all the parameters. Sub-classes may override this, but should
     * call the super method in that case.
     */
    public Bootstrap Validate()
    {
        if (_group == null)
            throw new InvalidOperationException("Group not set.");
        if (_factory == null)
            throw new InvalidOperationException("Channel factory not set.");
        if (_handler == null)
            throw new InvalidOperationException("Handler not set.");
        return this;
    }
    /**
     * Returns a deep clone of this bootstrap which has the identical configuration.  This method is useful when making
     * multiple {@link Channel}s with similar settings.  Please note that this method does not clone the
     * {@link EventLoopGroup} deeply but shallowly, making the group a shared resource.
     */
    public Bootstrap Clone()
    {
        var copy = new Bootstrap { _group = _group, _registrationLoop = _registrationLoop, _factory = _factory, _handler = _handler, _remote = _remote, _local = _local, _resolverConfiguration = _resolverConfiguration, _disableResolver = _disableResolver };
        foreach (var option in NewOptionsArray())
            copy._options.Add(option.Key, option.Value);
        foreach (var pair in _attributes) copy._attributes[pair.Key] = pair.Value;
        return copy;
    }
    // CLR: EventLoop is not an EventLoopGroup facade. An explicit registration owner provides
    // the original clone(eventLoop) behavior while keeping group lifecycle ownership shared.
    /**
     * Returns a deep clone of this bootstrap which has the identical configuration except that it uses
     * the given {@link EventLoopGroup}. This method is useful when making multiple {@link Channel}s with similar
     * settings.
     */
    public Bootstrap Clone(IEventLoop eventLoop)
    {
        ArgumentNullException.ThrowIfNull(eventLoop);
        var copy = Clone();
        copy._registrationLoop = eventLoop;
        return copy;
    }
    /**
     * Create a new {@link Channel} and register it with an {@link EventLoop}.
     */
    public Task<IChannel> RegisterAsync(CancellationToken cancellationToken = default) { Validate(); return InitAndRegisterAsync(cancellationToken); }
    private async Task<IChannel> InitAndRegisterAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var channel = _factory();
        try
        {
            channel.Pipeline.AddLast(_handler);
            channel.Config.SetOptions(NewOptionsArray());
            foreach (var attribute in _attributes.Values.ToArray())
                attribute.Apply(channel);
            ChannelInitializerExtensions.Notify(ChannelInitializerExtensions.Snapshot(), channel,
                static (extension, initialized) => extension.PostInitializeClientChannel(initialized));
            await (_registrationLoop == null ? _group.RegisterAsync(channel, token) : _registrationLoop.RegisterAsync(channel, token)).ConfigureAwait(false);
            return channel;
        }
        catch { await CleanupAsync(channel).ConfigureAwait(false); throw; }
    }
    /**
     * Connect a {@link Channel} to the remote peer.
     */
    public Task<IChannel> ConnectAsync(CancellationToken cancellationToken = default)
    { Validate(); if (_remote == null) throw new InvalidOperationException("Remote address not set."); return ConnectAsync(_remote, _local, cancellationToken); }
    public async Task<IChannel> ConnectAsync(EndPoint remote, EndPoint local = null, CancellationToken cancellationToken = default)
    {
        Validate();
        ArgumentNullException.ThrowIfNull(remote);
        var channel = await InitAndRegisterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var resolved = remote;
            if (!_disableResolver)
            {
                var resolving = _resolverConfiguration.Resolve(channel.EventLoop, remote, cancellationToken);
                try { resolved = await resolving.WaitAsync(cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Cancelling a connect must not wait for a third-party resolver to honor cancellation.
                    // Its producer remains independent; observe a later failure without capturing the channel.
                    _ = resolving.ContinueWith(static failed => { _ = failed.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    throw;
                }
            }
            await channel.ConnectAsync(resolved, local, cancellationToken).ConfigureAwait(false);
            return channel;
        }
        catch { await CleanupAsync(channel).ConfigureAwait(false); throw; }
    }
    /**
     * Create a new {@link Channel} and bind it.
     */
    public Task<IChannel> BindAsync(CancellationToken cancellationToken = default)
    { Validate(); if (_local == null) throw new InvalidOperationException("Local address not set."); return BindAsync(_local, cancellationToken); }
    /**
     * Create a new {@link Channel} and bind it.
     */
    public Task<IChannel> BindAsync(int port, CancellationToken cancellationToken = default)
        => BindAsync(new IPEndPoint(IPAddress.Any, port), cancellationToken);
    public async Task<IChannel> BindAsync(EndPoint local, CancellationToken cancellationToken = default)
    {
        Validate();
        ArgumentNullException.ThrowIfNull(local);
        var channel = await InitAndRegisterAsync(cancellationToken).ConfigureAwait(false);
        try
        { await channel.BindAsync(local, cancellationToken).ConfigureAwait(false); return channel; }
        catch { await CleanupAsync(channel).ConfigureAwait(false); throw; }
    }
    internal static async Task CleanupAsync(IChannel channel)
    {
        try
        {
            if (!channel.IsRegistered && channel is AbstractChannel native)
                native.CloseForcibly();
            else
                await channel.CloseAsync().ConfigureAwait(false);
        }
        catch { }
    }
}

[Sharable]
public sealed class ActionChannelInitializer<T>(Action<T> initialize) : ChannelInitializer<T> where T : IChannel
{
    private readonly Action<T> _initialize = initialize ?? throw new ArgumentNullException(nameof(initialize));
    protected override void InitChannel(T channel) => _initialize(channel);
}
