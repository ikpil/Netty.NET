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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Bootstrapping;

/// <summary>Configures the listener and each accepted child before registering it to a worker event loop.</summary>
/**
 * {@link Bootstrap} sub-class which allows easy bootstrap of {@link ServerChannel}
 *
 */
public class ServerBootstrap
{
    private IEventLoopGroup _parentGroup;
    private IEventLoopGroup _childGroup;
    private Func<IChannel> _factory;
    private IChannelHandler _handler;
    private IChannelHandler _childHandler;
    private EndPoint _local;
    private readonly Dictionary<ChannelOption, object> _options = new();
    private readonly Dictionary<ChannelOption, object> _childOptions = new();
    private readonly List<Action<IChannel>> _attributes = new();
    private readonly List<Action<IChannel>> _childAttributes = new();
    /**
     * Set the {@link EventLoopGroup} for the parent (acceptor) and the child (client). These
     * {@link EventLoopGroup}'s are used to handle all the events and IO for {@link ServerChannel} and
     * {@link Channel}'s.
     * <p>
     * <strong>Important:</strong> Usually this is only useful for advanced use-cases and usually
     * {@link #group(EventLoopGroup)} is the preferred way to configure the group.
     */
    public ServerBootstrap Group(IEventLoopGroup parent, IEventLoopGroup child = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (_parentGroup != null)
            throw new InvalidOperationException("Group already set.");
        _parentGroup = parent;
        _childGroup = child ?? parent;
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
    public ServerBootstrap ChannelFactory(Func<IChannel> factory)
    { ArgumentNullException.ThrowIfNull(factory); if (_factory != null) throw new InvalidOperationException("Channel factory already set."); _factory = factory; return this; }
    public ServerBootstrap Channel<T>() where T : IChannel, new() => ChannelFactory(() => new T());
    /**
     * the {@link ChannelHandler} to use for serving the requests.
     */
    public ServerBootstrap Handler(IChannelHandler handler) { _handler = handler ?? throw new ArgumentNullException(nameof(handler)); return this; }
    /**
     * Set the {@link ChannelHandler} which is used to serve the request for the {@link Channel}'s.
     */
    public ServerBootstrap ChildHandler(IChannelHandler handler) { _childHandler = handler ?? throw new ArgumentNullException(nameof(handler)); return this; }
    /**
     * The {@link SocketAddress} which is used to bind the local "end" to.
     */
    /**
     * @see #localAddress(SocketAddress)
     */
    public ServerBootstrap LocalAddress(EndPoint address) { _local = address ?? throw new ArgumentNullException(nameof(address)); return this; }
    /**
     * Allow to specify a {@link ChannelOption} which is used for the {@link Channel} instances once they got
     * created. Use a value of {@code null} to remove a previous set {@link ChannelOption}.
     */
    public ServerBootstrap Option<T>(ChannelOption<T> option, T value)
    { ArgumentNullException.ThrowIfNull(option); if (value is null) _options.Remove(option); else _options[option] = value; return this; }
    /**
     * Allow to specify a {@link ChannelOption} which is used for the {@link Channel} instances once they get created
     * (after the acceptor accepted the {@link Channel}). Use a value of {@code null} to remove a previous set
     * {@link ChannelOption}.
     */
    public ServerBootstrap ChildOption<T>(ChannelOption<T> option, T value)
    { ArgumentNullException.ThrowIfNull(option); if (value is null) _childOptions.Remove(option); else _childOptions[option] = value; return this; }
    /**
     * Allow to specify an initial attribute of the newly created {@link Channel}.  If the {@code value} is
     * {@code null}, the attribute of the specified {@code key} is removed.
     */
    public ServerBootstrap Attr<T>(AttributeKey<T> key, T value) where T : class
    { ArgumentNullException.ThrowIfNull(key); _attributes.Add(channel => channel.Attr(key).Set(value)); return this; }
    /**
     * Set the specific {@link AttributeKey} with the given value on every child {@link Channel}. If the value is
     * {@code null} the {@link AttributeKey} is removed
     */
    public ServerBootstrap ChildAttr<T>(AttributeKey<T> key, T value) where T : class
    { ArgumentNullException.ThrowIfNull(key); _childAttributes.Add(channel => channel.Attr(key).Set(value)); return this; }
    /**
     * Validate all the parameters. Sub-classes may override this, but should
     * call the super method in that case.
     */
    public ServerBootstrap Validate()
    {
        if (_parentGroup == null)
            throw new InvalidOperationException("Group not set.");
        if (_factory == null)
            throw new InvalidOperationException("Channel factory not set.");
        if (_childHandler == null)
            throw new InvalidOperationException("Child handler not set.");
        return this;
    }
    /**
     * Returns a deep clone of this bootstrap which has the identical configuration.  This method is useful when making
     * multiple {@link Channel}s with similar settings.  Please note that this method does not clone the
     * {@link EventLoopGroup} deeply but shallowly, making the group a shared resource.
     */
    public ServerBootstrap Clone()
    {
        var copy = new ServerBootstrap { _parentGroup = _parentGroup, _childGroup = _childGroup, _factory = _factory, _handler = _handler, _childHandler = _childHandler, _local = _local };
        foreach (var pair in _options)
            copy._options.Add(pair.Key, pair.Value);
        foreach (var pair in _childOptions)
            copy._childOptions.Add(pair.Key, pair.Value);
        copy._attributes.AddRange(_attributes);
        copy._childAttributes.AddRange(_childAttributes);
        return copy;
    }
    /**
     * Create a new {@link Channel} and bind it.
     */
    public Task<IChannel> BindAsync(CancellationToken cancellationToken = default)
    { Validate(); if (_local == null) throw new InvalidOperationException("Local address not set."); return BindAsync(_local, cancellationToken); }
    public async Task<IChannel> BindAsync(EndPoint address, CancellationToken cancellationToken = default)
    {
        Validate();
        ArgumentNullException.ThrowIfNull(address);
        cancellationToken.ThrowIfCancellationRequested();
        var channel = _factory();
        // Capture child configuration at bind time; later bootstrap mutation must not reconfigure an existing listener.
        var childOptions = new Dictionary<ChannelOption, object>(_childOptions);
        var childAttributes = _childAttributes.ToArray();
        var childHandler = _childHandler;
        var childGroup = _childGroup;
        var parentHandler = _handler;
        try
        {
            channel.Config.SetOptions(_options);
            foreach (var attribute in _attributes)
                attribute(channel);
            channel.Pipeline.AddLast(new ActionChannelInitializer<IChannel>(server =>
            {
                if (parentHandler != null)
                    server.Pipeline.AddLast(parentHandler);
                server.EventLoop.Execute(() => server.Pipeline.AddLast(new Acceptor(childGroup, childHandler, childOptions, childAttributes)));
            }));
            await _parentGroup.RegisterAsync(channel, cancellationToken).ConfigureAwait(false);
            await channel.BindAsync(address, cancellationToken).ConfigureAwait(false);
            return channel;
        }
        catch { await Bootstrap.CleanupAsync(channel).ConfigureAwait(false); throw; }
    }
    private sealed class Acceptor(IEventLoopGroup group, IChannelHandler handler,
        Dictionary<ChannelOption, object> options, Action<IChannel>[] attributes) : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(ChannelHandlerContext context, object message)
        {
            if (message is not IChannel child)
            { ReferenceCountUtil.Release(message); throw new ArgumentException("Expected an accepted channel."); }
            _ = RegisterChildAsync(child, context);
        }
        private async Task RegisterChildAsync(IChannel child, ChannelHandlerContext parent)
        {
            try
            {
                child.Config.SetOptions(options);
                foreach (var attribute in attributes)
                    attribute(child);
                child.Pipeline.AddLast(handler);
                await group.RegisterAsync(child).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                await Bootstrap.CleanupAsync(child).ConfigureAwait(false);
                parent.FireExceptionCaught(error);
            }
        }
    }
}
