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
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Resolver;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Bootstrapping;

/**
 * {@link AbstractBootstrap} is a helper class that makes it easy to bootstrap a {@link Channel}. It support
 * method-chaining to provide an easy way to configure the {@link AbstractBootstrap}.
 *
 * <p>When not used in a {@link ServerBootstrap} context, the {@link #bind()} methods are useful for connectionless
 * transports such as datagram (UDP).</p>
 */
// CLR: the common base of Bootstrap and ServerBootstrap. The self-typed builder members (group, channel, option,
// attr, handler, localAddress, validate, clone) stay on the two subclasses so each returns its own type without a
// self-typed generic parameter (see the "Bootstrap configuration views and failure/completion representation"
// decision); the shared registration/bind machinery and package-private static members live here.
public abstract class AbstractBootstrap
{
    internal AbstractBootstrap()
    {
        // Disallow extending from a different package.
    }

    /**
     * Returns the {@link AbstractBootstrapConfig} object that can be used to obtain the current config of the bootstrap.
     */
    public abstract AbstractBootstrapConfig Config { get; }

    public override string ToString()
    {
        StringBuilder buf = new StringBuilder()
            .Append(StringUtil.SimpleClassName(this))
            .Append('(').Append(Config).Append(')');
        return buf.ToString();
    }

    // CLR: SocketUtils.socketAddress(hostname, port), used by localAddress(String, int) and bind(String, int), is
    // new InetSocketAddress(hostname, port). That constructor rejects a null host, looks the host up with
    // InetAddress.getByName and keeps the address unresolved when the lookup throws UnknownHostException.
    // SocketUtils is replaced by native socket operations (docs/common-clr-design.md); the InetAddress.getByName rules
    // (literals, BSD-form rejection, java.net.preferIPv6Addresses order) are ported in DefaultNameResolver, which the
    // source also resolves through SocketUtils.addressByName, so the lookup goes through it. Like getByName it blocks
    // the caller. An unresolved InetSocketAddress is a DnsEndPoint here, as in NioDatagramChannel.
    internal static EndPoint SocketAddress(string hostname, int port)
    {
        if (hostname == null)
        {
            throw new ArgumentException("hostname can't be null");
        }

        IPAddress addr;

        try
        {
            addr = new DefaultNameResolver(ImmediateEventExecutor.INSTANCE).ResolveAsync(hostname).GetAwaiter().GetResult();
        }
        // CLR: DefaultNameResolver reports UnknownHostException as Common's UnknownHostException (owner decision
        // 2026-11-25). Dns.GetHostAddressesAsync rejects a name longer than 255 characters with
        // ArgumentOutOfRangeException and an invalid address with ArgumentException
        // (https://learn.microsoft.com/dotnet/api/system.net.dns.gethostaddressesasync); the JDK reports both as
        // UnknownHostException.
        catch (Exception e) when (e is UnknownHostException || e is ArgumentException)
        {
            return new DnsEndPoint(hostname, port);
        }

        return new IPEndPoint(addr, port);
    }

    // CLR: new InetSocketAddress(addr, port) binds the wildcard address InetAddress.anyLocalAddress() when addr is null;
    // as in BindAsync(int), the wildcard is IPAddress.Any (0.0.0.0, the JDK default while java.net.preferIPv6Addresses
    // is false).
    internal static IPEndPoint InetSocketAddress(IPAddress addr, int port)
    {
        return new IPEndPoint(addr ?? IPAddress.Any, port);
    }

