using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Resolver.Tests;

// These contracts explicitly exercise the default-token overload; cancellation cases use their own source.
#pragma warning disable xUnit1051
// Additional CLR contract tests. Original Java fixtures remain individually traceable in separate files.
public class ResolverContractTest
{
    private static readonly IPAddress[] Addresses = { IPAddress.Parse("192.0.2.1"), IPAddress.Parse("2001:db8::1"), IPAddress.Parse("192.0.2.2") };

    [Fact]
    public async Task ResolvedEndpointBypassesLookupAndPreservesIdentity()
    {
        var name = new Mock<INameResolver<IPAddress>>(MockBehavior.Strict);
        using var resolver = new InetSocketAddressResolver(ImmediateEventExecutor.INSTANCE, name.Object);
        name.Setup(r => r.Dispose());
        var endpoint = new IPEndPoint(Addresses[0], 12345);
        Assert.True(resolver.IsSupported(endpoint));
        Assert.True(resolver.IsResolved(endpoint));
        Assert.Same(endpoint, await resolver.ResolveAsync(endpoint));
        Assert.Same(endpoint, Assert.Single(await resolver.ResolveAllAsync(endpoint)));
        name.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DnsEndpointResolvesWithoutReverseLookupAndPreservesPortAndOrder()
    {
        var name = new Mock<INameResolver<IPAddress>>(MockBehavior.Strict);
        name.Setup(r => r.ResolveAsync("example.invalid", default)).ReturnsAsync(Addresses[0]);
        name.Setup(r => r.ResolveAllAsync("example.invalid", default)).ReturnsAsync(Addresses);
        name.Setup(r => r.Dispose());
        using var resolver = new InetSocketAddressResolver(ImmediateEventExecutor.INSTANCE, name.Object);
        var endpoint = new DnsEndPoint("example.invalid", 443);
        Assert.True(resolver.IsSupported(endpoint));
        Assert.False(resolver.IsResolved(endpoint));
        Assert.Equal(new IPEndPoint(Addresses[0], 443), await resolver.ResolveAsync(endpoint));
        Assert.Equal(Addresses.Select(ip => new IPEndPoint(ip, 443)), await resolver.ResolveAllAsync(endpoint));
        name.Verify(r => r.ResolveAsync(endpoint.Host, default), Times.Once());
        name.Verify(r => r.ResolveAllAsync(endpoint.Host, default), Times.Once());
    }

    [Theory]
    [InlineData(AddressFamily.InterNetwork)]
    [InlineData(AddressFamily.InterNetworkV6)]
    public async Task DnsEndpointHonorsClrAddressFamilyConstraint(AddressFamily family)
    {
        using var resolver = new InetSocketAddressResolver(ImmediateEventExecutor.INSTANCE, new FixedResolver(Addresses));
        var endpoint = new DnsEndPoint("example.invalid", 80, family);
        var expected = Addresses.Where(ip => ip.AddressFamily == family).ToArray();
        Assert.Equal(expected.Select(ip => new IPEndPoint(ip, 80)), await resolver.ResolveAllAsync(endpoint));
        Assert.Equal(new IPEndPoint(expected[0], 80), await resolver.ResolveAsync(endpoint));
    }

    [Fact]
    public async Task FamilyMismatchHasEmptyAllResultAndSingleLookupFails()
    {
        using var resolver = new InetSocketAddressResolver(ImmediateEventExecutor.INSTANCE, new FixedResolver(new[] { Addresses[0] }));
        var endpoint = new DnsEndPoint("example.invalid", 80, AddressFamily.InterNetworkV6);
        Assert.Empty(await resolver.ResolveAllAsync(endpoint));
        Assert.Equal(SocketError.HostNotFound, (await Assert.ThrowsAsync<SocketException>(() => resolver.ResolveAsync(endpoint))).SocketErrorCode);
    }

    [Fact]
    public async Task InvalidEndpointsFollowOriginalSynchronousAndFailedTaskBoundaries()
    {
        using var resolver = new InetSocketAddressResolver(ImmediateEventExecutor.INSTANCE, new FixedResolver(Addresses));
        EndPoint unsupported = new UnixDomainSocketEndPoint("resolver-test.sock");
        Assert.False(resolver.IsSupported(unsupported));
        Assert.False(resolver.IsSupported(null));
        Assert.Throws<NotSupportedException>(() => resolver.IsResolved(unsupported));
        Assert.Throws<NotSupportedException>(() => resolver.IsResolved(null));
        // Argument validation must throw before returning a Task, as in AbstractAddressResolver.resolve.
#pragma warning disable xUnit2014
        Assert.Throws<ArgumentNullException>(() => { resolver.ResolveAsync(null); });
        Assert.Throws<ArgumentNullException>(() => { resolver.ResolveAllAsync(null); });
#pragma warning restore xUnit2014
        await Assert.ThrowsAsync<NotSupportedException>(() => resolver.ResolveAsync(unsupported));
        await Assert.ThrowsAsync<NotSupportedException>(() => resolver.ResolveAllAsync(unsupported));
    }

    [Fact]
    public async Task EndpointResolutionPropagatesOriginalFailureObject()
    {
        var error = new InvalidOperationException("name lookup failed");
        var name = new Mock<INameResolver<IPAddress>>();
        name.Setup(r => r.ResolveAsync(It.IsAny<string>(), default)).Returns(Task.FromException<IPAddress>(error));
        name.Setup(r => r.ResolveAllAsync(It.IsAny<string>(), default)).Returns(Task.FromException<IReadOnlyList<IPAddress>>(error));
        using var resolver = new InetSocketAddressResolver(ImmediateEventExecutor.INSTANCE, name.Object);
        var endpoint = new DnsEndPoint("example.invalid", 80);
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(endpoint)));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAllAsync(endpoint)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompositeTriesDelegatesSequentiallyAndStopsOnSuccess(bool all)
    {
        var first = new Mock<INameResolver<IPAddress>>(MockBehavior.Strict);
        var second = new Mock<INameResolver<IPAddress>>(MockBehavior.Strict);
        var third = new Mock<INameResolver<IPAddress>>(MockBehavior.Strict);
        var pending = new TaskCompletionSource<IPAddress>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingAll = new TaskCompletionSource<IReadOnlyList<IPAddress>>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Setup(r => r.ResolveAsync("host", default)).Returns(pending.Task);
        first.Setup(r => r.ResolveAllAsync("host", default)).Returns(pendingAll.Task);
        second.Setup(r => r.ResolveAsync("host", default)).ReturnsAsync(Addresses[0]);
        second.Setup(r => r.ResolveAllAsync("host", default)).ReturnsAsync(Addresses);
        var delegates = new[] { first.Object, second.Object, third.Object };
        using var resolver = new CompositeNameResolver<IPAddress>(ImmediateEventExecutor.INSTANCE, delegates);
        delegates[1] = third.Object; // The constructor must copy the supplied array.
        Task operation = all ? resolver.ResolveAllAsync("host") : resolver.ResolveAsync("host");
        Assert.False(operation.IsCompleted);
        second.VerifyNoOtherCalls();
        third.VerifyNoOtherCalls();
        var error = new Exception("first failed");
        if (all)
        {
            pendingAll.SetException(error);
            Assert.Same(Addresses, await (Task<IReadOnlyList<IPAddress>>)operation);
        }
        else
        {
            pending.SetException(error);
            Assert.Same(Addresses[0], await (Task<IPAddress>)operation);
        }
        third.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompositeReportsOnlyLastFailureAndDoesNotOwnDelegates(bool all)
    {
        var firstError = new Exception("first");
        var lastError = new Exception("last");
        var first = new Mock<INameResolver<IPAddress>>(MockBehavior.Strict);
        var second = new Mock<INameResolver<IPAddress>>(MockBehavior.Strict);
        first.Setup(r => r.ResolveAsync("host", default)).Returns(Task.FromException<IPAddress>(firstError));
        first.Setup(r => r.ResolveAllAsync("host", default)).Returns(Task.FromException<IReadOnlyList<IPAddress>>(firstError));
        second.Setup(r => r.ResolveAsync("host", default)).Returns(Task.FromException<IPAddress>(lastError));
        second.Setup(r => r.ResolveAllAsync("host", default)).Returns(Task.FromException<IReadOnlyList<IPAddress>>(lastError));
        var resolver = new CompositeNameResolver<IPAddress>(ImmediateEventExecutor.INSTANCE, first.Object, second.Object);
        var observed = await Assert.ThrowsAsync<Exception>(() => all ? (Task)resolver.ResolveAllAsync("host") : resolver.ResolveAsync("host"));
        Assert.Same(lastError, observed);
        resolver.Dispose();
        first.Verify(r => r.Dispose(), Times.Never());
        second.Verify(r => r.Dispose(), Times.Never());
    }

    [Fact]
    public void CompositeValidatesDelegates()
    {
        var executor = ImmediateEventExecutor.INSTANCE;
        var name = new FixedResolver(Addresses);
        Assert.Throws<ArgumentNullException>(() => new CompositeNameResolver<IPAddress>(executor, null));
        Assert.Throws<ArgumentException>(() => new CompositeNameResolver<IPAddress>(executor));
        Assert.Throws<ArgumentException>(() => new CompositeNameResolver<IPAddress>(executor, name));
        Assert.Throws<ArgumentNullException>(() => new CompositeNameResolver<IPAddress>(executor, name, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellationStopsCompositeFallback(bool all)
    {
        using var cancellation = new CancellationTokenSource();
        var pending = new TaskCompletionSource<IPAddress>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingAll = new TaskCompletionSource<IReadOnlyList<IPAddress>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new Mock<INameResolver<IPAddress>>();
        first.Setup(r => r.ResolveAsync("host", cancellation.Token)).Returns(() => pending.Task.WaitAsync(cancellation.Token));
        first.Setup(r => r.ResolveAllAsync("host", cancellation.Token)).Returns(() => pendingAll.Task.WaitAsync(cancellation.Token));
        var second = new Mock<INameResolver<IPAddress>>(MockBehavior.Strict);
        using var resolver = new CompositeNameResolver<IPAddress>(ImmediateEventExecutor.INSTANCE, first.Object, second.Object);
        Task operation = all ? resolver.ResolveAllAsync("host", cancellation.Token) : resolver.ResolveAsync("host", cancellation.Token);
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(operation.IsCanceled);
        second.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreCanceledNameLookupNeverCallsImplementation(bool all)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var resolver = new ThrowingResolver(new Exception("must not call"));
        Task operation = all ? resolver.ResolveAllAsync("host", cancellation.Token) : resolver.ResolveAsync("host", cancellation.Token);
        Assert.True(operation.IsCanceled);
        Assert.Equal(cancellation.Token, (await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation)).CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SynchronousNameImplementationFailureBecomesFailedTask(bool all)
    {
        var error = new InvalidOperationException("original");
        using var resolver = new ThrowingResolver(error);
        Task operation = all ? resolver.ResolveAllAsync("host") : resolver.ResolveAsync("host");
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => operation));
    }

    [Fact]
    public async Task RoundRobinRotatesWithoutChangingOrShufflingUnderlyingSequence()
    {
        var name = new Mock<INameResolver<IPAddress>>(MockBehavior.Strict);
        name.Setup(r => r.ResolveAllAsync("host", default)).ReturnsAsync(Addresses);
        name.Setup(r => r.Dispose());
        using var resolver = new RoundRobinInetAddressResolver(ImmediateEventExecutor.INSTANCE, name.Object);
        for (int iteration = 0; iteration < 50; iteration++)
        {
            Assert.Contains(await resolver.ResolveAsync("host"), Addresses);
            var rotated = await resolver.ResolveAllAsync("host");
            Assert.NotSame(Addresses, rotated);
            Assert.Equal(Addresses.Length, rotated.Count);
            int first = Array.IndexOf(Addresses, rotated[0]);
            for (int i = 0; i < rotated.Count; i++)
                Assert.Same(Addresses[(first + i) % Addresses.Length], rotated[i]);
            Assert.Equal(new[] { "192.0.2.1", "2001:db8::1", "192.0.2.2" }, Addresses.Select(a => a.ToString()));
        }
        name.Verify(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task RoundRobinHandlesEmptyAndSingletonResultsAndDelegatesClose()
    {
        var empty = Array.Empty<IPAddress>();
        var name = new Mock<INameResolver<IPAddress>>();
        name.Setup(r => r.ResolveAllAsync("empty", default)).ReturnsAsync(empty);
        name.Setup(r => r.ResolveAllAsync("single", default)).ReturnsAsync(new[] { Addresses[0] });
        var resolver = new RoundRobinInetAddressResolver(ImmediateEventExecutor.INSTANCE, name.Object);
        Assert.Same(empty, await resolver.ResolveAllAsync("empty"));
        Assert.Equal(SocketError.HostNotFound, (await Assert.ThrowsAsync<SocketException>(() => resolver.ResolveAsync("empty"))).SocketErrorCode);
        Assert.Same(Addresses[0], await resolver.ResolveAsync("single"));
        Assert.Same(Addresses[0], Assert.Single(await resolver.ResolveAllAsync("single")));
        resolver.Dispose();
        name.Verify(r => r.Dispose(), Times.Once());
    }

    [Fact]
    public async Task NameResolverAddressAdapterIsCachedAcrossThreads()
    {
        using var name = new FixedResolver(Addresses);
        var adapters = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(name.AsAddressResolver)));
        Assert.All(adapters, adapter => Assert.Same(adapters[0], adapter));
    }

    [Fact]
    public async Task NoopResolverPreservesEvenUnresolvedEndpoint()
    {
        using var resolver = new NoopAddressResolver(ImmediateEventExecutor.INSTANCE);
        var endpoint = new DnsEndPoint("host.invalid", 1234);
        Assert.True(resolver.IsResolved(endpoint));
        Assert.Same(endpoint, await resolver.ResolveAsync(endpoint));
        Assert.Same(endpoint, Assert.Single(await resolver.ResolveAllAsync(endpoint)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task DefaultResolverHandlesLoopbackAndLiteralsWithoutExternalDns(string host)
    {
        using var resolver = new DefaultNameResolver(ImmediateEventExecutor.INSTANCE);
        IPAddress expected = string.IsNullOrEmpty(host) ? IPAddress.Loopback : IPAddress.Parse(host);
        Assert.Equal(expected, await resolver.ResolveAsync(host));
        Assert.Equal(expected, Assert.Single(await resolver.ResolveAllAsync(host)));
    }

    [Fact]
    public async Task DefaultResolverClassifiesMappedLiteralLikeJavaInetAddress()
    {
        using var resolver = new DefaultNameResolver(ImmediateEventExecutor.INSTANCE);
        Assert.Equal(IPAddress.Parse("192.0.2.1"), await resolver.ResolveAsync("::ffff:192.0.2.1"));
        Assert.Equal(IPAddress.Parse("192.0.2.1"), Assert.Single(await resolver.ResolveAllAsync("::ffff:192.0.2.1")));
    }

    [Fact]
    public async Task DefaultGroupResolvesLocalhostThroughOsResolver()
    {
        var executor = new DefaultEventExecutor();
        try
        {
            var resolver = DefaultAddressResolverGroup.INSTANCE.GetResolver(executor);
            var endpoints = await resolver.ResolveAllAsync(new DnsEndPoint("localhost", 8080));
            Assert.NotEmpty(endpoints);
            Assert.All(endpoints, endpoint =>
            {
                Assert.True(IPAddress.IsLoopback(endpoint.Address));
                Assert.Equal(8080, endpoint.Port);
            });
        }
        finally { await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    private sealed class FixedResolver(IReadOnlyList<IPAddress> addresses) : InetNameResolver(ImmediateEventExecutor.INSTANCE)
    {
        protected override Task<IPAddress> DoResolveAsync(string inetHost, CancellationToken cancellationToken) => Task.FromResult(addresses[0]);
        protected override Task<IReadOnlyList<IPAddress>> DoResolveAllAsync(string inetHost, CancellationToken cancellationToken) => Task.FromResult(addresses);
    }

    private sealed class ThrowingResolver(Exception error) : SimpleNameResolver<IPAddress>(ImmediateEventExecutor.INSTANCE)
    {
        protected override Task<IPAddress> DoResolveAsync(string inetHost, CancellationToken cancellationToken) => throw error;
        protected override Task<IReadOnlyList<IPAddress>> DoResolveAllAsync(string inetHost, CancellationToken cancellationToken) => throw error;
    }
}
