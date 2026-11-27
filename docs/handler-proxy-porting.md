# handler-proxy porting

Netty.NET ports `netty-handler-proxy` of Netty 4.2.19.Final (pinned commit
`64cc10f38ea5f5bd7eae48507817c66680d0afdc`) to `src/Netty.NET.Handler.Proxy`, with its original tests in
`test/Netty.NET.Handler.Proxy.Tests`. The [manifest](handler-proxy-porting-manifest.json) records every pinned file of
`handler-proxy/src` (18 files) with its C# counterpart, its CLR adaptations and evidence, or the reason it is not
ported. Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module handler-proxy
```

The checker verifies the file inventory and source hashes, that every original comment (license, Javadoc and line
comments) is present in the mapped C# files, that every declared JUnit test is mapped to a C# test of the same name
(case may differ) or carries a recorded disposition, and the facts and evidence of every unit that is not ported. It
fails while any entry is still `pending`.

| Status | Files |
| --- | --- |
| `clr-adapted` | 6 (the main units) |
| `verified` (tests and test support whose every original method is mapped) | 8 |
| `jvm-only-replaced` | 4 |

Of the 19 declared original tests, 18 are mapped to C# tests with the same names and 1 belongs to the GraalVM metadata
test, which is not ported. The C# run executes 69 test cases: `ProxyHandlerTest.test` runs once per `TestItem` (47
scenarios over real NIO sockets), the two `proxyTypes` tests once per proxy type, and `testInvalidHeaders` once per
`@ValueSource` input. The module has no `@Deprecated` members.

## Namespaces and assembly

The project, folder and assembly are `Netty.NET.Handler.Proxy`, the namespace root of `io.netty.handler.proxy` (the
2026-11-19 rule, see [codec-base-porting.md](codec-base-porting.md); no new name choice); the tests use
`Netty.NET.Handler.Proxy.Tests`. The project references `Netty.NET.Handler.Codec.Http` (which brings Handler,
codec-compression, codec-base, Transport, Resolver, Buffer and Common) and `Netty.NET.Handler.Codec.Socks`, the
modules that `module-info.yml` requires; `InternalsVisibleTo` opens internals only to the test assembly.

| Java package | C# namespace | Original package summary |
| --- | --- | --- |
| `io.netty.handler.proxy` | `Netty.NET.Handler.Proxy` | Adds support for client connections via proxy protocols such as SOCKS and HTTP CONNECT tunneling. |
| test `io.netty.handler.proxy` | `Netty.NET.Handler.Proxy.Tests` | |

## CLR adaptations

- **Addresses.** `SocketAddress` is `EndPoint`, a resolved `InetSocketAddress` is `IPEndPoint` and an unresolved one
  (`InetSocketAddress.createUnresolved`) is `DnsEndPoint` ([handler-porting.md](handler-porting.md), Addresses).
  `InetSocketAddress raddr = destinationAddress()` in the subclasses is `ProxyHandler.CastToInetSocketAddress`, which
  keeps an `IPEndPoint`, a `DnsEndPoint` or null and throws `InvalidCastException` (Java's `ClassCastException`) for any
  other address. `isUnresolved()` is a `DnsEndPoint`, `getHostString()` its `Host` and `getPort()` the `Port`.
  `getAddress().getHostAddress()`, the text the SOCKS handlers send, is `ProxyHandler.GetHostAddress`: the JDK text
  (uncompressed IPv6 groups and the scope id), built as handler's `IpSubnetFilterRule` builds it, because
  `IPAddress.ToString()` compresses IPv6 with `::`. An address prints as `EndPoint.ToString()`.
- **Generic accessors.** `<T extends SocketAddress> T proxyAddress()/destinationAddress()` of `ProxyHandler` and
  `ProxyConnectionEvent` are generic methods constrained to `EndPoint`; Java infers `T` from the assignment target, a C#
  caller names it (`ProxyAddress<IPEndPoint>()`), and the cast is checked.
- **Promises.** The `ChannelPromise` of `connect`/`write` is the `Task` that `ConnectAsync`/`WriteAsync` return
  ([codec-base-porting.md](codec-base-porting.md), Outbound operations). A write before the handshake finishes
  returns the consumer `Task` of `PendingWriteQueue.Add(msg, cancellationToken)`; `removeAndWriteAll()` is
  `RemoveAndWriteAllAsync()`, whose `Task` is ignored as in the source. `Future<Channel> connectFuture()` is
  `Task<IChannel> ConnectFuture()`: `LazyChannelPromise` (a `DefaultPromise` on `ctx.executor()`) is a
  `TaskCompletionSource<IChannel>` with `RunContinuationsAsynchronously`, as `SslHandler` maps its
  `LazyChannelPromise`. The write listener is a continuation on Common's `EventExecutorTaskScheduler` over
  `ctx.Executor` (the timing of `DefaultPromise.notifyListeners`). The connect timeout is `ctx.Executor.ScheduleAsync`
  with `TimeSpan.FromMilliseconds`, and its `Future<?>` is a `CancellationTokenSource` (`cancel(false)` is `Cancel()`),
  as in `SslHandler` and `WriteTimeoutHandler`.
- **`ConnectionPendingException`.** `promise.setFailure(new ConnectionPendingException())` in `connect` is a `Task`
  faulted with `SocketException(SocketError.AlreadyInProgress)`, the Transport's counterpart (`LocalChannel`,
  `AbstractNioChannel`).
- **`ProxyConnectException`.** `java.net.ConnectException` extends `SocketException` and `java.io.IOException`
  ([ConnectException](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/ConnectException.html)).
  The CLR `SocketException` has no constructor that takes an inner exception (`SocketException()`, `(int errorCode)`,
  `(int errorCode, string? message)`,
  [constructors](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketexception.-ctor?view=net-10.0)),
  so a subclass could not keep the cause that the `(Throwable)` and `(String, Throwable)` constructors record with
  `initCause`. `ProxyConnectException` therefore extends `System.IO.IOException`, which has
  `IOException(String, Exception)`
  ([IOException](https://learn.microsoft.com/en-us/dotnet/api/system.io.ioexception?view=net-10.0)), as the Transport's
  `ClosedChannelException` does. A `catch (SocketException)` does not see it. A constructor without a message leaves
  `Exception.Message` at the system default, where Java's `getMessage()` is null. `HttpProxyConnectException` stays the
  nested class of `HttpProxyHandler`.
- **Exception text.** `cause.toString()` in `setConnectFailure` is `ThrowableUtil.ThrowableToString` (the class name,
  `": "` and the message; owner decision 2026-11-24), because `Exception.ToString()` also appends the inner exception
  and the stack trace. `StringBuilder.append(Object)` writes `"null"` for a null reference, while the C#
  `Append(object)` writes nothing
  ([StringBuilder.Append](https://learn.microsoft.com/en-us/dotnet/api/system.text.stringbuilder.append?view=net-10.0)),
  so `exceptionMessage` writes an unset destination as `"null"` explicitly.
- **Fields and members.** C# has no `volatile long`; `connectTimeoutMillis` uses `Volatile.Read/Write`. Final Java
  methods are sealed overrides or non-virtual; `catch (Throwable)` is `catch (Exception)`. The `writeListener` field is
  assigned in the constructor, because a C# field initializer cannot refer to the instance.
- **HttpProxyHandler.** The HTTP message interfaces take the I prefix (`IFullHttpRequest`, `IHttpResponse`,
  `ILastHttpContent`), `CharSequence` is `ICharSequence`, `CharsetUtil.UTF_8/US_ASCII` are `Encoding.UTF8/ASCII` and
  `Unpooled.EMPTY_BUFFER` is `Unpooled.EmptyBuffer`, as in codec-http. `HttpClientCodecWrapper` implements
  `IChannelInboundHandler` and `IChannelOutboundHandler` and forwards every callback to the wrapped `HttpClientCodec`.
  The protected `newInitialMessage`, which `HttpProxyHandlerTest` calls through package access, has the internal
  forwarder `InvokeNewInitialMessage` ([codec-http-porting.md](codec-http-porting.md), Package access).
- **SOCKS handlers.** Java's `byte` is the C# `byte` of the codec-socks port, so `privateAuthMethod & 0xFF` keeps its
  value; `Collections.singletonList`/`Arrays.asList` are lists passed to the `IEnumerable` constructor of
  `DefaultSocks5InitialRequest`; the `Socks5AuthMethod` and status comparisons stay reference comparisons (codec-socks
  declares no `==` operator). `java.lang.Error` for the unreachable SOCKS5 branch is `InvalidOperationException`, as
  codec-base's `AsciiHeadersEncoder` maps it.

## Transport: lifecycle checks and queued-write completion (2026-11-25)

Java's `AbstractChannel.AbstractUnsafe` decides register (`if (isActive())` before `fireChannelActive`), bind,
disconnect, close and `flush0` with the virtual `isActive()`, and `AbstractNioChannel.connect` does the same. The
Transport port read its private `_active` flag at those points, so a channel whose `IsActive` override reports
something else was not treated as in Java. `HttpProxyHandlerTest` and `ProxyHandlerTest` use anonymous
`EmbeddedChannel` subclasses that report an inactive channel: in Java their registration fires no `channelActive`, so
the proxy handler queues the writes; in the port the registration fired `ChannelActive` and the handler sent its
initial message. `AbstractChannel` now reads `IsActive` at those points (register, bind, connect, disconnect, close and
its deregistration, `flush0`); `AbstractNioChannel` already did. `BeginRead` keeps reading the flag, because its check
holds a read for the CLR socket and 4.2's `beginRead()` has no `isActive()` check.

The same day `PendingWriteQueue` was changed to complete the producer of a queued write when the write completes, as
Java completes the `ChannelPromise` it hands to `write(msg, promise)`: the producer had been completed through a
thread-pool continuation and a submission to the executor, so `testRemoveOnProxyConnectionEventWritesPendingWrites`
(which checks `writeFuture.isSuccess()` right after the flush) failed under load. See
[transport-porting.md](transport-porting.md).

## Not ported

| Unit | Reason |
| --- | --- |
| `package-info.java` | Documentation-only package descriptor; the summary is kept in the manifest and the namespace table above. |
| `module-info.yml` | JPMS module descriptor input; the project references and `InternalsVisibleTo` are the CLR counterpart. |
| `reflect-config.json` | GraalVM reflection metadata; NativeAOT keeps handler metadata through the `DynamicallyAccessedMembers` annotations of `IChannelHandler`/`ChannelHandlerAdapter`. |
| `NativeImageHandlerMetadataTest` | Compares generated GraalVM metadata with the committed JSON; no CLR counterpart. |

`netty-testsuite-common` is a test dependency of handler-proxy, but no handler-proxy test refers to it: its
`LeakPresenceExtension` is registered for every module's tests through the JUnit service file and the
`-Djunit.jupiter.extensions.autodetection.enabled=true` argument of the parent `pom.xml` (`argLine.leak`). It is ported
as `Netty.NET.Testsuite.Common` and `Netty.NET.Handler.Proxy.Tests` imports `LeakPresenceExtension.props`, so every
handler-proxy test class runs under the leak presence detector, as in Java (2026-11-27, no leak found; see
[testsuite-common porting](testsuite-common-porting.md)).

## Test notes

- **ProxyHandlerTest.** The 17 proxy servers (HTTP, HTTPS, SOCKS4, SOCKS5 and SOCKS5 with private authentication; dead,
  intermediary and terminal modes) bind on the loopback in a static constructor that keeps the order of the static
  initializers and the static block; `@AfterAll` is the class fixture `StopServersFixture`, `@BeforeEach` the
  constructor and `@AfterEach` `Dispose()`. `@ParameterizedTest @MethodSource` are `[Theory] [MemberData]`, and
  `TestItem` is public because a theory parameter type must be as accessible as the method. `Collections.shuffle` is
  the same swap walk over `System.Random`, so a seed reproduces a C# order. `b.connect(...).channel()` is a channel
  factory that captures the new `NioSocketChannel`, because `Bootstrap.ConnectAsync` yields the channel only on
  success but calls the factory synchronously (as Transport's `SocketCloseForciblySourceTest` does).
  `Future.await(timeout)` is `Task.WhenAny` with `Task.Delay`; `CountDownLatch` is `CountdownEvent` with a guarded
  `Signal`; `java.lang.AssertionError` is xUnit's `FailException`; `String.valueOf(e)` is
  `ThrowableUtil.ThrowableToString`; `java.net.ConnectException` is the Transport's
  `SocketException(SocketError.ConnectionRefused)`.
- **ProxyServer.checkExceptions().** As in the source, it logs the recorded exceptions and never raises one: its loop
  only ends when the polled exception is null, so the final `if (t != null)` is never taken.
- **HttpProxyHandlerTest, resolved host names.** `new InetSocketAddress(InetAddress.getByName("localhost"), port)`
  keeps the host name `localhost` for `getHostString()`, while `IPEndPoint` keeps only the address and
  `HttpUtil.FormatHostnameForHttp` returns its numeric text (codec-http's
  `HttpUtilTest.testFormatHostnameForHttpFromResolvedAddressWithHostname` is CLR-inapplicable for the same reason).
  `testHostname`, `testHostHeaderWithHttpDefaultPort` and `testHostHeaderWithHttpsDefaultPort` resolve `localhost` with
  Resolver's `DefaultNameResolver` (the port of `InetAddress.getByName`) and expect that numeric text (bracketed for
  IPv6) where the source expects `localhost`; they still check the CONNECT URL and the `Host` header choice for ports
  80 and 443. The unresolved variants keep the source expectations.
- **HttpProxyHandlerTest, Mockito.** Mockito is Moq. The `ChannelPromise` of `connect(ctx, remote, local, promise)` is
  the `CancellationToken` the handler passes on, so `same(promise)` and `verify(ctx).connect(proxyAddress, null,
  promise)` match that token (`Times.Once`, the Mockito default); `verifyNoMoreInteractions(promise)` on the new mock
  checks no interaction and has no counterpart without a promise object. `new DefaultHttpHeaders(false)` is
  `DefaultHttpHeadersFactory.HeadersFactory().WithValidation(false).NewHeaders()`, as in codec-http's `HttpHeadersTest`.
- **Analyzer rules.** The test lifecycle methods are private (xUnit1013), `assertEquals(1, size())` is `Assert.Single`
  (xUnit2013) and the channel operations receive `TestContext.Current.CancellationToken` (xUnit1051).