    // CLR: the registration Task replaces the ChannelFuture returned by initAndRegister(). A Task has no channel()
    // accessor, so the created channel is returned through 'channel' (null when the factory failed; the faulted Task
    // replaces the promise on FailedChannel and GlobalEventExecutor).
    internal static Task InitAndRegister(Func<IChannel> channelFactory, Action<IChannel> init,
            Func<IChannel, Task> register, out IChannel channel)
    {
        channel = null;

        try
        {
            channel = channelFactory();
            init(channel);
        }
        catch (Exception t)
        {
            if (channel != null)
            {
                // channel can be null if newChannel crashed (eg SocketException("too many open files"))
                CloseForcibly(channel);

                return Task.FromException(t);
            }

            return Task.FromException(t);
        }

        Task regFuture = register(channel);
        if (regFuture.IsFaulted || regFuture.IsCanceled)
        {
            if (channel.IsRegistered)
            {
                _ = channel.CloseAsync();
            }
            else
            {
                CloseForcibly(channel);
            }
        }

        // If we are here and the promise is not failed, it's one of the following cases:
        // 1) If we attempted registration from the event loop, the registration has been completed at this point.
        //    i.e. It's safe to attempt bind() or connect() now because the channel has been registered.
        // 2) If we attempted registration from the other thread, the registration request has been successfully
        //    added to the event loop's task queue for later execution.
        //    i.e. It's safe to attempt bind() or connect() now:
        //         because bind() or connect() will be executed *after* the scheduled registration task is executed
        //         because register(), bind(), and connect() are all bound to the same thread.

        return regFuture;
    }

    // CLR: channel.unsafe().closeForcibly(); AbstractUnsafe is folded into AbstractChannel.
    internal static void CloseForcibly(IChannel channel)
    {
        if (channel is AbstractChannel abstractChannel)
        {
            abstractChannel.CloseForcibly();
        }
        else
        {
            _ = channel.CloseAsync();
        }
    }

