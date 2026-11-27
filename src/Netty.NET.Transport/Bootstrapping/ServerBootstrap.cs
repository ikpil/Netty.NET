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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Bootstrapping;

/// <summary>Configures the listener and each accepted child before registering it to a worker event loop.</summary>
/**
 * {@link Bootstrap} sub-class which allows easy bootstrap of {@link ServerChannel}
 *
 */
public class ServerBootstrap : AbstractBootstrap
{
    private static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance(typeof(ServerBootstrap));

    public ServerBootstrap()
    {
        Config = new ServerBootstrapConfig(this);
    }
    public override ServerBootstrapConfig Config { get; }
    internal IEventLoopGroup ConfiguredGroup => _parentGroup;
    internal IEventLoopGroup ConfiguredChildGroup => _childGroup;
    internal Func<IChannel> ConfiguredFactory => _factory;
    internal IChannelHandler ConfiguredHandler => _handler;
    internal IChannelHandler ConfiguredChildHandler => _childHandler;
    internal EndPoint ConfiguredLocalAddress => _local;
    internal KeyValuePair<ChannelOption, object>[] NewOptionsArray() { lock (_optionsGate) return _options.ToArray(); }
    internal KeyValuePair<ChannelOption, object>[] NewChildOptionsArray() { lock (_optionsGate) return _childOptions.ToArray(); }
    internal KeyValuePair<IAttributeKey, object>[] NewAttributesArray()
    {
        return _attributes.Select(pair => new KeyValuePair<IAttributeKey, object>(pair.Key, pair.Value.Value)).ToArray();
    }
    internal KeyValuePair<IAttributeKey, object>[] NewChildAttributesArray()
    {
        return _childAttributes.Select(pair => new KeyValuePair<IAttributeKey, object>(pair.Key, pair.Value.Value)).ToArray();
    }
    private volatile IEventLoopGroup _parentGroup;
    private volatile IEventLoopGroup _childGroup;
    private volatile Func<IChannel> _factory;
    private volatile IChannelHandler _handler;
    private volatile IChannelHandler _childHandler;
    private volatile EndPoint _local;
    // The order in which ChannelOptions are applied is important they may depend on each other for validation
    // purposes.
    private readonly OrderedDictionary<ChannelOption, object> _options = new();
    // The order in which child ChannelOptions are applied is important they may depend on each other for validation
    // purposes.
    private readonly OrderedDictionary<ChannelOption, object> _childOptions = new();
    private readonly object _optionsGate = new();
    private readonly ConcurrentDictionary<IAttributeKey, (object Value, Action<IChannel> Apply)> _attributes = new();
    private readonly ConcurrentDictionary<IAttributeKey, (object Value, Action<IChannel> Apply)> _childAttributes = new();
    /**
     * Specify the {@link EventLoopGroup} which is used for the parent (acceptor) and the child (client).
     */
    public ServerBootstrap Group(IEventLoopGroup group)
    {
        return Group(group, group);
    }

