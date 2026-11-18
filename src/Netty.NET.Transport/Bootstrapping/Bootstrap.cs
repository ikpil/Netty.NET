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
public class Bootstrap : AbstractBootstrap
{
    private static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance(typeof(Bootstrap));

    public Bootstrap() => Config = new BootstrapConfig(this);
    public override BootstrapConfig Config { get; }
    internal IEventLoopGroup ConfiguredGroup => _group;
    internal IEventLoop ConfiguredEventLoop => _registrationLoop;
    internal Func<IChannel> ConfiguredFactory => _factory;
    internal IChannelHandler ConfiguredHandler => _handler;
    internal EndPoint ConfiguredLocalAddress => _local;
    internal EndPoint ConfiguredRemoteAddress => _remote;
    internal object ConfiguredResolver => _disableResolver ? null : _externalResolver.ResolverGroup;
    private volatile IEventLoopGroup _group;
    private volatile IEventLoop _registrationLoop;
    private volatile Func<IChannel> _factory;
    private volatile IChannelHandler _handler;
    private volatile EndPoint _remote;
    private volatile EndPoint _local;
    private volatile bool _disableResolver;
    private volatile ExternalAddressResolver _externalResolver = ExternalAddressResolver.Default;
    // The order in which ChannelOptions are applied is important they may depend on each other for validation
    // purposes.
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
        ObjectUtil.CheckNotNull(group, "group");
        if (_group != null)
        {
            throw new InvalidOperationException("group set already");
        }
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
    public Bootstrap ChannelFactory(Func<IChannel> channelFactory)
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
    public Bootstrap Channel(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type channelClass)
    {
        // CLR: check separately so the trimming annotation on channelClass reaches the reflective factory (IL2072).
        ObjectUtil.CheckNotNull(channelClass, "channelClass");
        return ChannelFactory(new ReflectiveChannelFactory<IChannel>(channelClass).NewChannel);
    }
    // CLR: the generic form supplies the class at compile time and uses the same reflective factory.
    public Bootstrap Channel<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>()
        where T : IChannel, new() => Channel(typeof(T));
    /**
     * the {@link ChannelHandler} to use for serving the requests.
     */
    public Bootstrap Handler(IChannelHandler handler)
    {
        _handler = ObjectUtil.CheckNotNull(handler, "handler");
        return this;
    }
    /**
     * The {@link SocketAddress} to connect to once the {@link #connect()} method
     * is called.
     */
    public Bootstrap RemoteAddress(EndPoint remoteAddress)
    {
        // The source accepts null here; connect() then reports "remoteAddress not set".
        _remote = remoteAddress;
        return this;
    }
    /**
     * @see #remoteAddress(SocketAddress)
     */
    public Bootstrap RemoteAddress(string inetHost, int inetPort) => RemoteAddress(new DnsEndPoint(inetHost, inetPort));
    /**
     * @see #remoteAddress(SocketAddress)
     */
    public Bootstrap RemoteAddress(IPAddress inetHost, int inetPort) =>
        RemoteAddress(AbstractBootstrap.InetSocketAddress(inetHost, inetPort));
    /**
     * The {@link SocketAddress} which is used to bind the local "end" to.
     */
    public Bootstrap LocalAddress(EndPoint localAddress)
    {
        _local = localAddress;
        return this;
    }
    /**
     * @see #localAddress(SocketAddress)
     */
    // CLR: new InetSocketAddress(inetPort) is the wildcard address; as in BindAsync(int), the wildcard is IPAddress.Any.
    public Bootstrap LocalAddress(int inetPort) => LocalAddress(new IPEndPoint(IPAddress.Any, inetPort));
    /**
     * @see #localAddress(SocketAddress)
     */
    public Bootstrap LocalAddress(string inetHost, int inetPort) =>
        LocalAddress(AbstractBootstrap.SocketAddress(inetHost, inetPort));
    /**
     * @see #localAddress(SocketAddress)
     */
    public Bootstrap LocalAddress(IPAddress inetHost, int inetPort) =>
        LocalAddress(AbstractBootstrap.InetSocketAddress(inetHost, inetPort));
    /**
     * Allow to specify a {@link ChannelOption} which is used for the {@link Channel} instances once they got
     * created. Use a value of {@code null} to remove a previous set {@link ChannelOption}.
     */
    public Bootstrap Option<T>(ChannelOption<T> option, T value)
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
     * Allow to specify an initial attribute of the newly created {@link Channel}.  If the {@code value} is
     * {@code null}, the attribute of the specified {@code key} is removed.
     */
    public Bootstrap Attr<T>(AttributeKey<T> key, T value) where T : class
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
        _externalResolver = group == null ? ExternalAddressResolver.Default : ExternalAddressResolver.Of(group);
        _disableResolver = false;
        return this;
    }
    /**
     * Validate all the parameters. Sub-classes may override this, but should
     * call the super method in that case.
     */
    public Bootstrap Validate()
    {
        // AbstractBootstrap.validate()
        if (_group == null)
        {
            throw new InvalidOperationException("group not set");
        }
        if (_factory == null)
        {
            throw new InvalidOperationException("channel or channelFactory not set");
        }
        if (_handler == null)
        {
            throw new InvalidOperationException("handler not set");
        }
        return this;
    }
    /**
     * Returns a deep clone of this bootstrap which has the identical configuration.  This method is useful when making
     * multiple {@link Channel}s with similar settings.  Please note that this method does not clone the
     * {@link EventLoopGroup} deeply but shallowly, making the group a shared resource.
     */
    public Bootstrap Clone()
    {
        var copy = new Bootstrap { _group = _group, _registrationLoop = _registrationLoop, _factory = _factory, _handler = _handler, _remote = _remote, _local = _local, _externalResolver = _externalResolver, _disableResolver = _disableResolver };
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
    public Task<IChannel> RegisterAsync(CancellationToken cancellationToken = default)
    {
        Validate();
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<IChannel>(cancellationToken);
        }
        Task regFuture = InitAndRegister(cancellationToken, out IChannel channel);
        return RegisteredChannelAsync(regFuture, channel);
    }

    // CLR: Task<IChannel> stands for the registration ChannelFuture whose channel() is the new channel.
    internal static async Task<IChannel> RegisteredChannelAsync(Task regFuture, IChannel channel)
    {
        await regFuture.ConfigureAwait(false);
        return channel;
    }

    private Task InitAndRegister(CancellationToken token, out IChannel channel) =>
        AbstractBootstrap.InitAndRegister(_factory, Init,
            ch => _registrationLoop == null ? _group.RegisterAsync(ch, token) : _registrationLoop.RegisterAsync(ch, token),
            out channel);

    internal void Init(IChannel channel)
    {
        IChannelPipeline p = channel.Pipeline;
        p.AddLast(_handler);

        AbstractBootstrap.SetChannelOptions(channel, NewOptionsArray(), Logger);

        AbstractBootstrap.SetAttributes(channel, _attributes.Values.Select(attribute => attribute.Apply).ToArray());
        IReadOnlyList<ChannelInitializerExtension> extensions = AbstractBootstrap.GetInitializerExtensions();
        if (extensions.Count != 0)
        {
            foreach (ChannelInitializerExtension extension in extensions)
            {
                try
                {
                    extension.PostInitializeClientChannel(channel);
                }
                catch (Exception e)
                {
                    Logger.Warn("Exception thrown from postInitializeClientChannel", e);
                }
            }
        }
    }
    /**
     * Connect a {@link Channel} to the remote peer.
     */
    public Task<IChannel> ConnectAsync(CancellationToken cancellationToken = default)
    {
        Validate();
        EndPoint remoteAddress = _remote;
        if (remoteAddress == null)
        {
            throw new InvalidOperationException("remoteAddress not set");
        }

        return DoResolveAndConnectAsync(remoteAddress, _local, cancellationToken);
    }
    /**
     * Connect a {@link Channel} to the remote peer.
     */
    public Task<IChannel> ConnectAsync(string inetHost, int inetPort, CancellationToken cancellationToken = default) =>
        ConnectAsync(new DnsEndPoint(inetHost, inetPort), cancellationToken);
    /**
     * Connect a {@link Channel} to the remote peer.
     */
    public Task<IChannel> ConnectAsync(IPAddress inetHost, int inetPort, CancellationToken cancellationToken = default) =>
        ConnectAsync(AbstractBootstrap.InetSocketAddress(inetHost, inetPort), cancellationToken);
    /**
     * Connect a {@link Channel} to the remote peer.
     */
    public Task<IChannel> ConnectAsync(EndPoint remoteAddress, CancellationToken cancellationToken = default)
    {
        ObjectUtil.CheckNotNull(remoteAddress, "remoteAddress");
        Validate();
        return DoResolveAndConnectAsync(remoteAddress, _local, cancellationToken);
    }
    /**
     * Connect a {@link Channel} to the remote peer.
     */
    public Task<IChannel> ConnectAsync(EndPoint remoteAddress, EndPoint localAddress, CancellationToken cancellationToken = default)
    {
        ObjectUtil.CheckNotNull(remoteAddress, "remoteAddress");
        Validate();
        return DoResolveAndConnectAsync(remoteAddress, localAddress, cancellationToken);
    }
    /**
     * @see #connect()
     */
    private Task<IChannel> DoResolveAndConnectAsync(EndPoint remoteAddress, EndPoint localAddress, CancellationToken cancellationToken)
    {
        // CLR: an already cancelled token fails the operation before a channel is created.
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<IChannel>(cancellationToken);
        }
        Task regFuture = InitAndRegister(cancellationToken, out IChannel channel);

        if (regFuture.IsCompleted)
        {
            if (!regFuture.IsCompletedSuccessfully)
            {
                return AbstractBootstrap.Failed(regFuture);
            }
            return DoResolveAndConnect0(channel, remoteAddress, localAddress, AbstractBootstrap.NewPromise(), cancellationToken);
        }
        else
        {
            // Registration future is almost always fulfilled already, but just in case it's not.
            TaskCompletionSource<IChannel> promise = AbstractBootstrap.NewPromise();
            _ = regFuture.ContinueWith(future =>
            {
                // Directly obtain the cause and do a null check so we only need one volatile read in case of a
                // failure.
                Exception cause = AbstractBootstrap.Cause(future);
                if (cause != null || future.IsCanceled)
                {
                    // Registration on the EventLoop failed so fail the ChannelPromise directly to not cause an
                    // IllegalStateException once we try to access the EventLoop of the Channel.
                    AbstractBootstrap.SetFailure(promise, future);
                }
                else
                {
                    DoResolveAndConnect0(channel, remoteAddress, localAddress, promise, cancellationToken);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return promise.Task;
        }
    }

    private Task<IChannel> DoResolveAndConnect0(IChannel channel, EndPoint remoteAddress,
                                               EndPoint localAddress, TaskCompletionSource<IChannel> promise,
                                               CancellationToken cancellationToken)
    {
        // The Channel was created and registered before the address resolution started. Close it if the connect
        // attempt fails or is cancelled while resolution is still in progress.
        _ = promise.Task.ContinueWith(static (future, state) =>
        {
            if (!future.IsCompletedSuccessfully)
            {
                _ = ((IChannel)state).CloseAsync();
            }
        }, channel, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        if (promise.Task.IsCompleted)
        {
            return promise.Task;
        }

        try
        {
            if (_disableResolver)
            {
                DoConnect(remoteAddress, localAddress, promise, channel, cancellationToken);
                return promise.Task;
            }

            IEventLoop eventLoop = channel.EventLoop;
            ExternalAddressResolver.Resolver resolver;
            try
            {
                resolver = ExternalAddressResolver.GetOrDefault(_externalResolver).GetResolver(eventLoop);
            }
            catch (Exception cause)
            {
                AbstractBootstrap.FailAfterClose(channel, promise, Task.FromException(cause));
                return promise.Task;
            }

            if (!resolver.IsSupported(remoteAddress) || resolver.IsResolved(remoteAddress))
            {
                // Resolver has no idea about what to do with the specified remote address or it's resolved already.
                DoConnect(remoteAddress, localAddress, promise, channel, cancellationToken);
                return promise.Task;
            }

            Task<EndPoint> resolveFuture = resolver.Resolve(remoteAddress, cancellationToken);

            if (resolveFuture.IsCompleted)
            {
                Exception resolveFailureCause = AbstractBootstrap.Cause(resolveFuture);

                if (resolveFailureCause != null || resolveFuture.IsCanceled)
                {
                    // Failed to resolve immediately
                    AbstractBootstrap.FailAfterClose(channel, promise, resolveFuture);
                }
                else
                {
                    // Succeeded to resolve immediately; cached? (or did a blocking lookup)
                    DoConnect(resolveFuture.Result, localAddress, promise, channel, cancellationToken);
                }
                return promise.Task;
            }

            // Wait until the name resolution is finished.
            _ = resolveFuture.ContinueWith(future =>
            {
                if (!future.IsCompletedSuccessfully)
                {
                    AbstractBootstrap.FailAfterClose(channel, promise, future);
                }
                else if (!promise.Task.IsCompleted)
                {
                    DoConnect(future.Result, localAddress, promise, channel, cancellationToken);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            // CLR: cancelling the connect does not wait for a third-party resolver to honor the token; the promise
            // is cancelled and the close-on-failure continuation closes the channel.
            if (cancellationToken.CanBeCanceled)
            {
                CancellationTokenRegistration registration = cancellationToken.Register(
                    static state => ((TaskCompletionSource<IChannel>)state).TrySetCanceled(), promise);
                _ = promise.Task.ContinueWith(static (_, state) => ((CancellationTokenRegistration)state).Dispose(),
                    registration, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (Exception cause)
        {
            promise.TrySetException(cause);
        }
        return promise.Task;
    }

    private static void DoConnect(
            EndPoint remoteAddress, EndPoint localAddress, TaskCompletionSource<IChannel> connectPromise, IChannel channel,
            CancellationToken cancellationToken)
    {
        // This method is invoked before channelRegistered() is triggered. Give user handlers a chance to set up
        // the pipeline in its channelRegistered() implementation.
        AbstractBootstrap.Execute(channel, connectPromise, () =>
        {
            if (localAddress == null)
            {
                AbstractBootstrap.Complete(channel.ConnectAsync(remoteAddress, null, cancellationToken), channel, connectPromise, true);
            }
            else
            {
                AbstractBootstrap.Complete(channel.ConnectAsync(remoteAddress, localAddress, cancellationToken), channel,
                    connectPromise, true);
            }
        });
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
    public Task<IChannel> BindAsync(int inetPort, CancellationToken cancellationToken = default)
        => BindAsync(new IPEndPoint(IPAddress.Any, inetPort), cancellationToken);
    /**
     * Create a new {@link Channel} and bind it.
     */
    public Task<IChannel> BindAsync(string inetHost, int inetPort, CancellationToken cancellationToken = default)
        => BindAsync(AbstractBootstrap.SocketAddress(inetHost, inetPort), cancellationToken);
    /**
     * Create a new {@link Channel} and bind it.
     */
    public Task<IChannel> BindAsync(IPAddress inetHost, int inetPort, CancellationToken cancellationToken = default)
        => BindAsync(AbstractBootstrap.InetSocketAddress(inetHost, inetPort), cancellationToken);
    /**
     * Create a new {@link Channel} and bind it.
     */
    public Task<IChannel> BindAsync(EndPoint localAddress, CancellationToken cancellationToken = default)
    {
        Validate();
        return DoBind(ObjectUtil.CheckNotNull(localAddress, "localAddress"), cancellationToken);
    }

    private Task<IChannel> DoBind(EndPoint localAddress, CancellationToken cancellationToken)
    {
        // CLR: an already cancelled token fails the operation before a channel is created.
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<IChannel>(cancellationToken);
        }
        Task regFuture = InitAndRegister(cancellationToken, out IChannel channel);
        return AbstractBootstrap.DoBind(regFuture, channel, localAddress, cancellationToken);
    }

    // CLR: closes a channel whose bootstrap operation failed: channel.unsafe().closeForcibly() before registration,
    // channel.close() after it (initAndRegister); used by the channel pools.
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

    // Holder to avoid NoClassDefFoundError in case netty-resolver dependency is excluded (e.g. some address families do not need name resolution)
    // CLR: the holder keeps the configured AddressResolverGroup<T> and adapts its typed resolver to EndPoint.
    internal sealed class ExternalAddressResolver
    {
        internal static readonly ExternalAddressResolver Default = new ExternalAddressResolver(
            DefaultAddressResolverGroup.INSTANCE, executor => new Resolver<IPEndPoint>(DefaultAddressResolverGroup.INSTANCE.GetResolver(executor)));

        internal readonly object ResolverGroup;
        private readonly Func<IEventExecutor, Resolver> _getResolver;

        private ExternalAddressResolver(object resolverGroup, Func<IEventExecutor, Resolver> getResolver)
        {
            ResolverGroup = resolverGroup;
            _getResolver = getResolver;
        }

        internal static ExternalAddressResolver Of<T>(AddressResolverGroup<T> resolverGroup) where T : EndPoint =>
            new ExternalAddressResolver(resolverGroup, executor => new Resolver<T>(resolverGroup.GetResolver(executor)));

        internal Resolver GetResolver(IEventExecutor executor) => _getResolver(executor);

        internal static ExternalAddressResolver GetOrDefault(ExternalAddressResolver externalResolver)
        {
            return externalResolver ?? Default;
        }

        internal abstract class Resolver
        {
            internal abstract bool IsSupported(EndPoint address);
            internal abstract bool IsResolved(EndPoint address);
            internal abstract Task<EndPoint> Resolve(EndPoint address, CancellationToken cancellationToken);
        }

        private sealed class Resolver<T>(IAddressResolver<T> resolver) : Resolver where T : EndPoint
        {
            internal override bool IsSupported(EndPoint address) => resolver.IsSupported(address);
            internal override bool IsResolved(EndPoint address) => resolver.IsResolved(address);

            internal override Task<EndPoint> Resolve(EndPoint address, CancellationToken cancellationToken)
            {
                Task<T> resolved = resolver.ResolveAsync(address, cancellationToken);
                // CLR: Task<T> is not covariant; a completed result keeps the immediate (isDone) path.
                return resolved.IsCompletedSuccessfully
                    ? Task.FromResult<EndPoint>(resolved.Result)
                    : resolved.ContinueWith(static t => (EndPoint)t.GetAwaiter().GetResult(), CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }
}

[Sharable]
public sealed class ActionChannelInitializer<T>(Action<T> initialize) : ChannelInitializer<T> where T : IChannel
{
    private readonly Action<T> _initialize = initialize ?? throw new ArgumentNullException(nameof(initialize));
    protected override void InitChannel(T channel) => _initialize(channel);
}