    // CLR: Task<IChannel> stands for the ChannelFuture whose channel() is the bound channel.
    internal static Task<IChannel> DoBind(Task regFuture, IChannel channel, EndPoint localAddress,
            CancellationToken cancellationToken)
    {
        if (regFuture.IsFaulted || regFuture.IsCanceled)
        {
            return Failed(regFuture);
        }

        if (regFuture.IsCompleted)
        {
            // At this point we know that the registration was complete and successful.
            TaskCompletionSource<IChannel> promise = NewPromise();
            DoBind0(regFuture, channel, localAddress, promise, cancellationToken);
            return promise.Task;
        }
        else
        {
            // Registration future is almost always fulfilled already, but just in case it's not.
            TaskCompletionSource<IChannel> promise = NewPromise();
            _ = regFuture.ContinueWith(future =>
            {
                Exception cause = Cause(future);
                if (cause != null || future.IsCanceled)
                {
                    // Registration on the EventLoop failed so fail the ChannelPromise directly to not cause an
                    // IllegalStateException once we try to access the EventLoop of the Channel.
                    SetFailure(promise, future);
                }
                else
                {
                    DoBind0(regFuture, channel, localAddress, promise, cancellationToken);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return promise.Task;
        }
    }

    private static void DoBind0(
            Task regFuture, IChannel channel,
            EndPoint localAddress, TaskCompletionSource<IChannel> promise, CancellationToken cancellationToken)
    {
        // This method is invoked before channelRegistered() is triggered. Give user handlers a chance to set up
        // the pipeline in its channelRegistered() implementation.
        Execute(channel, promise, () =>
        {
            if (regFuture.IsCompletedSuccessfully)
            {
                // CLR: ChannelFutureListener.CLOSE_ON_FAILURE closes the channel before the promise reports failure.
                Complete(channel.BindAsync(localAddress, cancellationToken), channel, promise, true);
            }
            else
            {
                SetFailure(promise, regFuture);
            }
        });
    }

    // CLR: channel.eventLoop().execute(task); a rejected task fails the promise (and closes the channel) instead of
    // leaving a listener-created promise incomplete.
    internal static void Execute(IChannel channel, TaskCompletionSource<IChannel> promise, Action task)
    {
        try
        {
            channel.EventLoop.Execute(task);
        }
        catch (Exception e)
        {
            _ = channel.CloseAsync();
            promise.TrySetException(e);
        }
    }

    // CLR: completes the channel promise from an operation Task, optionally after ChannelFutureListener.CLOSE_ON_FAILURE.
    internal static void Complete(Task operation, IChannel channel, TaskCompletionSource<IChannel> promise, bool closeOnFailure)
    {
        _ = operation.ContinueWith(future =>
        {
            if (future.IsCompletedSuccessfully)
            {
                promise.TrySetResult(channel);
                return;
            }

            if (closeOnFailure)
            {
                FailAfterClose(channel, promise, future);
            }
            else
            {
                SetFailure(promise, future);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    // CLR: CLOSE_ON_FAILURE is the first listener of the promise in Java, so the channel is closed before user listeners
    // observe the failure; the promise fails once channel.close() has completed.
    internal static void FailAfterClose(IChannel channel, TaskCompletionSource<IChannel> promise, Task failed)
    {
        Task close;

        try
        {
            close = channel.CloseAsync();
        }
        catch (Exception)
        {
            close = Task.CompletedTask;
        }

        _ = close.ContinueWith(_ => SetFailure(promise, failed), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    internal static TaskCompletionSource<IChannel> NewPromise()
    {
        return new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // CLR: Future.cause() of a completed Task; a cancelled Task has no exception and is reported through IsCanceled.
    internal static Exception Cause(Task future)
    {
        return future.IsFaulted ? (future.Exception.InnerExceptions.Count == 1 ? future.Exception.InnerException : future.Exception) : null;
    }

    internal static void SetFailure(TaskCompletionSource<IChannel> promise, Task future)
    {
        if (future.IsCanceled)
        {
            promise.TrySetCanceled();
        }
        else
        {
            promise.TrySetException(Cause(future));
        }
    }

    internal static Task<IChannel> Failed(Task future)
    {
        TaskCompletionSource<IChannel> promise = NewPromise();
        SetFailure(promise, future);
        return promise.Task;
    }

    private static readonly bool CLOSE_ON_SET_OPTION_FAILURE = SystemPropertyUtil.GetBoolean(
            "io.netty.bootstrap.closeOnSetOptionFailure", true);

    // CLR: there is no ClassLoader; the explicitly registered process-wide extensions are captured instead.
    internal static IReadOnlyList<ChannelInitializerExtension> GetInitializerExtensions()
    {
        return ChannelInitializerExtensions.Snapshot();
    }

    // CLR: an entry applies its typed AttributeKey<T>; channel.attr(key).set(value) needs the generic key.
    internal static void SetAttributes(IChannel channel, Action<IChannel>[] attrs)
    {
        foreach (Action<IChannel> e in attrs)
        {
            e(channel);
        }
    }

    internal static void SetChannelOptions(
            IChannel channel, KeyValuePair<ChannelOption, object>[] options, IInternalLogger logger)
    {
        foreach (KeyValuePair<ChannelOption, object> e in options)
        {
            SetChannelOption(channel, e.Key, e.Value, logger);
        }
    }

    private static void SetChannelOption(
            IChannel channel, ChannelOption option, object value, IInternalLogger logger)
    {
        try
        {
            // CLR: IChannelConfig.SetOptions is the untyped setOption((ChannelOption<Object>) option, value).
            if (!channel.Config.SetOptions(new[] { new KeyValuePair<ChannelOption, object>(option, value) }))
            {
                logger.Warn("Unknown channel option '{}' for channel '{}' of type '{}'",
                        option, channel, channel.GetType());
            }
        }
        catch (Exception t)
        {
            logger.Warn(
                    "Failed to set channel option '{}' with value '{}' for channel '{}' of type '{}'",
                    option, value, channel, channel.GetType(), t);
            if (CLOSE_ON_SET_OPTION_FAILURE)
            {
                // Only rethrow if we want to close the channel in case of a failure.
                throw;
            }
        }
    }
}