    /**
     * Set the {@link EventLoopGroup} for the parent (acceptor) and the child (client). These
     * {@link EventLoopGroup}'s are used to handle all the events and IO for {@link ServerChannel} and
     * {@link Channel}'s.
     * <p>
     * <strong>Important:</strong> Usually this is only useful for advanced use-cases and usually
     * {@link #group(EventLoopGroup)} is the preferred way to configure the group.
     */
    public ServerBootstrap Group(IEventLoopGroup parentGroup, IEventLoopGroup childGroup)
    {
        // AbstractBootstrap.group(EventLoopGroup)
        ObjectUtil.CheckNotNull(parentGroup, "group");
        if (_parentGroup != null)
        {
            throw new InvalidOperationException("group set already");
        }
        _parentGroup = parentGroup;
        if (_childGroup != null)
        {
            throw new InvalidOperationException("childGroup set already");
        }
        _childGroup = ObjectUtil.CheckNotNull(childGroup, "childGroup");
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
    public ServerBootstrap ChannelFactory(Func<IChannel> channelFactory)
    {
        ObjectUtil.CheckNotNull(channelFactory, "channelFactory");
        if (_factory != null)
        {
            throw new InvalidOperationException("channelFactory set already");
        }

        _factory = channelFactory;
        return this;
    }
    /**
     * The {@link Class} which is used to create {@link Channel} instances from.
     * You either use this or {@link #channelFactory(io.netty.channel.ChannelFactory)} if your
     * {@link Channel} implementation has no no-args constructor.
     */
    // CLR: ServerBootstrap's channel type is ServerChannel, as AbstractBootstrap<ServerBootstrap, ServerChannel>.
    public ServerBootstrap Channel(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type channelClass)
    {
        // CLR: check separately so the trimming annotation on channelClass reaches the reflective factory (IL2072).
        ObjectUtil.CheckNotNull(channelClass, "channelClass");
        return ChannelFactory(new ReflectiveChannelFactory<IServerChannel>(channelClass).NewChannel);
    }
    // CLR: the generic form supplies the class at compile time and uses the same reflective factory.
    public ServerBootstrap Channel<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>()
        where T : IServerChannel, new()
    {
        return Channel(typeof(T));
    }
    /**
     * the {@link ChannelHandler} to use for serving the requests.
     */
    public ServerBootstrap Handler(IChannelHandler handler)
    {
        _handler = ObjectUtil.CheckNotNull(handler, "handler");
        return this;
    }
    /**
     * Set the {@link ChannelHandler} which is used to serve the request for the {@link Channel}'s.
     */
    public ServerBootstrap ChildHandler(IChannelHandler childHandler)
    {
        _childHandler = ObjectUtil.CheckNotNull(childHandler, "childHandler");
        return this;
    }
    /**
     * The {@link SocketAddress} which is used to bind the local "end" to.
     */
    public ServerBootstrap LocalAddress(EndPoint localAddress)
    {
        _local = localAddress;
        return this;
    }
    /**
     * @see #localAddress(SocketAddress)
     */
    // CLR: new InetSocketAddress(inetPort) is the wildcard address; as in BindAsync(int), the wildcard is IPAddress.Any.
    public ServerBootstrap LocalAddress(int inetPort)
    {
        return LocalAddress(new IPEndPoint(IPAddress.Any, inetPort));
    }
    /**
     * @see #localAddress(SocketAddress)
     */
    public ServerBootstrap LocalAddress(string inetHost, int inetPort)
    {
        return LocalAddress(AbstractBootstrap.SocketAddress(inetHost, inetPort));
    }
    /**
     * @see #localAddress(SocketAddress)
     */
    public ServerBootstrap LocalAddress(IPAddress inetHost, int inetPort)
    {
        return LocalAddress(AbstractBootstrap.InetSocketAddress(inetHost, inetPort));
    }
    /**
     * Allow to specify a {@link ChannelOption} which is used for the {@link Channel} instances once they got
     * created. Use a value of {@code null} to remove a previous set {@link ChannelOption}.
     */
    public ServerBootstrap Option<T>(ChannelOption<T> option, T value)
    {
        ObjectUtil.CheckNotNull(option, "option");
        lock (_optionsGate)
        {
            if (value is null)
            {
                _options.Remove(option);
            }
            else
            {
                _options[option] = value;
            }
        }
        return this;
    }
    /**
     * Allow to specify a {@link ChannelOption} which is used for the {@link Channel} instances once they get created
     * (after the acceptor accepted the {@link Channel}). Use a value of {@code null} to remove a previous set
     * {@link ChannelOption}.
     */
    public ServerBootstrap ChildOption<T>(ChannelOption<T> childOption, T value)
    {
        ObjectUtil.CheckNotNull(childOption, "childOption");
        lock (_optionsGate)
        {
            if (value is null)
            {
                _childOptions.Remove(childOption);
            }
            else
            {
                _childOptions[childOption] = value;
            }
        }
        return this;
    }
    /**
     * Allow to specify an initial attribute of the newly created {@link Channel}.  If the {@code value} is
     * {@code null}, the attribute of the specified {@code key} is removed.
     */
    public ServerBootstrap Attr<T>(AttributeKey<T> key, T value) where T : class
    {
        ObjectUtil.CheckNotNull(key, "key");
        if (value == null)
        {
            _attributes.TryRemove(key, out _);
        }
        else
        {
            _attributes[key] = (value, channel => channel.Attr(key).Set(value));
        }
        return this;
    }
    /**
     * Set the specific {@link AttributeKey} with the given value on every child {@link Channel}. If the value is
     * {@code null} the {@link AttributeKey} is removed
     */
    public ServerBootstrap ChildAttr<T>(AttributeKey<T> childKey, T value) where T : class
    {
        ObjectUtil.CheckNotNull(childKey, "childKey");
        if (value == null)
        {
            _childAttributes.TryRemove(childKey, out _);
        }
        else
        {
            _childAttributes[childKey] = (value, channel => channel.Attr(childKey).Set(value));
        }
        return this;
    }
    /**
     * Validate all the parameters. Sub-classes may override this, but should
     * call the super method in that case.
     */
    public ServerBootstrap Validate()
    {
        // AbstractBootstrap.validate()
        if (_parentGroup == null)
        {
            throw new InvalidOperationException("group not set");
        }
        if (_factory == null)
        {
            throw new InvalidOperationException("channel or channelFactory not set");
        }
        if (_childHandler == null)
        {
            throw new InvalidOperationException("childHandler not set");
        }
        if (_childGroup == null)
        {
            Logger.Warn("childGroup is not set. Using parentGroup instead.");
            _childGroup = _parentGroup;
        }
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
        lock (_optionsGate)
        {
            foreach (var pair in _options) copy._options.Add(pair.Key, pair.Value);
            foreach (var pair in _childOptions) copy._childOptions.Add(pair.Key, pair.Value);
        }
        foreach (var pair in _attributes) copy._attributes[pair.Key] = pair.Value;
        foreach (var pair in _childAttributes) copy._childAttributes[pair.Key] = pair.Value;
        return copy;
    }
    /**
     * Create a new {@link Channel} and bind it.
     */
    public Task<IChannel> BindAsync(CancellationToken cancellationToken = default)
    {
        Validate();
        EndPoint localAddress = _local;
        if (localAddress == null)
        {
            throw new InvalidOperationException("localAddress not set");
        }
        return BindAsync(localAddress, cancellationToken);
    }
    /**
     * Create a new {@link Channel} and bind it.
     */
    // CLR: AbstractBootstrap.bind(int) binds new InetSocketAddress(inetPort), the wildcard address; as in
    // Bootstrap.BindAsync(int), the wildcard is IPAddress.Any.
    public Task<IChannel> BindAsync(int inetPort, CancellationToken cancellationToken = default)
    {
        return BindAsync(new IPEndPoint(IPAddress.Any, inetPort), cancellationToken);
    }
    /**
     * Create a new {@link Channel} and bind it.
     */
    public Task<IChannel> BindAsync(string inetHost, int inetPort, CancellationToken cancellationToken = default)
    {
        return BindAsync(AbstractBootstrap.SocketAddress(inetHost, inetPort), cancellationToken);
    }
    /**
     * Create a new {@link Channel} and bind it.
     */
    public Task<IChannel> BindAsync(IPAddress inetHost, int inetPort, CancellationToken cancellationToken = default)
    {
        return BindAsync(AbstractBootstrap.InetSocketAddress(inetHost, inetPort), cancellationToken);
    }
    /**
     * Create a new {@link Channel} and bind it.
     */
    public Task<IChannel> BindAsync(EndPoint localAddress, CancellationToken cancellationToken = default)
    {
        Validate();
        EndPoint address = ObjectUtil.CheckNotNull(localAddress, "localAddress");
        // CLR: an already cancelled token fails the operation before a channel is created.
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<IChannel>(cancellationToken);
        }
        Task regFuture = InitAndRegister(cancellationToken, out IChannel channel);
        return AbstractBootstrap.DoBind(regFuture, channel, address, cancellationToken);
    }
    /**
     * Create a new {@link Channel} and register it with an {@link EventLoop}.
     */
    public Task<IChannel> RegisterAsync(CancellationToken cancellationToken = default)
    {
        Validate();
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<IChannel>(cancellationToken);
        }
        Task regFuture = InitAndRegister(cancellationToken, out IChannel channel);
        return Bootstrap.RegisteredChannelAsync(regFuture, channel);
    }

    private Task InitAndRegister(CancellationToken cancellationToken, out IChannel channel)
    {
        return AbstractBootstrap.InitAndRegister(_factory, Init, ch => _parentGroup.RegisterAsync(ch, cancellationToken), out channel);
    }

    internal void Init(IChannel channel)
    {
        AbstractBootstrap.SetChannelOptions(channel, NewOptionsArray(), Logger);
        AbstractBootstrap.SetAttributes(channel, _attributes.Values.Select(attribute => attribute.Apply).ToArray());

        IChannelPipeline p = channel.Pipeline;

        IEventLoopGroup currentChildGroup = _childGroup;
        IChannelHandler currentChildHandler = _childHandler;
        KeyValuePair<ChannelOption, object>[] currentChildOptions = NewChildOptionsArray();
        Action<IChannel>[] currentChildAttrs = _childAttributes.Values.Select(attribute => attribute.Apply).ToArray();
        IReadOnlyList<ChannelInitializerExtension> extensions = AbstractBootstrap.GetInitializerExtensions();

        // CLR: the anonymous ChannelInitializer<Channel>.initChannel(ch) and its Runnable are lambdas.
        p.AddLast(new ActionChannelInitializer<IChannel>(ch =>
        {
            IChannelPipeline pipeline = ch.Pipeline;
            IChannelHandler handler = _handler;
            if (handler != null)
            {
                pipeline.AddLast(handler);
            }

            ch.EventLoop.Execute(() =>
            {
                pipeline.AddLast(new ServerBootstrapAcceptor(
                        ch, currentChildGroup, currentChildHandler, currentChildOptions, currentChildAttrs,
                        extensions));
            });
        }));
        if (extensions.Count != 0 && channel is IServerChannel serverChannel)
        {
            foreach (ChannelInitializerExtension extension in extensions)
            {
                try
                {
                    extension.PostInitializeServerListenerChannel(serverChannel);
                }
                catch (Exception e)
                {
                    Logger.Warn("Exception thrown from postInitializeServerListenerChannel", e);
                }
            }
        }
    }

    private sealed class ServerBootstrapAcceptor : ChannelInboundHandlerAdapter
    {
        private readonly IEventLoopGroup _childGroup;
        private readonly IChannelHandler _childHandler;
        private readonly KeyValuePair<ChannelOption, object>[] _childOptions;
        private readonly Action<IChannel>[] _childAttrs;
        private readonly Action _enableAutoReadTask;
        private readonly IReadOnlyList<ChannelInitializerExtension> _extensions;

        internal ServerBootstrapAcceptor(
                IChannel channel, IEventLoopGroup childGroup, IChannelHandler childHandler,
                KeyValuePair<ChannelOption, object>[] childOptions, Action<IChannel>[] childAttrs,
                IReadOnlyList<ChannelInitializerExtension> extensions)
        {
            _childGroup = childGroup;
            _childHandler = childHandler;
            _childOptions = childOptions;
            _childAttrs = childAttrs;
            _extensions = extensions;

            // Task which is scheduled to re-enable auto-read.
            // It's important to create this Runnable before we try to submit it as otherwise the URLClassLoader may
            // not be able to load the class because of the file limit it already reached.
            //
            // See https://github.com/netty/netty/issues/1328
            _enableAutoReadTask = () => channel.Config.SetAutoRead(true);
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            IChannel child = (IChannel)msg;

            child.Pipeline.AddLast(_childHandler);

            try
            {
                // CLR: config validation replaces AbstractBootstrap.setChannelOptions; a rejected option still
                // force-closes the child as the source catch block does.
                child.Config.SetOptions(_childOptions);
            }
            catch (Exception cause)
            {
                ForceClose(child, cause);
                return;
            }
            foreach (var childAttr in _childAttrs)
                childAttr(child);

            if (_extensions.Count != 0)
            {
                foreach (ChannelInitializerExtension extension in _extensions)
                {
                    try
                    {
                        extension.PostInitializeServerChildChannel(child);
                    }
                    catch (Exception e)
                    {
                        Logger.Warn("Exception thrown from postInitializeServerChildChannel", e);
                    }
                }
            }

            try
            {
                _ = ForceCloseIfFailed(child, _childGroup.RegisterAsync(child));
            }
            catch (Exception t)
            {
                ForceClose(child, t);
            }
        }

        // CLR: awaiting the registration Task replaces the source ChannelFutureListener.
        private static async Task ForceCloseIfFailed(IChannel child, Task future)
        {
            try
            {
                await future.ConfigureAwait(false);
            }
            catch (Exception cause)
            {
                ForceClose(child, cause);
            }
        }

        private static void ForceClose(IChannel child, Exception t)
        {
            // CLR: AbstractChannel folds Unsafe.closeForcibly() into the channel.
            if (child is AbstractChannel unsafeChannel)
                unsafeChannel.CloseForcibly();
            else
                _ = child.CloseAsync();
            Logger.Warn("Failed to register an accepted channel: {}", child, t);
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            IChannelConfig config = ctx.Channel.Config;
            if (config.IsAutoRead())
            {
                // stop accept new connections for 1 second to allow the channel to recover
                // See https://github.com/netty/netty/issues/1328
                config.SetAutoRead(false);
                _ = ctx.Channel.EventLoop.ScheduleAsync(_enableAutoReadTask, TimeSpan.FromSeconds(1));
            }
            // still let the exceptionCaught event flow through the pipeline to give the user
            // a chance to do something with it
            ctx.FireExceptionCaught(cause);
        }
    }
}
