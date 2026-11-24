# handler porting

Netty.NET ports `netty-handler` of Netty 4.2.19.Final (pinned commit `64cc10f38ea5f5bd7eae48507817c66680d0afdc`) to
`src/Netty.NET.Handler`, with its original tests in `test/Netty.NET.Handler.Tests`: the nine packages address, flow,
flush, ipfilter, logging, pcap, stream, timeout and traffic (2026-11-23), and `io.netty.handler.ssl` with
`io.netty.handler.ssl.util` (2026-11-24), whose `SslHandler` runs on `System.Net.Security.SslStream` (see [SSL](#ssl)
and [handler-ssl-design.md](handler-ssl-design.md)). The [manifest](handler-porting-manifest.json) records every pinned
file of `handler/src` (316 files: 177 main and 95 test Java sources, `module-info.yml`, three main resources and 40
test resources) with its C# counterpart, its CLR adaptations and evidence, or the reason it is not ported. No entry is
`pending`. Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module handler
```

`handler` is also in the checker's default module list, so a run without `-Module` checks it with the codec and
pkitesting manifests; `-AllowPending` is no longer needed.

The checker verifies the file inventory and the source hashes of all 316 files. For every entry it also verifies that
every original comment (license, Javadoc and line comments) is present in the mapped C# files or recorded as belonging
to a deprecated member, a member that uses a JVM-only API or a member that needs a library that is not ported, that
every declared JUnit test is mapped to a C# test of the same name (case may differ) or carries a recorded disposition,
and the facts and evidence of every unit that is not ported. It fails on a `pending` entry. For the ssl port it was
extended (2026-11-24) in two ways: a unit counts as using a library when it imports one of the library's packages or
names one of its classes in a string literal for reflection (`Class.forName("org.conscrypt.Conscrypt")`), and the
members of a deprecated family are compared by Java package across the main and test source roots, with a test
accepted when it names the `via` helper it tests (`OpenJdkSelfSignedCertGeneratorTest` reaches the `@Deprecated`
`SelfSignedCertificate` through `OpenJdkSelfSignedCertGenerator`).

## Status

| Status | Files |
| --- | --- |
| `clr-adapted` (the ported main sources: 43 of the nine packages and 47 of ssl and ssl.util; and 13 ssl test classes and helpers with CLR-inapplicable methods or adapted helpers) | 103 |
| `verified` (tests whose every original method is mapped and test helpers without tests: 20 of the nine packages and 22 of ssl; and the 39 ssl test resources, copied byte for byte) | 81 |
| `jvm-only-replaced` (13 of the nine packages, 23 of ssl including its two GraalVM metadata files) | 36 |
| `library-not-ported` (ssl units that need netty-tcnative, Bouncy Castle, Conscrypt or the Amazon Corretto Crypto Provider, and their tests) | 78 |
| `deprecated-not-ported` (`@Deprecated` ssl units and the units that serve them) | 18 |

The checker reports:

```
handler manifest verified: 316 pinned files (103 clr-adapted, 18 deprecated-not-ported, 36 jvm-only-replaced, 78 library-not-ported, 81 verified), 331 mapped original tests, 107 CLR-inapplicable and 174 not-ported original tests, 44 deprecated-member, 77 JVM-only-API and 6 library-not-ported comment dispositions; 18 deprecated-not-ported, 36 JVM-only-replaced and 78 library-not-ported entries carry 1442 original comments.
```

Of the 612 declared original tests, 331 are mapped to C# tests with the same names (151 of the nine packages, 180 of
ssl and ssl.util), 107 are CLR-inapplicable (all ssl, see [SSL tests](#ssl-tests)) and 174 are not ported: 166 belong
to library-not-ported test classes (147 netty-tcnative, 14 Conscrypt, 3 Bouncy Castle, 2 Amazon Corretto Crypto
Provider), 4 to the deprecated-not-ported tests of `SelfSignedCertificate`, and 4 to JVM-only tests
(`NativeImageHandlerMetadataTest.collectAndCompareMetadata`, the two tests of `EnhancedX509ExtendedTrustManagerTest`
and the one of `SimpleTrustManagerFactoryTest`). Comments of deprecated members (5 of the nine packages, 39 of ssl),
of members that use a JVM-only API (77, all ssl) and of members that need a library that is not ported (6, ssl) are
recorded instead of ported, and the entries that are not ported carry 1442 original comments.

The generic mutation run rejected 19 of 19 broken copies of the manifest and restored it. With the ssl port the
solution builds with 0 warnings and 0 errors, the Transport checker passes, and the pkitesting tests are unchanged (37
passed, 37 skipped).

The C# run of `Netty.NET.Handler.Tests` (Windows 11 10.0.26300, .NET 10.0.8) executes 460 test cases: 432 pass, 28
are skipped and 0 fail, identical in three runs. The nine packages have 152 cases, because
`ChunkedWriteHandlerTest.testChunkedFileFailsWhenFileWasTruncated` is a theory with two rows: 151 pass and 1 is
skipped, `FileRegionThrottleTest.testGlobalWriteThrottle`, which is `@Disabled` in Java and `[Fact(Skip = ...)]` with
the same text in C# (see [Test notes](#test-notes)). ssl and ssl.util have 308 cases: 281 pass and 27 are skipped:

- 9 cases that configure cipher suites, because Windows has no `CipherSuitesPolicy` (`NewHandler` throws
  `PlatformNotSupportedException`): `SslHandlerTest.testHandshakeFailureCipherMissmatchTLSv12Jdk` and `...TLSv13Jdk`,
  `CipherSuiteCanaryTest.testHandshake`, and the 6 rows of
  `SSLEngineTest.clientInitiatedRenegotiationWithFatalAlertDoesNotInfiniteLoopServer`;
- 5 cases of `SniHandlerTest` (`testTinyFragments`, 4 rows, and `testTinyFirstFragment`), because SChannel rejects a
  first TLS record that carries only a few ClientHello bytes (see [Measurements](#measurements-and-platform-limits));
- `PkiTestingTlsTest.connectWithEd25519`, because the BCL has no Ed25519;
- the 12 rows of `SSLEngineTest.testMutualAuthValidClientCertChainTooLongFail{Optional,Require}ClientAuth`, which the
  JDK fixture `JdkSslEngineTest` overrides as `@Disabled` ("Does the JDK support a "max certificate chain length"?").

| Package | Ported main files | Ported test files | Mapped original tests | C# test cases |
| --- | --- | --- | --- | --- |
| address | 2 | 2 | 5 | 5 |
| flow | 1 | 1 | 21 | 21 |
| flush | 1 | 1 | 9 | 9 |
| ipfilter | 8 | 4 | 20 | 20 |
| logging | 3 | 1 | 20 | 20 |
| pcap | 8 | 3 (two helpers without tests) | 25 | 25 |
| stream | 6 | 2 | 34 | 35 (one theory with two rows) |
| timeout | 8 | 4 | 14 | 14 |
| traffic | 6 | 2 | 3 | 3 (one skipped) |
| ssl | 41 | 32 (19 verified, 13 clr-adapted) | 171 of 444 (107 CLR-inapplicable, 166 not ported) | 299 (27 skipped) |
| ssl.util | 6 | 3 | 9 of 16 (7 not ported) | 9 |
| **total** | 90 | 55 | 331 of 612 | 460 (28 skipped) |

Each package also has a `package-info.java` recorded as `jvm-only-replaced`. The ssl main files that are not ported
are 49 library-not-ported, 14 deprecated-not-ported and 13 jvm-only-replaced units (besides the two package-info
files), and the ssl test files that are not ported are 29 library-not-ported, 4 deprecated-not-ported and 6
jvm-only-replaced (see [Not ported](#not-ported)).

## Owner decisions (2026-11-23 and 2026-11-24)

- **Project.** The project, folder and assembly are `Netty.NET.Handler`, the namespace root of `io.netty.handler`
  (owner decision 2026-11-19, see [codec-base-porting.md](codec-base-porting.md)).
- **Namespaces.** Each package `io.netty.handler.<pkg>` is the namespace `Netty.NET.Handler.<Ns>` in the folder `<Ns>/`
  of the project, with `Ns` = `Address`, `Flow`, `Flush`, `IpFilter`, `Logging`, `Pcap`, `Ssl` (with `Ssl.Util` in
  `Ssl/Util/`), `Stream`, `Timeout` and `Traffic`. `ipfilter` takes the casing of its Java types (`IpFilterRule`,
  `IpSubnetFilter`), as `haproxy` became `HAProxy` ([codec-haproxy-porting.md](codec-haproxy-porting.md)). The tests
  are `Netty.NET.Handler.Tests.<Ns>` in the folder `<Ns>/` of the test project.
- **Name clashes (CS0102).** A Java method whose C# name would equal a nested type of the same type takes a trailing
  underscore: `PcapWriteHandler.builder()` is `Builder_()`, because a method cannot be declared next to the nested
  `Builder` class of the same name ([CS0102](https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0102)), as
  codec-compression's `status()` is `Status_()` ([codec-compression-porting.md](codec-compression-porting.md)) and
  `HAProxyTLV.type()` is `Type_()`. `PcapWriteHandler.state()` stays `State()`,
  because the enum `State` is a namespace-level type and does not clash; inside the class the simple name `State`
  binds to the method, so the enum is written `Pcap.State` (see [Name lookup](#shared-clr-adaptations)).
- **Order.** The non-ssl packages were ported first (2026-11-23), while `io.netty.handler.ssl` waited for the
  `SslStream` design. The owner decided that design on 2026-11-24 ([handler-ssl-design.md](handler-ssl-design.md)):
  `SslStream` behind a record-aligned mediation stream, BCL authentication options in place of `SSLEngine` (no
  SSLEngine facade), OpenSSL/Bouncy Castle/Conscrypt units library-not-ported, and pkitesting ported on the BCL. ssl
  and ssl.util were ported on the same day in three waves (see [SSL](#ssl)), and no entry is pending.

## Namespaces and assembly

The project references `Netty.NET.Handler.Codec` (codec-base, which brings Transport, Resolver, Buffer and Common), and
`InternalsVisibleTo` opens internals to `Netty.NET.Handler.Tests` only. The ssl port needs no further reference:
`SslStream` and the certificate and key types are BCL types. The test project also references `Netty.NET.PkiTesting`
(the certificates of the ssl tests, see [Lead decisions](#lead-decisions-2026-11-24)) and deploys the 39 ssl test
resources next to the test assembly.

| Java package | C# namespace | Original package summary |
| --- | --- | --- |
| `io.netty.handler.address` | `Netty.NET.Handler.Address` | Package to dynamically replace local / remote {@link java.net.SocketAddress}. |
| `io.netty.handler.flow` | `Netty.NET.Handler.Flow` | Package to control the flow of messages. |
| `io.netty.handler.flush` | `Netty.NET.Handler.Flush` | Package to control flush behavior. |
| `io.netty.handler.ipfilter` | `Netty.NET.Handler.IpFilter` | Package to filter IP addresses (allow/deny). |
| `io.netty.handler.logging` | `Netty.NET.Handler.Logging` | Logs the I/O events for debugging purpose. |
| `io.netty.handler.pcap` | `Netty.NET.Handler.Pcap` | Capture data and write into Pcap format which helps in troubleshooting. |
| `io.netty.handler.stream` | `Netty.NET.Handler.Stream` | Writes very large data stream asynchronously neither spending a lot of memory nor getting {@link java.lang.OutOfMemoryError}. For a detailed example, please refer to {@code io.netty.example.http.file}. |
| `io.netty.handler.timeout` | `Netty.NET.Handler.Timeout` | Adds support for read and write timeout and idle connection notification using a {@link io.netty.util.Timer}. |
| `io.netty.handler.traffic` | `Netty.NET.Handler.Traffic` | Implementation of a Traffic Shaping Handler and Dynamic Statistics. (The summary continues with the TrafficCounter/AbstractTrafficShapingHandler overview, the configure/doAccounting notes and the Channel/Global handler sharing rules; the full text is kept verbatim in the manifest entry of `traffic/package-info.java`.) |
| `io.netty.handler.ssl` | `Netty.NET.Handler.Ssl` | <a href="https://en.wikipedia.org/wiki/Transport_Layer_Security">SSL &middot; TLS</a> implementation based on {@link javax.net.ssl.SSLEngine} (in this port the engine is `System.Net.Security.SslStream`, ssl design decision 1) |
| `io.netty.handler.ssl.util` | `Netty.NET.Handler.Ssl.Util` | Utility classes that helps easier development of TLS/SSL applications. |
| test packages of the eleven above | `Netty.NET.Handler.Tests.Address`, `.Flow`, `.Flush`, `.IpFilter`, `.Logging`, `.Pcap`, `.Ssl`, `.Ssl.Util`, `.Stream`, `.Timeout`, `.Traffic` | (no test package-info) |

## Shared CLR adaptations

- **Access and names.** Java names are kept (only case or `_` differ; Task-returning methods add `Async`), Java methods
  stay methods (`state()` is `State()`, `isFirst()` is `IsFirst()`), and interfaces take the `I` prefix
  (`IIpFilterRule`, `IChunkedInput<B>`). Package-private types and members are `internal`. Private members of a nested
  class that the enclosing class reads are `internal` (`ChunkedWriteHandler.PendingWrite`, `PcapWriteHandler.Builder`,
  `IpSubnetFilterRule`'s nested rules), because C# does not open a nested class's private members to the enclosing
  class. `@Sharable` is `[Sharable]`. The tests reach the protected `IpSubnetFilter.accept(...)`, which Java tests reach
  through package access, through the internal forwarder `InvokeAccept`, as codec-base's
  `MessageToMessageDecoder.InvokeDecode` ([codec-base-porting.md](codec-base-porting.md)).
- **Enums.** Simple Java enums are C# enums (`IdleState`, `IpFilterRuleType`, `ByteBufFormat`, pcap's internal `State`
  and `ChannelType`); as value types they cannot be null, so their `checkNotNull` calls have no counterpart, and
  `IpSubnetFilter` keeps its null "no common rule type" state in `IpFilterRuleType?`
  ([enum](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/enum)). `LogLevel` and pcap's
  `TCPPacket.TCPFlag` declare constructors, fields and methods, so they are sealed classes with static readonly
  instances, as codec-base's `Base64Dialect`.
- **Exceptions.** `IllegalArgumentException` is `ArgumentException`, `IllegalStateException` is
  `InvalidOperationException`, `catch (Throwable)` is `catch (Exception)`, `AssertionError` is `UnreachableException`
  ([UnreachableException](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.unreachableexception?view=net-10.0)),
  `assert` is `Debug.Assert`, and `serialVersionUID` has no counterpart.
- **Executors and scheduling.**
  - `ctx.executor().schedule(...)` is `ScheduleAsync` and `scheduleAtFixedRate(...)` is `ScheduleAtFixedRateAsync`
    (`src/Netty.NET.Common/Concurrent/EventExecutorSchedulingExtensions.cs`). The `Future<?>`/`ScheduledFuture<?>` of
    scheduled work is the `CancellationTokenSource` whose token was passed (`cancel(...)` is `Cancel()`), as Transport's
    `AbstractNioChannel` keeps `connectTimeoutFuture`; `cancel(true)` interrupts nothing ("No worker is interrupted").
  - The scheduled Tasks are not observed, as nothing observes the Java futures, so an executor that rejects the work or
    cannot schedule it yields a faulted Task instead of a thrown exception.
  - `ScheduledExecutorService` is `IEventExecutorGroup` and `EventExecutor` is `IEventExecutor` (`TrafficCounter`,
    `GlobalTrafficShapingHandler`).
  - `FlushConsolidationHandler` keeps the `Future` of its submitted flush task only to `cancel(false)` it. It is a
    `CancellationTokenSource` whose token goes to `SubmitAsync`, which does not run a task cancelled before it starts,
    while a started task runs to its end, as
    [Future.cancel(false)](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/concurrent/Future.html#cancel(boolean))
    allows.
- **Durations.** A `TimeSpan` replaces each `(long, TimeUnit)` pair (`IdleStateHandler`, `ReadTimeoutHandler` and
  `WriteTimeoutHandler` constructors), as recorded in [common-clr-design.md](common-clr-design.md) ("Monotonic time and
  duration conversion"), so the `TimeUnit` parameter and its `checkNotNull` have no counterpart. A positive TimeSpan
  converts to nanoseconds with saturation, as `TimeUnit.toNanos` does (`MIN_TIMEOUT_NANOS` stays 1 ms). The nanosecond
  delays handed to the executor round up to the 100 ns tick
  ([TimeSpan.Ticks](https://learn.microsoft.com/dotnet/api/system.timespan.ticks)), so an idle or write timeout task
  runs at most 99 ns after its Java deadline and never before it.
- **Task promises.** `ChannelFuture`/`ChannelPromise` are `Task` ([transport-porting.md](transport-porting.md)). The
  outbound operations `connect/bind/disconnect/close/deregister/write(ctx, ..., promise)` are the Task-returning
  `*Async(ctx, ..., CancellationToken)` overrides, and the returned Task replaces the promise
  ([codec-base-porting.md](codec-base-porting.md), Outbound operations). Where the source completes a caller's promise
  later (`ResolveAddressHandler`, the queued writes of `ChunkedWriteHandler` and of the traffic handlers), a
  `TaskCompletionSource` stands for it and `TrySetFromTask` copies the outcome of the Task it waits for
  ([common-task-composition.md](common-task-composition.md)). A queued write keeps the caller's `CancellationToken`: the
  token cancels the queued promise at once, as `ChannelPromise.cancel(...)` does
  ([CancellationToken.UnsafeRegister](https://learn.microsoft.com/dotnet/api/system.threading.cancellationtoken.unsaferegister)),
  and it is handed to `ctx.WriteAsync` when the message is written.
- **Listener timing.** Java's `DefaultPromise` notifies a listener at once when the promise completes on its event loop,
  and otherwise through `executor.execute`
  ([DefaultPromise.notifyListeners](https://github.com/netty/netty/blob/64cc10f38ea5f5bd7eae48507817c66680d0afdc/common/src/main/java/io/netty/util/concurrent/DefaultPromise.java)).
  A listener is a Task continuation; a synchronous continuation runs at once when the Task has already completed
  ([TaskContinuationOptions](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcontinuationoptions?view=net-10.0)).
  The Transport's operation Tasks (writes, connects, the `CloseFuture`) come from `TaskCompletionSource`s with
  `RunContinuationsAsynchronously`, which "Forces continuations added to the current task to be executed
  asynchronously" ([TaskCreationOptions](https://learn.microsoft.com/dotnet/api/system.threading.tasks.taskcreationoptions)),
  so a `ContinueWith(..., ExecuteSynchronously, TaskScheduler.Default)` continuation of a Task that completes later runs
  on a thread-pool thread after the operation completed. The port uses three listener policies:
  - `IdleStateHandler` and `WriteTimeoutHandler` attach their listeners with `IdleStateHandler.ExecutorTaskScheduler`,
    a `TaskScheduler` over `ctx.Executor`. The completing thread queues the continuation to it
    ([TaskScheduler.QueueTask](https://learn.microsoft.com/dotnet/api/system.threading.tasks.taskscheduler)), and it
    runs the continuation at once on the event loop or hands it to `Execute`, the `notifyListeners()` policy. With
    `TaskScheduler.Default`, `IdleStateHandlerTest.testWriterNotIdle`, `testAllNotIdle`, `testObserveWriterIdle` and
    `testObserveAllIdle` failed in every run.
  - `ChunkedWriteHandler` attaches a continuation on `TaskScheduler.Default` that runs the listener at once when it
    runs on the event loop and otherwise through `executor.Execute`, logging a rejected submission
    ([common-native-progress.md](common-native-progress.md)). Only a write that is not done yet gets a listener; when
    the write completes, its continuation runs on a thread-pool thread, so the listener normally runs as a later
    event-loop task.
  - The other handlers (`DynamicAddressConnectHandler`, `ResolveAddressHandler`, `AbstractRemoteAddressFilter`,
    `UniqueIpFilter` and the traffic handlers' promise copies) use plain synchronous continuations on
    `TaskScheduler.Default`. `UniqueIpFilter` therefore forgets an address shortly after the close (see
    [IP filter](#ip-filter)).
- **java.io and NIO (owner decision: JVM-only families replaced without facades).**
  - `InputStream`/`OutputStream` are `System.IO.Stream`. `OutputStream.write(byte[])` is `Write(array, 0, length)`, as
    Java's `write(byte[])` calls `write(b, 0, b.length)`
    ([OutputStream](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/OutputStream.html)).
    `ByteBuf.writeBytes(InputStream, n)` is `WriteBytes(Stream, n)`, which returns the `Stream.Read` count; `Read`
    "returns 0 only if zero bytes were requested or when there is no more data in the stream"
    ([Stream.Read](https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.read?view=net-10.0)), so a zero count
    is Java's -1.
  - `java.io.File` is `FileInfo` ([common-clr-design.md](common-clr-design.md)); `RandomAccessFile` and the NIO
    `FileChannel` are `FileStream`, the file type that Transport's `DefaultFileRegion` takes
    ([transport-porting.md](transport-porting.md)). `isOpen()` is `CanRead`, which is false once a
    `FileStream`/`MemoryStream` is closed
    ([FileStream.CanRead](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.canread?view=net-10.0),
    [MemoryStream.CanRead](https://learn.microsoft.com/en-us/dotnet/api/system.io.memorystream.canread?view=net-10.0)).
  - `RandomAccessFile.readFully` is `Stream.ReadExactly`, and `EOFException` is `EndOfStreamException`, an
    `IOException` ([ReadExactly](https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.readexactly?view=net-10.0),
    [EndOfStreamException](https://learn.microsoft.com/en-us/dotnet/api/system.io.endofstreamexception?view=net-10.0)).
  - `Closeable` keeps `close()` as `Close()` and implements `IDisposable.Dispose()` with it (`PcapWriter`,
    `PcapWriteHandler`), as codec-compression's `IDecompressor` does for `AutoCloseable`
    ([codec-compression-porting.md](codec-compression-porting.md)).
- **Addresses.**
  - `SocketAddress` is `EndPoint`, `InetSocketAddress` is `IPEndPoint` and `InetAddress` is `IPAddress`
    ([codec-dns-porting.md](codec-dns-porting.md)); `instanceof Inet4Address/Inet6Address` is the `AddressFamily` test
    (`InterNetwork`/`InterNetworkV6`).
  - The NIO channels report the IPv4 peers of dual-mode sockets in IPv4 form (`AbstractNioChannel.Normalize`), so the
    filters and the pcap handler see the addresses the JDK would.
  - `SocketUtils.addressByName` (`InetAddress.getByName`) is the `DefaultNameResolver` lookup that Transport's
    `AbstractBootstrap` uses for the same call (JDK literal rules, an IPv4-mapped literal becomes IPv4,
    `UnknownHostException` is `SocketException`).
  - An address prints as `EndPoint.ToString()` (`0.0.0.0:80`), not as Java's `0.0.0.0/0.0.0.0:80`
    ([IPEndPoint.ToString](https://learn.microsoft.com/en-us/dotnet/api/system.net.ipendpoint.tostring?view=net-10.0),
    [InetSocketAddress](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/InetSocketAddress.html)),
    as recorded for codec-dns. `IpSubnetFilterRule` keeps the JDK text of `InetAddress.getHostAddress()` (see
    [IP filter](#ip-filter)).
- **Logging.** `InternalLogger`/`InternalLogLevel` are Common's `IInternalLogger`/`InternalLogLevel` from
  `InternalLoggerFactory` (`src/Netty.NET.Common/Internal/Logging/InternalLoggerFactory.cs`); `Class<?>` is `Type`,
  and a logger named after a class uses `Type.FullName`. The tests capture log events through a mocked factory (see
  [Test notes](#test-notes)).
- **Text.** Numbers in messages and `toString()` use the invariant culture. Debug log messages and `toString()` keep
  Java's `"true"`/`"false"`, where
  [Boolean.ToString](https://learn.microsoft.com/dotnet/api/system.boolean.tostring) gives `"True"`/`"False"`.
  `String.valueOf(null)` is `"null"`.
- **Channel internals.** `Channel.Unsafe` is folded into `AbstractChannel` ([transport-porting.md](transport-porting.md)),
  so `unsafe().outboundBuffer()` is `AbstractChannel.OutboundBuffer`. Java's buffer is null once `close()` starts; the
  Transport keeps a closed buffer whose `IsClosed` is internal, so the traffic handlers treat a channel that is no
  longer open as the null buffer, as `PendingBytesTracker` does. `IdleStateHandler`'s idle tasks never inspect a closed
  buffer, because they return once the channel is closed. `System.identityHashCode` is `RuntimeHelpers.GetHashCode`
  ([page](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.runtimehelpers.gethashcode)).
- **Name lookup.** Inside `Netty.NET.Handler.*` the simple names `Stream`, `Timeout`, `Flow` and `Logging` bind to the
  sibling namespaces, so the code writes the BCL types in full (`System.IO.Stream`, and `System.TimeoutException` in
  `FlowControlHandlerTest`'s exchanger). A method named like a type hides the type in expressions, so
  `LoggingHandler.ByteBufFormat()` writes the constants `Logging.ByteBufFormat.X`, `PcapWriteHandler.State()` writes
  `Pcap.State.X`, and `AbstractTrafficShapingHandler.TrafficCounter()` writes
  `Traffic.TrafficCounter.MilliSecondFromNano()`.

## Address, flush, IP filter and logging

### Address

- **DynamicAddressConnectHandler** is an abstract `ChannelOutboundHandlerAdapter`. The final `connect(...)` is a sealed
  override of `ConnectAsync`: `promise.setFailure(e)` after `localAddress()`/`remoteAddress()` throws is the returned
  faulted Task, and the listener that removes the handler after a successful connect is a synchronous continuation.
  `localAddress(...)`/`remoteAddress(...)` are protected virtual `LocalAddress`/`RemoteAddress`; `throws Exception` has
  no C# counterpart.
- **ResolveAddressHandler** takes `AddressResolverGroup<? extends SocketAddress>`. C# has no wildcard, a constructor
  cannot declare its own type parameter, and a generic class is invariant (variance is limited to generic interfaces and
  delegates, [variance](https://learn.microsoft.com/en-us/dotnet/standard/generics/covariance-and-contravariance)), so
  the class is `ResolveAddressHandler<T> where T : EndPoint`, which accepts any `AddressResolverGroup<T>`
  (`DefaultAddressResolverGroup` is `AddressResolverGroup<IPEndPoint>`), as Transport made
  `Bootstrap.resolver(AddressResolverGroup<?>)` the generic method `Resolver<T>`. On the resolution path the promise is
  a `TaskCompletionSource`: a faulted or cancelled resolve Task is copied with `TrySetFromTask`,
  `ctx.connect(future.getNow(), localAddress, promise)` copies the connect Task into it, and the handler is removed
  after the connect is started, as in the source. This adds a type parameter to a non-generic Java type (see
  [Owner review items](#owner-review-items)).

### Flush

- **FlushConsolidationHandler** is a `ChannelDuplexHandler`. The anonymous flush `Runnable` is an `Action` lambda over
  the handler fields (null when `consolidateWhenNoReadInProgress` is false). `eventLoop().submit(flushTask)` is
  `ctx.Channel.EventLoop.SubmitAsync(flushTask, token)`, with the cancellation described under Executors and scheduling;
  the returned Task is not observed, as the source keeps the `Future` only to cancel it.
  `DEFAULT_EXPLICIT_FLUSH_AFTER_FLUSHES` is a `const`.

### IP filter

- **AbstractRemoteAddressFilter.** `T extends SocketAddress` is `T : EndPoint`. Java's unchecked `(T)` cast of
  `remoteAddress()` is erased, and the bridge method of a narrower `accept(...)` override checks the type inside the
  `try`, so a mismatch closes the channel; the checked C# cast is made inside the `try` for the same outcome.
  `ChannelFutureListener.CLOSE` on the `channelRejected(...)` future is a synchronous continuation that closes
  `ctx.Channel` (a Task has no channel), as codec-base's `MessageAggregator` does.
- **IpSubnetFilter.** `Collections.sort` is stable
  ([Collections.sort](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Collections.html#sort(java.util.List,java.util.Comparator)))
  and `List.Sort` is not, so `sortAndFilter` iterates the stable `Enumerable.OrderBy`
  ([OrderBy](https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.orderby?view=net-10.0)), which keeps the
  Java order of equal rules and so the parent rule named in the overlap message. `Arrays.binarySearch` is
  `Array.BinarySearch<object>`, which also passes the array element first
  ([ArraySortHelper](https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/ArraySortHelper.cs)),
  so `IpSubnetFilterRuleComparator` receives the rule as `o1` and the `IPEndPoint` as `o2`, as in Java.
- **IpSubnetFilterRule.**
  - `ipAddressWithCidr.split("/")` drops trailing empty strings
    ([String.split](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html#split(java.lang.String))),
    so they are removed after `string.Split`. `Integer.parseInt` is `int.Parse` with a leading sign and the invariant
    culture; its `NumberFormatException` is `FormatException`/`OverflowException`, the mapping of codec-base's
    `CharSequenceValueConverter`. An invalid host name that `Dns` rejects with `ArgumentException` stays an
    `ArgumentException`.
  - The `InetAddress` constructor keeps the JDK text of `InetAddress.getHostAddress()`: the dotted quad for IPv4 and,
    for IPv6, eight uncompressed hexadecimal groups plus `%scope`
    ([OpenJDK Inet6Address](https://raw.githubusercontent.com/openjdk/jdk17u/master/src/java.base/share/classes/java/net/Inet6Address.java)),
    while `IPAddress.ToString()` compresses with `::`
    ([IPAddress.ToString](https://learn.microsoft.com/en-us/dotnet/api/system.net.ipaddress.tostring?view=net-10.0)).
    A scope id of 0 counts as none, because `IPAddress` has no "scope set" flag. `getIpAddress()` and `toString()`
    therefore keep the Java result.
  - Subnet arithmetic: Java `int` and C# `int` are both signed 32-bit, so the IPv4 masks, `compareInt(x ^ MIN_VALUE,
    ...)` and `(int) (-1L << 32 - cidrPrefix)` (shift count masked,
    [shift operators](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/bitwise-and-shift-operators);
    unchecked narrowing,
    [numeric conversions](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/numeric-conversions))
    give the Java values. `java.math.BigInteger` is `System.Numerics.BigInteger`, whose `&` and `<<` use the same
    two's-complement semantics
    ([Java](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/math/BigInteger.html),
    [&](https://learn.microsoft.com/en-us/dotnet/api/system.numerics.biginteger.op_bitwiseand?view=net-10.0),
    [<<](https://learn.microsoft.com/en-us/dotnet/api/system.numerics.biginteger.op_leftshift?view=net-10.0)); the IPv6
    value is `new BigInteger(GetAddressBytes(), isUnsigned: true, isBigEndian: true)`.
- **UniqueIpFilter.** `ConcurrentHashMap.newKeySet()` is a `ConcurrentDictionary<IPAddress, bool>` used as a set (`add`
  is `TryAdd`, which returns false if the key exists; `remove` is `TryRemove`). The `closeFuture()` listener is a
  continuation of the `CloseFuture` Task. Java notifies it during the close on the event loop; Transport's `CloseFuture`
  is a `TaskCompletionSource` with `RunContinuationsAsynchronously`
  ([TaskCreationOptions](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcreationoptions?view=net-10.0)),
  so the address is forgotten on a thread-pool thread shortly after the close.
- **RuleBasedIpFilter.** The deprecated `RuleBasedIpFilter(IpFilterRule...)` is not ported (see [Not ported](#not-ported));
  the tests use the replacement with `acceptIfNotFound = true`, the value the deprecated constructor passed.

### Logging

- **LoggingHandler** logs through Common's `InternalLoggerFactory` (see Logging above). `level()` and `byteBufFormat()`
  are the methods `Level()` and `ByteBufFormat()`, and the format methods are protected virtual. An exception argument
  is rendered as `Throwable.toString()` (class name, `": "`, message;
  [Throwable.toString](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Throwable.html#toString())),
  because `Exception.ToString()` also appends inner exceptions and the stack trace
  ([Exception.ToString](https://learn.microsoft.com/en-us/dotnet/api/system.exception.tostring?view=net-10.0)). Address
  arguments print as `EndPoint.ToString()` (`CONNECT: 0.0.0.0:80`). The `(length % 15)` row estimate of the source is
  kept.
- **LogLevel** is a sealed class with static readonly instances and a private constructor; a `LogLevel` variable can be
  null, which keeps `LoggingHandler`'s `checkNotNull(level, "level")`. `ToString()` returns the constant name and
  `toInternalLevel()` is `ToInternalLevel()`; the implicit `values()`/`valueOf(String)` are not added (no caller).
  **ByteBufFormat** is a C# enum.

## Pcap and stream

### Stream

- **IChunkedInput.** `ChunkedWriteHandler` matches any `ChunkedInput<?>`. The interface is `IChunkedInput<out B> where
  B : class`: the covariant parameter makes every `IChunkedInput<B>` an `IChunkedInput<object>`, as the wildcard type
  is ("Covariance and contravariance support reference types",
  [out (generic modifier)](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/out-generic-modifier)),
  and the class constraint matches Java type arguments, which are reference types (as codec-base's
  `MessageAggregator`). The deprecated `readChunk(ChannelHandlerContext)` is not ported (see [Not ported](#not-ported)).
- The public members of `ChunkedStream`, `ChunkedFile`, `ChunkedNioFile` and `ChunkedNioStream` are virtual, because the
  Java classes are not final.
- **ChunkedStream.** `PushbackInputStream` has no BCL counterpart: the byte that `isEndOfInput()` reads ahead is kept in
  a one-byte pushback field and read first, as `PushbackInputStream.available()`/`read(...)` do
  ([PushbackInputStream](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/PushbackInputStream.html)).
  `Stream` has no `available()`: a seekable stream reports `Length - Position`, the bytes that
  `ByteArrayInputStream`/`FileInputStream` report, and any other stream 0, the `InputStream` default
  ([InputStream](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/InputStream.html),
  [Stream.Length](https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.length?view=net-10.0)).
- **ChunkedFile.** `new RandomAccessFile(file, "r")` opens a read-only `FileStream`
  ([RandomAccessFile](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/RandomAccessFile.html));
  `seek(offset)` is `Seek(offset, SeekOrigin.Begin)`. `readFully(buf.array(), buf.arrayOffset(), n)` reads into the heap
  buffer's writable memory (`ByteBuf.AsSpan`, the Buffer port's replacement of `array()`/`arrayOffset()`,
  [buffer-porting.md](buffer-porting.md)) with `ReadExactly`.
- **ChunkedNioFile** reads positionally through `ByteBuf.WriteBytes(SafeFileHandle, long, int)`, the Buffer port's
  `FileChannel` overload, which never moves the file position and reports the end of the file as 0
  ([buffer-porting.md](buffer-porting.md), Positioned file transfers;
  [FileChannel](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/channels/FileChannel.html)), so a
  non-positive count fails with `EndOfStreamException`. A closed `FileStream` passed to `ChunkedNioFile(FileStream,
  int)` throws the Transport `ClosedChannelException`, as `FileChannel.size()` does.
- **ChunkedNioStream.** The `ReadableByteChannel` is a `Stream`, whose `Read` blocks until at least one byte is
  available (the class requires a blocking channel), and its NIO `ByteBuffer` is a byte array with a fill position
  ([ReadableByteChannel](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/channels/ReadableByteChannel.html)).
  A private `Read()` helper reads up to the remaining space and returns -1 at the end of the stream.
- **ChunkedWriteHandler.**
  - `write(ctx, msg, promise)` is `WriteAsync(ctx, msg, cancellationToken)`. A queued write gets its own
    `TaskCompletionSource`, whose continuations run asynchronously as the Transport `ChannelOutboundBuffer` write
    promises do, and which `PendingWrite` completes; the token cancels it, as `ChannelPromise.cancel(...)` cancels the
    promise. A queued plain message is written with `ctx.WriteAsync(msg, token)`, and its outcome is copied with
    `TrySetFromTask`.
  - `addListener` follows the `ChunkedWriteHandler` policy described under Listener timing. `isSuccess()`/`cause()` are
    `IsCompletedSuccessfully` and the inner exception, or a `TaskCanceledException` for a cancelled write.
    `future.channel()` is the handler context's channel, because a Task has no channel.
  - **Progress.** `ChannelProgressivePromise` is the channel's `IChannelConfig.WriteProgress` observer
    ([transport-porting.md](transport-porting.md)). `PendingWrite.progress` keeps `DefaultProgressivePromise`'s checks
    and reports a `ChannelWriteProgress` for the queued write's Task. Because the observer is channel-wide, the chunk
    writes themselves are also reported by `ChannelOutboundBuffer` (with their own `Operation` Task), whereas Java
    writes the chunks with plain promises.
  - `ArrayDeque` is `Queue<T>`; `peek`/`poll` are `TryPeek`/`TryDequeue`, and `remove()` is `Dequeue()`, which also
    throws when the queue is empty
    ([ArrayDeque](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/ArrayDeque.html),
    [Queue.TryPeek](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.queue-1.trypeek?view=net-10.0),
    [Queue.Dequeue](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.queue-1.dequeue?view=net-10.0)).

### Pcap

- **Addresses.** `getPort()` is `Port`, `getAddress()` is `Address` and `InetAddress.getAddress()` is
  `GetAddressBytes()`. Java converts IPv4-mapped input to `Inet4Address`
  ([Inet6Address](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/Inet6Address.html)); the
  Transport NIO channels already report mapped endpoints in IPv4 form, while a caller-supplied mapped `IPEndPoint`
  (builder `forceTcpChannel`/`forceUdpChannel`) keeps the IPv6 family. `isAnyLocalAddress()` is equality with
  `IPAddress.Any`/`IPv6Any`, as in Transport's `AbstractChannel.IsAnyLocalAddress`, which is internal to Transport.
  `InetAddress.getByAddress` is `new IPAddress(byte[])`.
- **PcapWriteHandler.** `SocketChannel`/`ServerSocketChannel`/`DatagramChannel` are the Transport
  `ISocketChannel`/`IServerSocketChannel`/`IDatagramChannel`. `AtomicReference<State>` is a `volatile` enum field with
  `Interlocked.CompareExchange` for `compareAndSet` ([transport-porting.md](transport-porting.md)). `builder()` is
  `Builder_()` (CS0102), and the enum is written `Pcap.State` inside the class. The private nested enum `ChannelType`
  and the `Builder`'s fields and constructor are internal, because the enclosing class reads them and a field cannot be
  more accessible than its type (CS0052). `WildcardAddressHolder`'s static initializer is a static constructor.
  `toString()` keeps the Java boolean words, invariant-culture numbers and `"null"` for unset fields; the addresses
  print as `IPEndPoint.ToString()`.
- **PcapWriter.** `synchronized (outputStream)` is `lock (outputStream)`, and `System.currentTimeMillis()` is
  `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`
  ([ToUnixTimeMilliseconds](https://learn.microsoft.com/en-us/dotnet/api/system.datetimeoffset.tounixtimemilliseconds?view=net-10.0)).
- **Packet writers.** `PcapHeaders`, `EthernetPacket`, `IPPacket`, `TCPPacket` and `UDPPacket` are `internal sealed`
  classes with private constructors. `TCPPacket`'s `TCPFlag...` varargs parameter is `params TCPFlag[]`, and the
  `(int)` casts of the segment and acknowledgment numbers are unchecked (Java narrowing keeps the low 32 bits).
  `IPPacket.IPV6_VERSION_TRAFFIC_FLOW` keeps the source's decimal `60000000`. See [Byte values](#byte-values).

## Timeout, flow and traffic

### Timeout

- **IdleStateHandler.**
  - The constructors `(int, int, int)` (seconds), `(TimeSpan, TimeSpan, TimeSpan)` and `(bool, TimeSpan, TimeSpan,
    TimeSpan)` replace the `TimeUnit` overloads (see Durations).
  - `schedule(ctx, task, delay, unit)`, visible for testing, is the internal virtual `Schedule(ctx, Action, TimeSpan)`
    returning the `CancellationTokenSource` of the scheduled work. The ticker is `ctx.Executor.Ticker()`, as in Java
    (the EmbeddedChannel frozen clock in the tests).
  - The write listener uses `ExecutorTaskScheduler` (see Listener timing); the listener field is assigned in the
    constructor, because a field initializer cannot reference the instance (CS0236).
  - The outbound buffer's `current()`, `totalPendingWriteBytes()` and `currentProgress()` are the `Current`,
    `TotalPendingWriteBytes` and `CurrentProgress` properties (see Channel internals). The abstract `Runnable`
    `AbstractIdleTask` and its subclasses are nested classes with `Run()` that reach the handler through an explicit
    reference.
- **WriteTimeoutHandler.** `WriteTimeoutHandler(long, TimeUnit)` is `WriteTimeoutHandler(TimeSpan)` with the same
  saturation, minimum and 100 ns rounding. Because the promise is the Task returned by `ctx.WriteAsync`,
  `scheduleTimeout` runs right after the write is handed to the next handler instead of right before it; a write that
  has already completed cancels the timeout through its listener at once, as a listener added to a completed Java
  promise runs at once. `scheduledFuture.isDone()` is the scheduled Task's `IsCompleted` (a rejected schedule completes
  it at once). The listener is `OperationComplete(Task)` as a continuation on `ExecutorTaskScheduler`, and
  `operationComplete` keeps the hand-off to the executor of [netty issue 11053](https://github.com/netty/netty/issues/11053).
- **ReadTimeoutHandler.** `ReadTimeoutHandler(long, TimeUnit)` is `ReadTimeoutHandler(TimeSpan)`; the final
  `channelIdle` is a sealed override.
- **Exceptions.** `TimeoutException` extends the Transport `ChannelException`; its package-private constructors are
  internal, and the shared constructor calls `ChannelException(message, null, shared)`. The `fillInStackTrace()`
  override, which returns `this` without recording frames, is an override of the virtual `Exception.StackTrace` that
  returns null ("If no stack trace is available ... the value is null",
  [Exception.StackTrace](https://learn.microsoft.com/en-us/dotnet/api/system.exception.stacktrace)).
  `ReadTimeoutException`/`WriteTimeoutException` are sealed with a static readonly `INSTANCE`.
- **IdleStateEvent.** `state()` and `isFirst()` are the methods `State()` and `IsFirst()`. `toString()` uses
  `StringUtil.SimpleClassName` and the enum constant name; the private `DefaultIdleStateEvent` is a private sealed
  nested class.

### Flow

- **FlowControlHandler.** The `ArrayDeque<Object>` subclass `RecyclableArrayDeque` is a `Queue<object>` subclass, the
  BCL circular-array FIFO queue, because the handler uses only `offer`/`poll`/`size`/`isEmpty`/`clear` (`Enqueue`,
  `TryDequeue` through a `Poll` helper that returns null when empty, `Count`, `Clear`). Its `ToString()` returns the
  `AbstractCollection.toString()` text (`[a, b]`) for the trace message, because `Queue<T>` inherits
  `Object.ToString()`. The anonymous `Recycler` subclass is `Recycler.Create` with the `newObject` factory, as the
  Transport pools `WriteTask` and `ChannelOutboundBuffer.Entry`. `isQueueEmpty()` is internal (package-private).

### Traffic

- **Volatile longs.** C# volatile fields cannot be `long`
  ([volatile](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/volatile)), so the `volatile long`
  fields are read and written with `Volatile.Read/Write`; the volatile `float` and `boolean` fields stay volatile.
- **Monitors.** The `synchronized` methods of `TrafficCounter` and the overrides in `GlobalChannelTrafficCounter` lock
  `this`, the monitor a Java synchronized method uses, so subclasses share it; `synchronized (this)` and
  `synchronized (perChannel)` blocks lock the same objects. This sets aside the C# guidance against locking `this`
  ([lock](https://learn.microsoft.com/dotnet/csharp/language-reference/statements/lock)).
- **TrafficCounter.** The monitor is scheduled with `IEventExecutorGroup.ScheduleAtFixedRateAsync` on the executor's
  own queue; `AtomicLong` is Common's `AtomicLong` (`getAndSet` is `Set`, which returns the previous value).
  `System.nanoTime()` is `SystemTimer.NanoTime()` and `System.currentTimeMillis()` is
  `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`, as in `DefaultChannelId`. The deprecated
  `readTimeToWait`/`writeTimeToWait(size, limit, maxTime)` are ported with `[Obsolete]` (see [Not ported](#not-ported)).
  In them, the second local named `time` is `time2`, because a C# local is in scope in its whole block (CS0136).
- **AbstractTrafficShapingHandler.**
  - `submitWrite` returns the Task of the write: `ctx.WriteAsync`'s Task when the message is written at once, and
    otherwise the Task of a `TaskCompletionSource` kept with the caller's `CancellationToken` in `ToSend` (see Task
    promises); `releaseAndFailQueuedWrite` fails that `TaskCompletionSource`.
  - `ArrayDeque<ToSend>` is `LinkedList<ToSend>`, O(1) at both ends
    ([LinkedList](https://learn.microsoft.com/dotnet/api/system.collections.generic.linkedlist-1)).
    `java.nio.channels.ClosedChannelException` is the Transport `ClosedChannelException`.
  - `AttributeKey<T>` requires a reference type, so `READ_SUSPENDED` holds a boxed `bool` as `AttributeKey<object>`,
    and `REOPEN_TASK` holds the `Action` of the nested `ReopenReadTimerTask.Run`.
  - `setUserDefinedWritability` uses `AbstractChannel.OutboundBuffer` (see Channel internals). The constructor calls
    the virtual `userDefinedWritabilityIndex()`, as in Java.
  - **Visibility.** The package-private members that `GlobalChannelTrafficShapingHandler` overrides as `protected`
    (`submitWrite`, `checkWaitReadTime`, `informReadOperation`) and `doAccounting` (called by `TrafficCounter`) are
    `protected internal` (CS0507). A subclass in another assembly can therefore override `submitWrite`, which Java
    allows only inside the package.
- **Global handlers.** `ConcurrentHashMap<Integer, PerChannel>` is `ConcurrentDictionary<int, PerChannel>` keyed by
  `GetHashCode()`. `channelQueues.values()` is the `ConcurrentDictionary.Values` copy
  ([Values](https://learn.microsoft.com/dotnet/api/system.collections.concurrent.concurrentdictionary-2.values)), where
  Java iterates a weakly consistent view; both visit every channel present when the iteration starts.
  `GlobalChannelTrafficShapingHandler.channelTrafficCounters()` returns an `ICollection<TrafficCounter>` view, the
  nested `TrafficCounterCollection`: `Add`, removing a present element and clearing a non-empty collection throw
  `NotSupportedException`, as `AbstractCollection` does through the iterator's `UnsupportedOperationException`, and
  `IsReadOnly` is true. The `MAX_DEVIATION` text is the invariant `float.ToString()`
  (`0.4`, as `Float.toString`). `GlobalTrafficShapingHandler.release()` is the public non-virtual `Release()`.

## SSL

`io.netty.handler.ssl` and `io.netty.handler.ssl.util` were ported on 2026-11-24 following
[handler-ssl-design.md](handler-ssl-design.md), in three waves, after which the lead recorded the remaining units that
are not ported, ported the shared test helper `SilenceExceptionHandler`, copied the test resources and took the
decisions under [Lead decisions](#lead-decisions-2026-11-24).

| Wave | Scope | Files | Tests |
| --- | --- | --- | --- |
| 1, engine-independent | SNI and ClientHello parsing, `SslUtils` record parsing and TLS 1.3 probes, PEM, ALPN configuration and names, cipher-suite constants, converters and filters, events, exceptions, `SslContextOption`, `SslHandlerCoalescingBufferQueue`; ssl.util lazy certificates and `ThreadLocalInsecureRandom` | 35 main `clr-adapted`, 2 package-info; 11 test files `verified` | 41 mapped; 52 cases, all pass |
| 2, ssl-handler | `SslHandler`, `ApplicationProtocolNegotiationHandler` on `SslStream` | 2 `clr-adapted`; `ApplicationProtocolAccessor`, `SslMasterKeyHandler` `jvm-only-replaced`; `AsyncRunnable` library-not-ported | |
| 2, ssl-context | `SslContext`, `SslContextBuilder`, `JdkSslContext`, `DelegatingSslContext`, `SslProvider`, `SniHandler`, `OptionalSslHandler`; ssl.util `InsecureTrustManagerFactory`, `FingerprintTrustManagerFactory`, `FingerprintTrustManagerFactoryBuilder` | 10 `clr-adapted`; 8 trust, resumption and JCA SPI units `jvm-only-replaced` | |
| 3, tests-handler | `SslHandlerTest`, `ParameterizedSslHandlerTest`, `CloseNotifyTest`, `ApplicationProtocolNegotiationHandlerTest`, `RenegotiateTest`, `JdkSslRenegotiateTest`, `CipherSuiteCanaryTest`, `PkiTestingTlsTest` | 8 test files; `DelayingExecutor` `jvm-only-replaced` | 77 declared: 46 mapped, 31 CLR-inapplicable; 48 cases, 44 pass, 4 skipped |
| 3, tests-engine | the `SSLEngineTest` fixture and its JDK subclass `JdkSslEngineTest` | 2 test files; `ConstantTrustManagerFactory`, `EmptyExtendedX509TrustManager` `jvm-only-replaced` | 72 declared: 20 mapped, 52 CLR-inapplicable; 103 cases, 85 pass, 18 skipped |
| 3, tests-context | SNI, `OptionalSslHandler`, `SslContext`/`SslContextBuilder`, JDK context, trust manager factory and PEM tests | 13 test files; 2 `jvm-only-replaced` | 100 declared: 73 mapped, 24 CLR-inapplicable, 3 not ported; 105 cases, 100 pass, 5 skipped |

### SslHandler on SslStream

`SslHandler` keeps Netty's structure (record parsing, the wrap/unwrap loops, the handshake and close futures, the
timeouts, `startTls`, the wrap data size). Only the `SSLEngine` calls are replaced:

| Java (SSLEngine) | C# | Evidence |
| --- | --- | --- |
| `SslHandler(SSLEngine[, boolean startTls])` | `SslHandler(SslClientAuthenticationOptions[, bool startTls])` and `SslHandler(SslServerAuthenticationOptions[, bool startTls])`; the handler owns the options and creates `new SslStream(mediation, false)` | ssl design decision 2; [SslStream](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream?view=net-10.0) ("transmits data using a stream that you supply") |
| `engine.wrap/unwrap` | private `EngineWrap`/`EngineUnwrap` returning an `EngineResult` (Status `BUFFER_UNDERFLOW`/`BUFFER_OVERFLOW`/`OK`/`CLOSED`, HandshakeStatus `NOT_HANDSHAKING`/`FINISHED`/`NEED_TASK`/`NEED_WRAP`/`NEED_UNWRAP`), so the Java `wrap`/`wrapNonAppData`/`wrapMultiple`/`unwrap` loops are unchanged | CLR comment block "the SSLEngine calls on SslStream" in `Ssl/SslHandler.cs` |
| transport bytes | private nested `MediationStream : System.IO.Stream, IValueTaskSource<int>`: `decode` pushes one whole record (cut by `SslUtils.getEncryptedPacketLength`) into SslStream's pending read, completed inline with `ManualResetValueTaskSourceCore` (no `RunContinuationsAsynchronously`); zero-byte reads are supported; SslStream's writes are collected and drained into the `out` buffer | [ManualResetValueTaskSourceCore source](https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Sources/ManualResetValueTaskSourceCore.cs), [RunContinuationsAsynchronously](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.sources.manualresetvaluetasksourcecore-1.runcontinuationsasynchronously?view=net-10.0) |
| a record shorter than its 5-byte header (an SSLv2-style length of 4) | `BUFFER_UNDERFLOW` with nothing consumed, as the JDK engine (`SSLEngineInputRecord.bytesInCompletePacket`: "if (packet.remaining() < 5) return -1"), so `decodeJdkCompatible` throws `NotSslRecordException`; pushed into SslStream, the bytes made it wait for the rest of its frame header and report nothing (measured, `SslHandlerTest.testIncorrectLength`) | added by the tests-handler wave |
| inline continuations | `RunEngine` clears a non-default `SynchronizationContext` and, inside a Task of another scheduler, runs the call with `Task.RunSynchronously(TaskScheduler.Default)`, because the TPL does not inline continuations there | [TaskContinuation.cs](https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskContinuation.cs) (`IsValidLocationForInlining`), [SetSynchronizationContext](https://learn.microsoft.com/en-us/dotnet/api/system.threading.synchronizationcontext.setsynchronizationcontext?view=net-10.0), [RunSynchronously](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.runsynchronously?view=net-10.0) |
| `beginHandshake()` | `AuthenticateAsClientAsync`/`AuthenticateAsServerAsync(options)` where Java begins the handshake, or implicitly on the first wrap/unwrap, as SSLEngine does | [AuthenticateAsClientAsync](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.authenticateasclientasync?view=net-10.0), [SSLEngine](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/javax/net/ssl/SSLEngine.html) |
| unwrap result data, `CLOSED` | one outstanding `SslStream.ReadAsync` after the handshake; decrypted bytes are copied into the decode output; a read of 0 is the peer's close_notify | [ReadAsync](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.readasync?view=net-10.0) |
| answer to a received close_notify | `EngineAcknowledgeCloseNotify`: when the read returns 0, the protocol is below TLS 1.3 and the outbound side is open, the engine calls `ShutdownAsync`; the unwrap result reports `NEED_WRAP` and `wrapNonAppData` drains the alert, as in Java. The JDK engine answers (`TransportContext.passiveInboundClose`: "For TLS 1.2 and prior version, it is required to respond with a close_notify alert of its own"; RFC 5246 7.2.1), SslStream writes nothing (measured, `CloseNotifyTest` TLSv1.2) | added by the tests-handler wave |
| wrap of application data | `SslStream.WriteAsync`, which completes synchronously; while a server renegotiation runs, data waits as during an SSLEngine handshake | [WriteAsync](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.writeasync?view=net-10.0) |
| `closeOutbound()` | `SslStream.ShutdownAsync()` (close_notify), only after a successful handshake | [ShutdownAsync](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.shutdownasync?view=net-10.0) |
| `closeInbound()`, `isInboundDone()` | a flag only; the debug log of `closeInbound()`'s SSLException has no counterpart | |
| delegated tasks (`NEED_TASK`, `SslTasksRunner`) | `NEED_TASK` means SslStream went on asynchronously (neither waiting for input nor holding output); `SslTasksRunner` resumes on the event loop when the mediation stream or an SslStream Task reports progress (`OnEngineProgress`) | |
| `SSLException`, `SSLHandshakeException` | `AuthenticationException`; any other exception of the handshake Task (a validation callback's `CryptographicException`) or of a read or write becomes its `InnerException`, as the JDK's `SSLHandshakeException.initCause` | [AuthenticationException](https://learn.microsoft.com/en-us/dotnet/api/system.security.authentication.authenticationexception?view=net-10.0) |
| `applicationProtocol()` | `NegotiatedApplicationProtocol` decoded as UTF-8 (the encoding of `SslApplicationProtocol(string)`), null when empty | [SslApplicationProtocol.cs](https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Net.Security/src/System/Net/Security/SslApplicationProtocol.cs) |
| `engine()` | not public (ssl design decision 2): internal `Engine()` returns the handler's `SslStream`, and internal `ClientOptions()`/`ServerOptions()` the options (lead decision 4); `SniHandler`/`OptionalSslHandler` dispose the SslStream of a handler that never joined a pipeline, which is safe before `handlerAdded` (measured) | |
| `renegotiate()` | server: `NegotiateClientCertificateAsync` after cancelling the outstanding read (it completes at once when the client certificate is already known); client: `NotSupportedException`, which fails the new handshake promise and closes, as a failing `beginHandshake()` does | [NegotiateClientCertificateAsync](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.negotiateclientcertificateasync?view=net-10.0), [SslStream.IO.cs](https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Net.Security/src/System/Net/Security/SslStream.IO.cs) |
| `Promise`, `ChannelPromise`, `Future` | `TaskCompletionSource`/`Task`; the handshake and close futures use `RunContinuationsAsynchronously`; the handler's own promises complete producers inline; cascades and listeners run on `IdleStateHandler.ExecutorTaskScheduler(ctx)`, so write Tasks complete on the event loop when the transport write completes. `closeOutbound()` and `closeOutbound(ChannelPromise)` fold into `Task CloseOutbound()`, and `renegotiate(Promise<Channel>)` is `Renegotiate(TaskCompletionSource<IChannel>)` | [Task promises and Listener timing](#shared-clr-adaptations) |
| `(long, TimeUnit)` | `TimeSpan` (`SetHandshakeTimeout`, `SetCloseNotifyFlushTimeout`, `SetCloseNotifyReadTimeout`); `toMillis` truncates the ticks | [common-clr-design.md](common-clr-design.md) |
| `volatile long` | `long` with `Volatile.Read/Write` | [Volatile](https://learn.microsoft.com/en-us/dotnet/api/system.threading.volatile?view=net-10.0) |
| `ignoreException` | an `IOException`, or a `SocketException` whose `SocketErrorCode` is `ConnectionReset`/`ConnectionAborted`, in place of the `StackTraceElement` and class-loader walk | [SocketError](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketerror?view=net-10.0) |
| `IGNORABLE_ERROR_MESSAGE` | `Regex` with ASCII `[xX]` classes and Java line terminators, wrapped in `\A(?:...)\z` | [Pattern](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/regex/Pattern.html), [.NET character classes](https://learn.microsoft.com/en-us/dotnet/standard/base-types/character-classes-in-regular-expressions) |
| packet buffer size | `5 + 2^14 + 2048` (the TLSCiphertext limit) | [RFC 5246](https://www.rfc-editor.org/rfc/rfc5246.txt) 6.2.3 |

### JSSE and JCA on the BCL

| Java (JSSE/JCA) | C# | Where / evidence |
| --- | --- | --- |
| `SSLException`, `SSLHandshakeException` | `AuthenticationException` for both (a handshake failure is not told apart by type). It derives from `SystemException`, not `IOException`, so Java `catch (IOException)` paths no longer see SSL errors. `SSLException(Throwable)` gets the message `cause.toString()` through `(string, Exception)`. | `NotSslRecordException`, `SslClosedEngineException`, `SslHandshakeTimeoutException`, `StacklessSSLHandshakeException`, `SslUtils.ToSSLHandshakeException`; [AuthenticationException](https://learn.microsoft.com/en-us/dotnet/api/system.security.authentication.authenticationexception?view=net-10.0), [SSLException](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/javax/net/ssl/SSLException.html) |
| `CertificateException`, `KeyException`, `GeneralSecurityException`, `InvalidKeySpecException` | `CryptographicException` (as pkitesting's `Algorithms`) | `PemReader`, `PseudoRandomFunction`, `SslContext`, `FingerprintTrustManagerFactory` |
| `SSLContext` (key and trust managers, `SecureRandom`, session context) | the trust and key material fields of `JdkSslContext`; the `SecureRandom` and the session cache size and timeout have no option | [handler-ssl-design.md](handler-ssl-design.md), Known differences |
| `SSLEngine` created per connection by `SslContext.newEngine` | fresh `SslClientAuthenticationOptions`/`SslServerAuthenticationOptions` built by `SslContext.NewHandler(alloc, startTls)` and `NewHandler(alloc, peerHost, peerPort, startTls)`, which `JdkSslContext` overrides | ssl design decision 2; [SslClientAuthenticationOptions](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslclientauthenticationoptions?view=net-10.0), [SslServerAuthenticationOptions](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslserverauthenticationoptions?view=net-10.0) |
| `TrustManagerFactory` | `X509ChainPolicy` (`CustomRootTrust`, `CustomTrustStore`, `RevocationMode.NoCheck`), the options' `CertificateChainPolicy`, cloned per handler | as pkitesting `X509Bundle.ToTrustManagerFactory`; [X509ChainPolicy](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509chainpolicy?view=net-10.0) |
| `TrustManager` (`X509ExtendedTrustManager`), `checkClientTrusted`/`checkServerTrusted` | `RemoteCertificateValidationCallback`, which gets SslStream's verdict as `SslPolicyErrors` (with `RemoteCertificateNameMismatch` cleared when endpoint identification is disabled); a rejection returns false. A plain trust check is `X509Chain { ChainPolicy = policy.Clone() }.Build(eec)` | [RemoteCertificateValidationCallback](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.remotecertificatevalidationcallback?view=net-10.0), [SslPolicyErrors](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslpolicyerrors?view=net-10.0), [X509Chain.Build](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509chain.build?view=net-10.0) |
| `KeyManagerFactory` | `SslStreamCertificateContext` (PKCS #12 round trip for a persisted key, `Create(target, chain, offline: true)`), created once per context | as `X509Bundle.ToKeyManagerFactory`; [Create](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstreamcertificatecontext.create?view=net-10.0); reuse "enables ... TLS session resumption on Linux servers" ([best practices](https://learn.microsoft.com/en-us/dotnet/core/extensions/sslstream-best-practices)) |
| `KeyManager` | `ServerCertificateSelectionCallback` (server), `LocalCertificateSelectionCallback` (client) | [server](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.servercertificateselectioncallback?view=net-10.0), [client](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.localcertificateselectioncallback?view=net-10.0) |
| `KeyStore` | the PKCS #12 `byte[]` of an `X509Certificate2Collection`; the key store type is not used | `SslContext.BuildKeyStore` |
| `java.security.cert.X509Certificate` (subclass with data) | an `X509Certificate2` subclass whose base is built from `X509CertificateLoader.LoadCertificate(bytes)` (PEM or DER) through the non-obsolete `X509Certificate2(X509Certificate)` constructor; the parsed certificate is a field disposed in `Dispose(bool)`. Parsing cannot be deferred (the BCL type holds the parsed handle), so invalid bytes fail in the constructor; the JCA getters are the inherited BCL members | `Ssl/Util/LazyX509Certificate`; [X509Certificate2 constructors](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificate2.-ctor?view=net-10.0) (the data-loading ones are obsolete, SYSLIB0057), [LoadCertificate](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificateloader.loadcertificate?view=net-10.0) |
| `javax.security.cert.X509Certificate` (deprecated-for-removal v1 type) | the BCL base class `X509Certificate`, built the same way | `Ssl/Util/LazyJavaxX509Certificate`; [javax X509Certificate](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/javax/security/cert/X509Certificate.html), [X509Certificate](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificate?view=net-10.0) |
| `X509Certificate` subclass that only holds unvalidated PEM (`PemX509Certificate`) | a reference-counted `IPemEncoded` holder (not an `X509Certificate2`); its 25 throwing `X509Certificate` overrides are not ported | `PemX509Certificate`; `ToPEM(alloc, useDirect, params object[] chain)` takes `IPemEncoded` or a BCL `X509Certificate` (encoded with `GetRawCertData()`) |
| `java.security.PrivateKey` | `AsymmetricAlgorithm` (pkitesting decision); where a value may also be a `PemPrivateKey` holder, the parameter is `object`: the `IPemEncoded` shortcut, else `ExportPkcs8PrivateKey()` (PKCS #8, what `getEncoded()` returns) | `PemPrivateKey.ToPEM(alloc, useDirect, object key)`; [AsymmetricAlgorithm](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.asymmetricalgorithm?view=net-10.0), [ExportPkcs8PrivateKey](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.asymmetricalgorithm.exportpkcs8privatekey?view=net-10.0) |
| `PKCS8EncodedKeySpec`, `EncryptedPrivateKeyInfo` decryption | PKCS #8 `byte[]`; decryption by `ImportEncryptedPkcs8PrivateKey` on RSA, DSA or ECDsa, then `ExportPkcs8PrivateKey` | [ImportEncryptedPkcs8PrivateKey](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.asymmetricalgorithm.importencryptedpkcs8privatekey?view=net-10.0), [ImportPkcs8PrivateKey](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.asymmetricalgorithm.importpkcs8privatekey?view=net-10.0) |
| Bouncy Castle first attempt of `toPrivateKey` | the BCL PEM importers (`ImportFromPem`/`ImportFromEncryptedPem` on RSA, DSA, ECDsa), null when none reads the key | `SslContext.ReadPemPrivateKey` (CLR-only helper) |
| `javax.security.auth.Destroyable` | plain methods `Destroy()`/`IsDestroyed()` (release and inspect the reference count) | `PemPrivateKey` |
| `java.security.SecureRandom` | a `RandomNumberGenerator` subclass (`GetBytes` overrides); `ThreadLocalRandom.current()` is `Random.Shared`; the `java.util.Random`-only members are not ported | `Ssl/Util/ThreadLocalInsecureRandom`; [RandomNumberGenerator](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.randomnumbergenerator?view=net-10.0), [Random.Shared](https://learn.microsoft.com/en-us/dotnet/api/system.random.shared?view=net-10.0), [common-clr-design.md](common-clr-design.md) |
| `javax.crypto.Mac` | `IncrementalHash.CreateHMAC(new HashAlgorithmName(upper-cased name after "Hmac"), key)`; unknown names (HmacSHA224) give "Could not find algo"; an empty key is still rejected, as by `SecretKeySpec` | `PseudoRandomFunction`; [CreateHMAC](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.incrementalhash.createhmac?view=net-10.0), [HashAlgorithmName](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.hashalgorithmname?view=net-10.0), [SecretKeySpec](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/javax/crypto/spec/SecretKeySpec.html) |
| `MessageDigest` in a `FastThreadLocal` | the stateless `CryptographicOperations.HashData(HashAlgorithmName, byte[])`; JCA names become BCL names (`SHA-256` is `SHA256`, `SHA3-256` unchanged) | `FingerprintTrustManagerFactory`; [HashData](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptographicoperations.hashdata?view=net-10.0) |
| `java.security.Provider` parameters, `SSLContext` factories | not ported (no BCL counterpart); the `provider == null` branch is kept | `SslUtils.IsTLSv13SupportedByJDK()`/`IsTLSv13EnabledByJDK()` have no parameter; `newInitContext`, `getSSLContext`, `getTlsVersion` are not ported |
| JDK defaults `isTLSv13SupportedByJDK`, `isTLSv13EnabledByJDK` | an in-memory SslStream client/server handshake with `PROBING_CERT`/`PROBING_KEY` (persisted through a PKCS #12 round trip), `EnabledSslProtocols = Tls13` (supported) and `None` (the OS default, enabled), reading `SslStream.SslProtocol`; run once by the static constructor, the client on its own thread, every wait bounded by the constant `PROBE_TIMEOUT_MILLIS` (a static field deadlocked on the type initializer, measured: 60 s and both false) | `SslUtils`; [SslProtocols](https://learn.microsoft.com/en-us/dotnet/api/system.security.authentication.sslprotocols?view=net-10.0) ("None: Allows the operating system to choose the best protocol"), [SslStream.SslProtocol](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.sslprotocol?view=net-10.0) |
| `setEnabledProtocols` | `EnabledSslProtocols` (names to values; none configured is `SslProtocols.None`, the recommended OS choice) | [best practices](https://learn.microsoft.com/en-us/dotnet/core/extensions/sslstream-best-practices) |
| `setEnabledCipherSuites` | the `CipherSuitesPolicy` of the `TlsCipherSuite` values (IANA names, which are the JSSE names), only for configured suites; names are looked up in a name map (`Enum.TryParse` also accepts numbers); an unknown name is `ArgumentException("Unsupported CipherSuite: X")`; on Windows and Android `NewHandler` throws `PlatformNotSupportedException` | [CipherSuitesPolicy ctor](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.ciphersuitespolicy.-ctor?view=net-10.0), [TlsCipherSuite](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.tlsciphersuite?view=net-10.0) |
| `setWantClientAuth`, `setNeedClientAuth`; `getWantClientAuth`/`getNeedClientAuth` | `ClientCertificateRequired = true`; `REQUIRE` keeps SslStream's rejection of a missing certificate (`RemoteCertificateNotAvailable`, measured), `OPTIONAL` accepts it in the validation callback; the tests read want/need from the options and the callback | `JdkSslContext.ConfigureAndWrapEngine`, `VerifyRemoteCertificate`; [ClientCertificateRequired](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslserverauthenticationoptions.clientcertificaterequired?view=net-10.0) |
| `SSLParameters.setEndpointIdentificationAlgorithm` | SslStream's host name check against `TargetHost`; a null algorithm clears `RemoteCertificateNameMismatch`; `"HTTPS"` and `"LDAPS"` use the same check | `JdkSslContext.VerifyRemoteCertificate` |
| `createSSLEngine(peerHost, peerPort)`, `setServerNames` | `TargetHost` is the first SNI server name, else the peer host (SNI and the certificate name check use the same name); the port has no option | the server's `ServerCertificateSelectionCallback` receives it (measured) |
| `SNIMatcher` in the server `SSLParameters` | no counterpart: `SslServerAuthenticationOptions` has no SNI matcher | `SniClientTest` (2 tests CLR-inapplicable) |
| JDK ALPN negotiators | a private `IApplicationProtocolNegotiator` holding the list, and the options' `ApplicationProtocols`; `FATAL_ALERT` versus `NO_ADVERTISE`/`ACCEPT` has no SslStream option | `JdkSslContext.ToNegotiator` |
| `ReferenceCountUtil.safeRelease(sslHandler.engine())` | `sslHandler.Engine().Dispose()` | `SniHandler.ReplaceHandler`, `OptionalSslHandler` |
| `SSLSession`/`ExtendedSSLSession` facts | members of the handler's `SslStream`: `SslProtocol` (mapped to `"TLSv1.2"`/`"TLSv1.3"`), `NegotiatedCipherSuite`, `LocalCertificate`/`RemoteCertificate` (`Subject` is the principal name, `GetRawCertData()` is `getEncoded()`), `NegotiatedApplicationProtocol`, `TargetHostName` (client: the SNI it sent, server: the SNI it received), `IsAuthenticated` (`getHandshakeSession() == null`); the session id, times, `isValid()`, `getPeerCertificateChain()` and the signature algorithm lists have no member | the tests; [SslStream](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream?view=net-10.0), [SSLSession](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/javax/net/ssl/SSLSession.html), [TargetHostName](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.targethostname?view=net-10.0) |

### Other adaptations in ssl

- **Regular expressions.** `Pattern`/`Matcher` are `Regex`: `matches()` wraps the pattern in `\A(?:...)\z`, Java's `.`
  is written `[^\n\r\u0085  ]`, `group(n)` is null when the group does not participate, `find(start)` is
  `Match(input, start)`, and ASCII `CASE_INSENSITIVE` is written with explicit classes (`CipherSuiteConverter`,
  `SignatureAlgorithmConverter`, `PemReader`, `SslHandler`).
- **NIO buffers.** `ByteBuffer[]` (`getEncryptedPacketLength(ByteBuffer[], int)`) is `ReadOnlyMemory<byte>[]`
  (position to limit), read big-endian with `BinaryPrimitives`; `ByteBuf.ReadableMemory` is `nioBuffer()`.
- **Listeners and timeouts.** `Future<T>` with `addListener` in `SslClientHelloHandler` and `AbstractSniHandler` is
  `Task<T>` with a continuation on `IdleStateHandler.ExecutorTaskScheduler(ctx)`; the `cause()` of a cancelled Task is
  a `TaskCanceledException`. The `ScheduledFuture` of the handshake timeout is a `CancellationTokenSource` passed to
  `ScheduleAsync`, as in `IdleStateHandler`.
- **SNI host name.** `ByteBuf.toString(i, n, US_ASCII)`, whose fast path decodes Latin-1, is `Encoding.Latin1`, so
  octets above 127 decode as Java decodes them (`AbstractSniHandler.ExtractSniHostname`).
- **Mappings.** `Mapping`/`AsyncMapping` are `Func<string, SslContext>` and `IAsyncMapping<string, SslContext>`
  ([common-native-async-mapping.md](common-native-async-mapping.md)); `DomainNameMapping<? extends SslContext>` is
  `DomainNameMapping<SslContext>`. `SniHandler` calls `MapAsync(hostname)` without a cancellation token, as the pinned
  handler does not cancel its mapping.
- **Stackless exceptions.** The `fillInStackTrace()` override is `ThrowableUtil.UnknownStackTrace` in `NewInstance`, as
  Transport's `StacklessClosedChannelException`.
- **Option pool.** The generic static pool of `SslContextOption<T>` is a non-generic `SslContextOption` (pool,
  `ValueOf<T>`, `Exists`) plus `SslContextOption<T>`, as Common's `AttributeKey`; a name requested again with another
  `T` throws `ArgumentException`, where Java returns the same object.
- **Names.** `ApplicationProtocolConfig.protocol()`, `selectorFailureBehavior()` and `selectedListenerFailureBehavior()`
  are `Protocol_()`, `SelectorFailureBehavior_()` and `SelectedListenerFailureBehavior_()` (CS0102, see the owner
  decisions). `SslProtocols` (Netty's protocol names) shares its simple name with
  `System.Security.Authentication.SslProtocols`, which `JdkSslContext` and `SslUtils` import as `AuthSslProtocols`.
  `SslProvider`, a Java enum with static methods, is a sealed class with static readonly instances.
- **Cipher-suite constants.** `SslUtils.DEFAULT_CIPHER_SUITES`, `DEFAULT_TLSV13_CIPHER_SUITES`, `TLSV13_CIPHER_SUITES`
  and `TLSV13_CIPHERS` keep the IANA names in insertion order (`TLSV13_CIPHERS` is a `ReadOnlySet`).

### Context layer

- **SslContext.** The protected `newHandler(alloc, startTls)` and `newHandler(alloc, peerHost, peerPort, startTls)`
  are `protected internal abstract NewHandler(...)` (Java builds them on the abstract `newEngine`, which is not
  ported); `JdkSslContext` overrides them with a fresh options object per handler. `newServerContextInternal`/
  `newClientContextInternal` build a `JdkSslContext` through the internal `NewJdkSslServerContext`/
  `NewJdkSslClientContext`, which do what the constructors of the deprecated `JdkSslServerContext`/`JdkSslClientContext`
  did; `OPENSSL`/`OPENSSL_REFCNT` throw `NotSupportedException`. `ToPrivateKey` reads PEM with the BCL importers and
  decrypts PKCS #8 with `ImportEncryptedPkcs8PrivateKey`; it is `protected internal` (the 3-argument overload
  `internal`) for the tests. `BuildTrustManagerFactory` returns an `X509ChainPolicy`, `BuildKeyManagerFactory` an
  `SslStreamCertificateContext` that keeps every non-target PKCS #12 entry in its additional certificates.
- **SslContextBuilder.** The fluent API keeps the Java names with BCL parameter types: `TrustManager(FileInfo | Stream |
  X509Certificate2... | X509ChainPolicy | RemoteCertificateValidationCallback)`, `KeyManager(...)` and `ForServer(...)`
  with files, streams, `AsymmetricAlgorithm` plus certificates, `SslStreamCertificateContext`,
  `ServerCertificateSelectionCallback`, and for a client `KeyManager(LocalCertificateSelectionCallback)`;
  `ServerName(string)` (`SNIHostName` is the only supported `SNIServerName`); `Ciphers` becomes a
  `CipherSuitesPolicy`. `SecureRandom`, `SessionCacheSize`, `SessionTimeout` and `KeyStoreType` are kept for the API but
  not applied (no SslStream option; the BCL writes PKCS #12).
- **JdkSslContext.** It holds the protocols, the cipher policy, the ALPN list, the chain policy, the trust and key
  callbacks, the certificate context and the client authentication mode, and `ConfigureAndWrapEngine` copies them into
  each options object. `VerifyRemoteCertificate` is the trust decision: no certificate (a client fails; a server accepts
  unless `REQUIRE`), endpoint identification, a configured trust manager callback, expired trust anchors (lead
  decision 2), and a server with `ClientAuth.NONE` (lead decision 3).
- **Trust helpers.** `InsecureTrustManagerFactory` is a static class whose `INSTANCE` is a
  `RemoteCertificateValidationCallback` that accepts every certificate. `FingerprintTrustManagerFactory` is built with
  `Builder(algorithm)`, converts implicitly to a `RemoteCertificateValidationCallback`, and gives the tests its
  callback through the internal `EngineGetTrustManagers()`; a mismatch throws `CryptographicException`.
- **SNI and OptionalSslHandler.** `SniHandler` takes a `Func<string, SslContext>`, a `DomainNameMapping<SslContext>` or
  an `IAsyncMapping<string, SslContext>`; `ReplaceHandler` and `OptionalSslHandler` dispose the SslStream of a handler
  they do not install. `DelegatingSslContext` keeps only `InitHandler(SslHandler)` (empty by default), because
  `initEngine(SSLEngine)` is JVM-only.

### Measurements and platform limits

Windows 11 10.0.26300, .NET 10.0.8; EmbeddedChannel pairs, loopback sockets and raw SslStream probes of the waves:

| Check | Result |
| --- | --- |
| TLS 1.2 and 1.3 handshake through `SslHandler` | `TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384` / `TLS_AES_256_GCM_SHA384`; completes inside `WriteInbound` without `RunPendingTasks`; validation callbacks, events and reads run on the calling thread, also under a non-default `SynchronizationContext` and inside a Task of a custom `TaskScheduler` |
| Data and throughput | 100 000 bytes in 7 records each way, write Tasks complete at once; 600-640 MB/s (TLS 1.3, 16 KiB writes, EmbeddedChannel) |
| ALPN | `h2` on both sides; `ApplicationProtocolNegotiationHandler` configures and removes itself; the server's first protocol wins. Without a common protocol both sides fail with `AuthenticationException` (Win32 0x80090367 `SEC_E_APPLICATION_PROTOCOL_MISMATCH`), where JSSE completes without ALPN for `NO_ADVERTISE`/`ACCEPT` |
| close_notify | `CloseOutbound()` writes 31 bytes (TLS 1.2) or 24 bytes (TLS 1.3) and the peer gets `SslCloseCompletionEvent.SUCCESS`; SslStream does not answer a received close_notify (0 bytes), so `SslHandler` answers it below TLS 1.3 |
| Handshake timeout, non-TLS input, removal before the handshake | `SslHandshakeTimeoutException` event and close; `DecoderException(NotSslRecordException)`; the handshake and close futures fail with `AuthenticationException` |
| Untrusted server certificate | the client fails with `AuthenticationException` ("...UntrustedRoot"); the server handshake completes first, because SslStream validates after its handshake loop (`CompleteHandshake` in `SslStream.IO.cs`), and the client's alert (83/32 bytes on TLS 1.2, 112 bytes on TLS 1.3) fails the server's first read ("The decryption operation failed") |
| Validation callback | returning false: `AuthenticationException` ("The remote certificate was rejected by the provided RemoteCertificateValidationCallback.") and a fatal alert (`CreateFatalHandshakeAlertToken`, [SslStream.Protocol.cs](https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Net.Security/src/System/Net/Security/SslStream.Protocol.cs)); throwing: SslStream rethrows the exception as is and sends no alert, and `SslHandler` reports it as the `InnerException` of an `AuthenticationException` |
| Server renegotiation | succeeds on TLS 1.2 and 1.3 and sets `RemoteCertificate`; a server write during it is delivered after it. Client data sent before the client saw the request: TLS 1.2 fails ("Received data during renegotiation."), TLS 1.3 loses the data. When SChannel resumed a session that already carries a client certificate, `NegotiateClientCertificateAsync` throws "Remote certificate is already available." and the handler completes at once |
| Client `Renegotiate()` | `NotSupportedException`; the client closes |
| Server without a certificate | SslStream throws `NotSupportedException` ("The server mode SSL must use a certificate with the associated private key") when authentication starts |
| Client certificates | `REQUIRE` without a callback rejects a client without certificate (`RemoteCertificateNotAvailable`); a client key manager with two unrelated certificates sends only the leaf |
| Expired trust anchors | the Windows chain engine reports `NotTimeValid` for `mutual_auth_ca.pem` (expired 2017) and `test2.crt` (2014) in a custom trust store, which JSSE accepts (lead decision 2) |
| First ClientHello record of 1, 4 or 5 bytes (raw SslStream server) | `AuthenticationException` (inner `Win32Exception` "The message received was unexpected or badly formatted") and a 7-byte alert; first records of 10 to 50 bytes or unfragmented are accepted. `SniHandler` itself aggregates 1-byte fragments, copying each byte once |
| Key files | every PKCS #8 test key (plain, PBES2 AES-128/3DES/AES-256, PKCS #12 PBE-SHA1-3DES, PBES1-MD5-DES with an empty password) loads; `ImportFromPem` reads the unencrypted PKCS #1 RSA key; the legacy `Proc-Type: 4,ENCRYPTED` PKCS #1 RSA/DSA keys ("ASN1 corrupted data") and the PKCS #1 DSA key ("Neither RSA, DSA nor EC worked") do not load, as they need Bouncy Castle in Java too; `ec_params_unsupported.pem` (explicit EC parameters) loads, where Java throws `CertificateException` |
| Wildcard bind | `IPAddress.Any` on the dual-mode Transport socket accepts IPv4 only, so a connect to `::1` fails; the JDK binds the IPv6 wildcard for `0.0.0.0` ([net_util_md.c](https://raw.githubusercontent.com/openjdk/jdk17u/master/src/java.base/unix/native/libnet/net_util_md.c)) |
| `SslUtils` TLS 1.3 probes | about 50 ms each; both true on this host |
| `SslContextBuilder.secureRandom(spy)` | the spy is never asked for bytes |

Platform limits, stated in CLR comments:

- **Renegotiation** only as a server, on FreeBSD, Linux and Windows (`NegotiateClientCertificateAsync` is
  `SupportedOSPlatform` there); application data the client sends before answering is rejected (TLS 1.2) or lost
  (TLS 1.3), and a client `renegotiate()` fails.
- **close_notify** is sent only after the handshake completed (`ShutdownAsync` needs an authenticated stream), and
  SslStream answers none; `SslHandler` answers below TLS 1.3, as the JDK engine.
- **Cipher suites** cannot be restricted on Windows and Android (`CipherSuitesPolicy`).
- **Session resumption.** SslStream has no session cache size, timeout or resumption facts; on Windows SChannel
  resumes sessions across connections of one process, so a later handshake can find a client certificate already
  present.
- **SChannel** rejects a first TLS record that carries only a few ClientHello bytes.
- **Key logging** (`SslMasterKeyHandler`'s purpose) works only where SslStream uses OpenSSL (Linux).
- **Linux and macOS were not measured.** All measurements and runs are on Windows (SChannel).

### Lead decisions (2026-11-24)

1. **Units that are not ported** (recorded in the manifest): the netty-tcnative, Bouncy Castle, Conscrypt and
   Amazon Corretto units are library-not-ported (a unit uses a library when it imports it or names one of its classes
   in a string literal for reflection; the checker was extended for this); the `@Deprecated` `SelfSignedCertificate`
   family (its four generators, `SelfSignedCertificateTest`, `CachedSelfSignedCertificate`,
   `KeytoolSelfSignedCertGeneratorTest`, `OpenJdkSelfSignedCertGeneratorTest`, the last reaching the family through the
   generator it tests; the checker now compares Java packages across the main and test source roots and accepts a test
   that names its `via` helper), `JdkSslClientContext`/`JdkSslServerContext` and the `Jdk*`/`OpenSsl*`
   `ApplicationProtocolNegotiator` families are deprecated-not-ported; `JdkSslEngine`, `JdkAlpnSslEngine`,
   `JdkAlpnSslUtils` and the test `MockAlternativeKeyProvider` are jvm-only-replaced, as are `native-image.properties`
   and the Bouncy Castle `reflect-config.json`. The 39 test resources are copied byte for byte to
   `test/Netty.NET.Handler.Tests/Ssl/Resources` and deployed next to the test assembly, where Common's
   `ResourcesUtil.GetFile` finds them. The lead ported `SilenceExceptionHandler`.
2. **JdkSslContext accepts expired trust anchors.** With a custom trust store, `JdkSslContext` always installs its
   validation callback, and it accepts a chain whose only errors are `NotTimeValid` on elements of that trust store.
   RFC 5280 6.1: "When the trust anchor is provided in the form of a self-signed certificate, this self-signed
   certificate is not included as part of the prospective certification path", and the validity check of 6.1.3 (a)(2)
   applies to certificate i for all i in [1..n] of that path ([RFC 5280](https://www.rfc-editor.org/rfc/rfc5280.txt)).
   The Windows chain engine reports `NotTimeValid` for the expired test resources `mutual_auth_ca.pem` (2017) and
   `test2.crt` (2014), which JSSE accepts; any other error, or an expired certificate that is not an anchor, still
   fails ([X509ChainElement.ChainElementStatus](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509chainelement.chainelementstatus?view=net-10.0)).
3. **A server without client authentication accepts any client certificate.** A server `JdkSslContext` with
   `ClientAuth.NONE` installs the callback too and accepts any client certificate or none: renegotiation on SslStream is
   `NegotiateClientCertificateAsync`, which always asks for a client certificate, and SslStream's default validation
   would reject a client without one (`RemoteCertificateNotAvailable`), while a JSSE server without client
   authentication checks none. `RenegotiateTest` keeps the source's server context.
4. **Internal accessors and engine emulation in SslHandler.** Besides the internal `Engine()`, the internal
   `ClientOptions()`/`ServerOptions()` give the tests the handler's options (in place of reflection on private
   fields). `SslHandler` answers a received close_notify below TLS 1.3 (`EngineAcknowledgeCloseNotify`, as the JDK
   engine; SslStream does not) and treats a record shorter than its 5-byte header as `BUFFER_UNDERFLOW` (as the JDK
   engine); both were added by the tests-handler wave (see [SslHandler on SslStream](#sslhandler-on-sslstream)).
5. **pkitesting key lifetime.** `X509Bundle.ToKeyManagerFactory` keeps the loaded PKCS #12 entries alive with the
   returned `SslStreamCertificateContext` (`ConditionalWeakTable`): a self-signed bundle stores the certificate twice,
   both loaded entries share one key container, and finalizing the unused entry deleted the key, so the server failed
   with `SEC_E_UNKNOWN_CREDENTIALS` (6 of 6 runs with a forced GC, about 1 in 70 runs without). See
   [pkitesting-porting.md](pkitesting-porting.md).
6. **Test project reference.** `test/Netty.NET.Handler.Tests` references `Netty.NET.PkiTesting`, whose
   `CertificateBuilder` replaces the deprecated `SelfSignedCertificate` in the tests.

## Byte values

`ByteBuf.getByte`/`readByte` return `System.Byte` (owner decision). No byte sign is observable in text or arithmetic
of the handler packages. The places that keep Java's signed byte or short literals do so through unchecked casts,
which give the octets and values Java writes and compares:

| Place | Java | C# |
| --- | --- | --- |
| `PcapHeaders.GLOBAL_HEADER` | `(byte) -95, -78, -61, -44`, ..., `-1, -1` | `unchecked((byte) -95)` and so on: the octets `0xA1 0xB2 0xC3 0xD4` ... `0xFF 0xFF` |
| `EthernetPacket` dummy destination MAC | `(byte) -1` | `unchecked((byte) -1)`, the octet `0xFF` |
| `IPPacket.MAX_TTL` | `(byte) 255` (-1 as a Java byte) | the C# byte 255; both write the octet `0xFF` through `writeByte(int)` |
| `SslUtils.DTLS_1_0`, `DTLS_1_2`, `DTLS_1_3` | `(short) 0xFEFF`, `0xFEFD`, `0xFEFC` (negative shorts) | `unchecked((short) 0xFEFF)` and so on, compared with the signed `GetShort` result as Java compares `getShort` |
| `SslUtils.getEncryptedPacketLength(ByteBuf, ...)` | `getUnsignedByte` for the content type and major version, the signed `getShort` for the version, `shortBE(...) & 0x7FFF` / `& 0x3FFF` | `GetByte` (unsigned), `GetShort` (signed), the same masks after sign extension |

The other byte uses keep the source code:

- `IpSubnetFilterTest.testIpFilterRuleHandler` compares the bytes 1..7, which have the same value as `System.Byte`.
- `PcapWriteHandlerTest`'s `readUnsignedInt()`/`readUnsignedShort()`/`readByte()` return `uint`/`ushort`/`System.Byte`,
  so the `long` expectations compare as `long`, and the signed literals (`(byte) 0xff`, the -1 MAC byte, the `int`
  literal `0xa1b2c3d4`) keep the bits Java compares.
- `SslUtils.unsignedByte(byte)` and the `ByteBuffer` path mask a signed `ByteBuffer.get` with `& 0xFF`; span elements
  are already unsigned, and `getShort(pos + 1) == 0x101` is `ReadInt16BigEndian`.
- `SslClientHelloHandler.decode` and `AbstractSniHandler.extractSniHostname` use `getUnsignedByte`, which is `GetByte`;
  host name octets above 127 decode as Latin-1 `(char) (b & 0xff)`, as Java's fast path.
- `SslUtilsTest.probeForSSLv2`'s `(byte) 0x82` is the `System.Byte` 0x82 (the same octet), and the zero-length records
  of `SslClientHelloHandlerTest` (`SSL_CONTENT_TYPE_HANDSHAKE`, 3) are the non-negative bytes 22 and 3.
- The address, flush, logging, stream, timeout, flow and traffic packages do no arithmetic on signed bytes.

## Not ported

### Deprecated members (nine packages)

Owner decision 2026-11-16 does not port a deprecated member whose `@deprecated` text names a replacement
([Deprecated](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Deprecated.html)); its Javadoc is
recorded as `deprecated-not-ported` in the manifest entry of its file. These are the five recorded comments of the
nine packages; the ssl ones follow under [SSL: deprecated units and members](#ssl-deprecated-units-and-members).

| Member | `@deprecated` text | C# replacement |
| --- | --- | --- |
| `RuleBasedIpFilter(IpFilterRule...)` | Use `RuleBasedIpFilter#RuleBasedIpFilter(boolean, IpFilterRule...)` | `RuleBasedIpFilter(bool acceptIfNotFound, params IIpFilterRule[] rules)`; the deprecated constructor called `this(true, rules)`. |
| `ChunkedInput.readChunk(ChannelHandlerContext)` | Use `#readChunk(ByteBufAllocator)`. | `IChunkedInput<B>.ReadChunk(IByteBufAllocator allocator)`. The `@Deprecated` overrides in `ChunkedFile`, `ChunkedNioFile`, `ChunkedNioStream`, `ChunkedStream` and the test classes are not ported with it. |
| `ChunkedWriteHandler(int maxPendingWrites)` | use `#ChunkedWriteHandler()` | `ChunkedWriteHandler()`; the deprecated constructor only checked that `maxPendingWrites` is positive. |
| `PcapWriteHandler(OutputStream)` | Use `Builder` instead. | `PcapWriteHandler.Builder_().Build(outputStream)` (`captureZeroByte` false and `writePcapGlobalHeader` true are the Builder defaults). |
| `PcapWriteHandler(OutputStream, boolean, boolean)` | Use `Builder` instead. | `PcapWriteHandler.Builder_().CaptureZeroByte(captureZeroByte).WritePcapGlobalHeader(writePcapGlobalHeader).Build(outputStream)`. |

Deprecated members without a `@deprecated` tag name no replacement and are ported with `[Obsolete]`
([ObsoleteAttribute](https://learn.microsoft.com/dotnet/api/system.obsoleteattribute)):

| Member | Disposition |
| --- | --- |
| `TrafficCounter.readTimeToWait(long, long, long)`, `writeTimeToWait(long, long, long)` | ported with `[Obsolete]` |
| `AbstractTrafficShapingHandler.submitWrite(ctx, msg, delay, promise)` | ported with `[Obsolete]`; it returns the Task and takes the `CancellationToken` |

`WriteTimeoutHandlerTest` calls `addLast(group, handler)`, the deprecated `addLast(EventExecutorGroup,
ChannelHandler...)` that the Transport does not port; the test calls its body `AddLast(group, null, handler)`.

### JVM-only units (nine packages and module metadata)

| Unit | Status | Reason |
| --- | --- | --- |
| nine `package-info.java` (address, flow, flush, ipfilter, logging, pcap, stream, timeout, traffic) | `jvm-only-replaced` | C# namespaces cannot carry documentation comments; the summaries are in the namespace table above (traffic's full text is in its manifest entry). |
| `module-info.yml` | `jvm-only-replaced` | JPMS module descriptor; `Netty.NET.Handler.csproj` references codec-base, and `InternalsVisibleTo` opens internals to the test assembly only. It also requires `io.netty.transport.unix.common`, tcnative, Bouncy Castle and Conscrypt, which only serve the library-not-ported units of `io.netty.handler.ssl` (`transport-native-unix-common` only passes a socket descriptor to the OpenSSL engine), so the project has no counterpart for them. |
| `META-INF/native-image/.../generated/handlers/reflect-config.json` | `jvm-only-replaced` | GraalVM reflection metadata of the handler types (the file also lists the ssl handlers); NativeAOT keeps handler metadata through the `DynamicallyAccessedMembers` annotations on `IChannelHandler`/`ChannelHandlerAdapter`. |
| `NativeImageHandlerMetadataTest` (1 test) | `jvm-only-replaced` | Generates and compares that GraalVM JSON. |
| `junit-platform.properties` (test resource) | `jvm-only-replaced` | Enables JUnit parallel execution with test classes running concurrently ([JUnit](https://docs.junit.org/5.10.0/user-guide/#writing-tests-parallel-execution)); xUnit v3 runs a collection per test class in parallel and the tests of one class sequentially by default ([xUnit](https://xunit.net/docs/running-tests-in-parallel)), which is the configured behaviour. Classes that must not run in parallel declare a collection with `DisableParallelization = true`. |

### SSL: library-not-ported units

Owner decision 2026-11-24 (ssl design decision 3, the rule set for codec-compression on 2026-11-23): a unit that needs
a third-party Java library without a BCL counterpart is not ported, and its tests follow it. A unit counts as using a
library when it imports one of the library's packages or names one of its classes in a string literal for reflection.
The manifest records the library, the reason, the family owner and every original comment and test.

| Library | Main files | Test files (declared tests) | Units |
| --- | ---: | ---: | --- |
| netty-tcnative (OpenSSL/BoringSSL over JNI) | 43 | 23 (147) | `OpenSsl`, the `OpenSsl*`/`ReferenceCountedOpenSsl*` contexts, engines and sessions, the session cache, key-material, credential, private-key-method and certificate-compression types, `JdkDelegatingPrivateKeyMethod` and `AsyncRunnable` (the async delegated tasks of the OpenSSL engine); the `OpenSsl*` tests, the OpenSSL/JDK and OpenSSL/Conscrypt interop fixtures (they call `OpenSsl` directly), `JdkDelegatingPrivateKeyMethodTest` and `SslErrorTest` |
| Bouncy Castle | 4 | 2 (3) | `BouncyCastlePemReader`, `BouncyCastleAlpnSslEngine`, `BouncyCastleAlpnSslUtils`, `util/BouncyCastleUtil`; `BouncyCastleEngineAlpnTest`, `util/BouncyCastleUtilTest` |
| Conscrypt | 2 | 3 (14) | `Conscrypt`, `ConscryptAlpnSslEngine`; `ConscryptSslEngineTest`, `ConscryptJdkSslEngineInteropTest`, `JdkConscryptSslEngineInteropTest` |
| Amazon Corretto Crypto Provider | 0 | 1 (2) | `AmazonCorrettoSslEngineTest` (the SSLEngine suite with that JCA provider) |
| **total** | 49 | 29 (166) | |

Members of ported units that need one of these libraries are recorded with their comments (6 dispositions):
`SslContextBuilder.addCredential/addCredentials` (`OpenSslCredential`), `CipherSuiteConverter.convertToCipherStrings`
(it validates with `OpenSsl.isCipherSuiteAvailable` and feeds only OpenSSL contexts), and the `TCNATIVE`/`CONSCRYPT`
constants of `SslHandler.SslEngineType`. `SslProvider` keeps `OPENSSL` and `OPENSSL_REFCNT`; they report as
unavailable, and building a context for them throws `NotSupportedException`.

### SSL: deprecated units and members

The owner rule of 2026-11-16 applies as above. Units (14 main, 4 test; their 4 declared tests are recorded):

| Units | `@deprecated` text | C# replacement |
| --- | --- | --- |
| `JdkSslClientContext`, `JdkSslServerContext` | Use `SslContextBuilder` to create `JdkSslContext` instances and only use `JdkSslContext` in your code. | `SslContextBuilder`; `SslContext` builds the context through the internal `JdkSslContext.NewJdkSslClientContext`/`NewJdkSslServerContext`. |
| `JdkApplicationProtocolNegotiator`, `JdkAlpnApplicationProtocolNegotiator`, `OpenSslApplicationProtocolNegotiator`, `OpenSslDefaultApplicationProtocolNegotiator`, `OpenSslNpnApplicationProtocolNegotiator`; `JdkBaseApplicationProtocolNegotiator` and `JdkDefaultApplicationProtocolNegotiator`, which implement the first | use `ApplicationProtocolConfig` | `ApplicationProtocolConfig`, mapped to the `ApplicationProtocols` of the SslStream options. |
| `util/SelfSignedCertificate`; its generators `BouncyCastleSelfSignedCertGenerator`, `CertificateBuilderCertGenerator`, `KeytoolSelfSignedCertGenerator`, `OpenJdkSelfSignedCertGenerator`; the tests `SelfSignedCertificateTest`, `CachedSelfSignedCertificate`, `KeytoolSelfSignedCertGeneratorTest`, `OpenJdkSelfSignedCertGeneratorTest` (the last through the generator it tests) | Use the `CertificateBuilder` from `netty-pkitesting` instead. | `Netty.NET.PkiTesting.CertificateBuilder` ([pkitesting-porting.md](pkitesting-porting.md)); the tests that need a self-signed certificate build one with it. |

Members (39 recorded comments):

| Member | `@deprecated` text | C# replacement |
| --- | --- | --- |
| `SslContext.newServerContext` (10 overloads), `newClientContext` (13 overloads) | Replaced by `SslContextBuilder` | `SslContextBuilder.ForServer(...)`/`ForClient()` ... `Build()` |
| `SslContext.nextProtocols()` | Use `applicationProtocolNegotiator()` instead. | `ApplicationProtocolNegotiator().Protocols()` |
| `JdkSslContext(SSLContext, ...)` (two public constructors) | names a constructor that takes a JSSE `SSLContext`, which is not ported either | `SslContextBuilder` |
| `SslHandler.getCloseNotifyTimeoutMillis()`, `setCloseNotifyTimeout(long, TimeUnit)`, `setCloseNotifyTimeoutMillis(long)` | names the `...CloseNotifyFlushTimeout...` members | `GetCloseNotifyFlushTimeoutMillis()`, `SetCloseNotifyFlushTimeout(TimeSpan)`, `SetCloseNotifyFlushTimeoutMillis(long)` |
| `SslHandler.close()`, `close(ChannelPromise)` | no `@deprecated` tag; `@Deprecated` with Javadoc naming `closeOutbound()`/`closeOutbound(ChannelPromise)` | `CloseOutbound()` |
| `SslHandler.isEncrypted(ByteBuf)` | names `isEncrypted(ByteBuf, boolean)` | `SslHandler.IsEncrypted(ByteBuf, bool)` |
| `SslProtocols.SSL_v2_HELLO`, `SSL_v2`, `SSL_v3`, `TLS_v1`, `TLS_v1_1` | Consider using `TLS_v1_2` or `TLS_v1_3` | `SslProtocols.TLS_v1_2`/`TLS_v1_3`; code that must still name an old protocol uses the text (`"SSLv2Hello"`, `"SSLv2"`, `"SSLv3"`, `"TLSv1"`, `"TLSv1.1"`) |
| `FingerprintTrustManagerFactory(Iterable/String.../byte[]...)` (the SHA-1 constructors) | ... by calling `builder(String)` | `FingerprintTrustManagerFactory.Builder("SHA1" or a stronger algorithm)` |

`@Deprecated` ssl members without `@deprecated` text are ported with `[Obsolete]`: `SslContext.GenerateKeySpec`,
`SslContext.BuildTrustManagerFactory(FileInfo, X509ChainPolicy)` and `JdkSslContext.BuildKeyManagerFactory` (two
overloads, `[Obsolete("will be removed.")]`). `ApplicationProtocolNegotiator` has the `@deprecated` Javadoc tag but no
`@Deprecated` annotation, and `SslContext.applicationProtocolNegotiator()` returns it, so `IApplicationProtocolNegotiator`
is ported without `[Obsolete]`.

### SSL: JVM-only units and members

Owner decision 2026-11-16 (JVM-only API families are replaced without facades) and ssl design decision 2 (no SSLEngine
facade):

| Unit | Status | Reason / C# counterpart |
| --- | --- | --- |
| `ApplicationProtocolAccessor` | `jvm-only-replaced` | `SslHandler.ApplicationProtocol()` reads `SslStream.NegotiatedApplicationProtocol` (UTF-8, null when empty). |
| `JdkSslEngine`, `JdkAlpnSslEngine`, `JdkAlpnSslUtils` | `jvm-only-replaced` | SSLEngine wrappers and reflective JDK 9 ALPN access; SslStream negotiates ALPN from the options' `ApplicationProtocols`. |
| `SslMasterKeyHandler` | `jvm-only-replaced` | JDK-internal reflection or the OpenSSL engine; the runtime writes the key log itself with `SSLKEYLOGFILE` and the AppContext switch `System.Net.EnableSslKeyLogging`, only where SslStream uses OpenSSL (Linux). |
| `EnhancingX509ExtendedTrustManager` | `jvm-only-replaced` | Used only by the deprecated `JdkSslServerContext`; it rewrites JSSE host name messages, while SslStream reports `RemoteCertificateNameMismatch`. |
| `ResumableX509ExtendedTrustManager`, `ResumptionController` | `jvm-only-replaced` | SslStream reports no session resumption (only `AllowTlsResume` exists), so `SslHandler` has no resumption argument. |
| `util/SimpleTrustManagerFactory`, `TrustManagerFactoryWrapper`, `SimpleKeyManagerFactory`, `KeyManagerFactoryWrapper`, `X509KeyManagerWrapper` | `jvm-only-replaced` | JCA SPI plumbing; the builder keeps the BCL callbacks and objects directly. |
| ssl and ssl.util `package-info.java` | `jvm-only-replaced` | Summaries in the namespace table. |
| `native-image.properties` | `jvm-only-replaced` | `--initialize-at-run-time` for `ThreadLocalInsecureRandom`; the C# type initializes at first use, and NativeAOT has no build-time class initialization switch. |
| `META-INF/native-image/io.netty/netty-handler/reflect-config.json` | `jvm-only-replaced` | Reflection records for the Bouncy Castle provider constructors, which only the library-not-ported `util/BouncyCastleUtil` creates. |
| tests `ConstantTrustManagerFactory`, `EmptyExtendedX509TrustManager` | `jvm-only-replaced` | JSSE trust manager SPI helpers for the CLR-inapplicable `testSessionAfterHandshake*` and resumption tests; the counterpart is a `RemoteCertificateValidationCallback`. |
| test `DelayingExecutor` | `jvm-only-replaced` | An `Executor` for SSLEngine delegated tasks, which SslStream does not have. |
| test `MockAlternativeKeyProvider` | `jvm-only-replaced` | A JCA `Provider` for the library-not-ported `JdkDelegatingPrivateKeyMethodTest`. |
| tests `EnhancedX509ExtendedTrustManagerTest` (2 tests), `util/SimpleTrustManagerFactoryTest` (1 test) | `jvm-only-replaced` | Their units are jvm-only-replaced and no part has a ported subject. |

JVM-only members of ported units (77 recorded comments):

| Members | C# |
| --- | --- |
| `SslHandler(SSLEngine, Executor)`, `SslHandler(SSLEngine, boolean, Executor)`, `SslContext.newHandler(..., Executor)` | Not ported: SslStream has no delegated tasks. |
| `SslHandler.engine()`, `SslContext.newEngine(...)` (2), `sessionContext()`, `sessionCacheSize()`, `sessionTimeout()`; `JdkSslContext(SSLContext, ...)`, `context()`, `sessionContext()`; `DelegatingSslContext.initEngine(SSLEngine)` | The internal `SslHandler.Engine()` (the handler's `SslStream`), `NewHandler(...)` building the options, `InitHandler(SslHandler)`; SslStream has no session cache size or timeout. |
| `SslHandler`: the JDK `unwrap` Android workaround, `getUnwrapInputBuffer` (JDK-8357268), `singleBuffer`, the NIO buffer choice of `wrap(...)`, `runDelegatedTasks`, `SslTasksRunner.run()`'s task pull, the `closeInbound()` SSLException log, the `StackTraceElement` walk of `ignoreException`, `LazyChannelPromise` | `EngineWrap`/`EngineUnwrap` over SslStream, `SocketErrorCode` checks, a `TaskCompletionSource<IChannel>` (see [SslHandler on SslStream](#sslhandler-on-sslstream)). |
| `SslContext.getPBEAlgorithm`, package-private `newServerContext(..., keyStore)`, `SslContextBuilder.sslContextProvider(Provider)`, the IBM J9 `SSL_` probe in `JdkSslContext.supportedCiphers` | `ImportEncryptedPkcs8PrivateKey` reads the algorithm itself; the BCL has no JCA provider. |

## Test notes

- **Lifecycle.** xUnit creates one test class instance per test, so `@BeforeEach`/`@AfterEach` are the constructor and
  `Dispose()`, and the static `@BeforeAll`/`@AfterAll` run from xUnit class fixtures
  ([shared context](https://xunit.net/docs/shared-context)): `ResolveAddressHandlerTest`, `FlowControlHandlerTest`,
  `TrafficShapingHandlerTest` and `FileRegionThrottleTest`, whose fixture deletes the temporary file in place of
  `deleteOnExit()`. The test methods pass `TestContext.Current.CancellationToken` to the channel operations
  (xUnit1051).
- **Logging capture.** `LoggingHandlerTest` uses logback appenders in Java. `setup()` installs a mocked
  `IInternalLoggerFactory` that returns a mocked `IInternalLogger` (the "appender") for `LoggingHandler`'s logger name
  and the previous factory's loggers for other names; `teardown()` restores the previous factory, as Common's
  `InternalLoggerFactoryTest` does.
  - One logging event is one `Log(level, msg)` call (`Log(level, msg, cause)` for `exceptionCaught`), and
    `verify(appender).doAppend(argThat(matcher))` is `Verify(..., Times.Once())` with a Moq `Match.Create` matcher
    ([Moq quickstart](https://github.com/devlooped/moq/wiki/Quickstart)).
  - The mock reports DEBUG and above as enabled, logback's default root level
    ([logback configuration](https://logback.qos.ch/manual/configuration.html)).
  - `@Isolated` is a collection with `DisableParallelization`
    ([xUnit parallelism](https://xunit.net/docs/running-tests-in-parallel)), because the factory is process-wide.
  - The connect and bind expectations use `0.0.0.0:80` where Java expects `0.0.0.0/0.0.0.0:80`
    (`shouldLogChannelConnect`, `shouldLogChannelConnectWithLocalAddress`, `shouldLogChannelBind`).
- **Embedded channels whose remote address is captured.** The anonymous `EmbeddedChannel` subclasses of
  `IpSubnetFilterTest` read the captured address in `remoteAddress0()` while the base constructor registers the channel;
  the C# nested classes store it from a primary-constructor parameter in a field initializer, which runs before the base
  constructor
  ([constructor execution](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/classes#15114-constructor-execution)).
- **Loopback.** `Inet4Address/Inet6Address.getLoopbackAddress()` both call the static
  `InetAddress.getLoopbackAddress()`, which is 127.0.0.1 unless `java.net.preferIPv6Addresses` selects `::1`
  ([InetAddress](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/InetAddress.html#getLoopbackAddress()),
  [Inet6AddressImpl](https://raw.githubusercontent.com/openjdk/jdk17u/master/src/java.base/share/classes/java/net/Inet6AddressImpl.java)),
  so both `noClassCastException*` tests use `IPAddress.Loopback`.
- **IpSubnetFilterTest.testUniqueIpFilterHandler.** Because `UniqueIpFilter` forgets the address after the close (see
  [IP filter](#ip-filter)), the channel for the same address is created again until it is accepted, bounded by 10
  seconds, before the source's `assertTrue`; a rejected channel changes no filter state.
- **UniqueIpFilterTest.** `CyclicBarrier(2)` is `Barrier(2)`, which advances phases by itself, so `reset()` has no
  counterpart ([Barrier](https://learn.microsoft.com/en-us/dotnet/api/system.threading.barrier?view=net-10.0),
  [CyclicBarrier](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/concurrent/CyclicBarrier.html)).
  `Executors.newFixedThreadPool(2)` is the concurrent scheduler of a `ConcurrentExclusiveSchedulerPair` with concurrency
  2 on the thread pool
  ([ConcurrentExclusiveSchedulerPair](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.concurrentexclusiveschedulerpair.-ctor?view=net-10.0)).
  All 10000 rounds run (about 0.2 s); each round uses a new filter, so the close timing does not affect the outcome.
- **ResolveAddressHandlerTest.** `future.channel()` has no counterpart for a failed `Task<IChannel>`, and Bootstrap
  closes the channel of a failed connect itself, so the test closes only a connected channel.
- **Pcap tests.** The NIO tests (`udpV4*`, `tcpV4*`) bind loopback NIO channels and wait for
  `ShutdownGracefullyAsync`, about 2 s each (the default quiet period, as in Java); `writePcapGreaterThan4Gb` pushes
  2 x 4 GiB through the handler in about 15 s. `CloseDetectingByteBufOutputStream` cannot extend `ByteBufOutputStream`,
  which is sealed in the Buffer port, so it forwards to one. `EmbeddedDatagramChannel` keeps the `EmbeddedChannel`
  config, because the Transport `DefaultDatagramChannelConfig` takes a `NioDatagramChannel`; the `ChannelPromise`
  overloads of the group methods fold into the eight Task-returning `IDatagramChannel` methods.
- **ChunkedWriteHandlerTest.** `ch.newPromise()`/`cancel(false)` are a `CancellationTokenSource`;
  `syncUninterruptibly()` is an await, and `awaitUninterruptibly()` an await with `SuppressThrowing |
  ContinueOnCapturedContext` (xUnit1030). `File.deleteOnExit()` is an `AppDomain.ProcessExit` handler
  ([ProcessExit](https://learn.microsoft.com/en-us/dotnet/api/system.appdomain.processexit?view=net-10.0),
  [File](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/File.html)), as in the Transport
  `SocketFileRegionSourceTest`. `testChunkedFileFailsWhenFileWasTruncated` is a `[Theory]` with the rows `nio = false`
  and `nio = true`.
- **Idle-state tests.** They drive the EmbeddedChannel frozen clock as the Java tests do (`FreezeTime`,
  `AdvanceTimeBy`, `RunPendingTasks`). `anyNotIdle`'s `delayInNanos / 2 + 1` nanoseconds round up to the next 100 ns
  tick, which keeps the action strictly after half the idle time. `IdleStateHandlerTest.ObservableChannel` keeps
  flushed messages in the `ChannelOutboundBuffer` by returning a pending Task from the per-message `DoWrite(object)`
  (the Embedded transport has no overridable `doWrite(ChannelOutboundBuffer)` loop); `DoClose()` completes it so the
  drain releases its reference.
- **Socket tests.** `FlowControlHandlerTest` and `FileRegionThrottleTest` bind the wildcard address as in Java. Java
  connects to the loopback address instead
  ([SocketChannelImpl.checkRemote](https://raw.githubusercontent.com/openjdk/jdk17u/master/src/java.base/share/classes/sun/nio/ch/SocketChannelImpl.java)),
  while a CLR connect to 0.0.0.0 fails on Windows ("The requested address is not valid in its context", observed), so
  the client helpers make the same replacement.
- **FlowControlHandlerTest.** `java.util.concurrent.Exchanger` has no BCL counterpart
  ([Exchanger](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/concurrent/Exchanger.html)), so
  the test has a private two-party Monitor-based exchanger. `CountDownLatch` is `CountdownEvent` with a helper that
  ignores a zero count, because `Signal` throws when the event is already set
  ([CountdownEvent.Signal](https://learn.microsoft.com/dotnet/api/system.threading.countdownevent.signal)).
  `testSwallowedReadComplete` keeps the Java real-time `Thread.sleep(delayMillis + 20)` against the unfrozen clock.
- **FileRegionThrottleTest.testGlobalWriteThrottle** is `@Disabled("This test is flaky, need more investigation")` in
  Java and `[Fact(Skip = ...)]` with the same text in C#. Its body is ported; enabled once locally, it passed in 4 s.
- **TrafficShapingHandlerTest.** `Executors.newSingleThreadScheduledExecutor()` is a `DefaultEventExecutor`, and the
  `ChannelPromise` of `writeOneOutbound(holder, promise)` is the Task that `WriteOneOutboundAsync` returns.

### SSL tests

**Shared adaptations.**

- **Certificates.** `CachedSelfSignedCertificate` and `SelfSignedCertificate` (deprecated, not ported) are a per-class
  cached pkitesting bundle, `new CertificateBuilder().Subject("CN=localhost").Rsa2048().SetIsCertificateAuthority(true)
  .BuildSelfSigned()`, the defaults of `SelfSignedCertificate()`: `certificate()`/`privateKey()` are
  `ToTempCertChainPem()`/`ToTempPrivateKeyPem()`, and `cert()`/`key()` are `GetCertificate()`/`GetKeyPair()`. The
  resource files are read with `ResourcesUtil.GetFile`. `KeyStore.load` with `KeyManagerFactory.init` is
  `SslContext.BuildKeyManagerFactory(pkcs12, null, password, null)`.
- **Engines.** `SSLContext.getDefault().createSSLEngine()` is an options object for `SslHandler(options)`; an engine
  without `setUseClientMode` is a server engine
  ([TransportContext.java](https://raw.githubusercontent.com/openjdk/jdk17u/master/src/java.base/share/classes/sun/security/ssl/TransportContext.java):
  "new SSLConfiguration(sslContext, false)"). Such a server engine gets the cached test certificate as
  `ServerCertificateContext`, because SslStream refuses a server without one (measured). `sslCtx.newEngine(alloc[, host,
  port])` wrapped in `new SslHandler(...)` is `sslCtx.NewHandler(alloc[, host, port], startTls: false)`. The engine's
  settings are read through the internal `ServerOptions()`/`ClientOptions()`: `getWantClientAuth()`/
  `getNeedClientAuth()` are false for a client, and for a server `ClientCertificateRequired` plus whether the
  validation callback, called as SslStream calls it (`null`, `RemoteCertificateNotAvailable`), accepts a missing
  certificate (want) or rejects it (need). Releasing an engine that never handshook is `handler.Engine().Dispose()`.
- **Trust managers.** A `TrustManager` that throws `CertificateException` is a `RemoteCertificateValidationCallback`
  that returns false, so SslStream sends the fatal alert the Java test expects; an exception thrown by the callback
  propagates without an alert ("No try/catch wraps this call",
  [SslStream.Protocol.cs](https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Net.Security/src/System/Net/Security/SslStream.Protocol.cs)).
- **Listener timing.** A connect listener that writes "before fireChannelActive is called" is a first handler that
  writes from `channelActive` before passing the event on (the Transport's connect Task continues asynchronously). A
  `ChannelFutureListener` whose order against later events matters (`testHandshakeFailBeforeWritePromise`) is a
  continuation on the internal `IdleStateHandler.ExecutorTaskScheduler(ctx)` (reached through `InternalsVisibleTo`); a
  thread-pool continuation raced with the close event (measured).
- **Sockets.** `new InetSocketAddress(0)` with a connect to `sc.localAddress()` is a wildcard `IPEndPoint` with a
  connect to the loopback address (see Socket tests above); `SSLEngineTest` binds `IPv6Any` where the OS supports IPv6,
  because `IPAddress.Any` on the dual-mode socket accepts IPv4 only.
- **EmbeddedChannel pairs** are pumped by moving `ReadOutbound` buffers into the peer's `WriteInbound`; the handshake
  needs no `RunPendingTasks`. `FinishAndReleaseAll()` after a session returns true (the close_notify is queued).
- **xUnit.** `@Timeout` is the `Timeout` of a `[Fact]`/`[Theory]` on a Task-returning test; `xUnit1069` is disabled with
  a reason where a helper waits with `TestContext.Current.CancellationToken`. Helpers that overload a test name
  (`testCloseNotify`, `testBufferMessagesUntilHandshakeComplete`, two in `CipherSuiteConverterTest`) take a `0` suffix
  (xUnit1024). `@Isolated` is a collection with `DisableParallelization`.
- **Concurrency helpers.** `CountDownLatch` is `CountdownEvent` with a `CountDown` that ignores a zero count,
  `LinkedBlockingQueue` is `BlockingCollection`, `ConcurrentLinkedQueue` is `ConcurrentQueue`, and a `Promise` is a
  `TaskCompletionSource` (`PromiseNotifier.cascade` is `TrySetFromTask`,
  [common-task-composition.md](common-task-composition.md)).
- **Others.** `SslProtocols.SSL_v3` (deprecated, not ported) is the text `"SSLv3"`. A protected `decode(...)` called
  through package access goes through a nested `OptionalSslHandler` subclass with an `InvokeDecode` forwarder.
  `Mockito.mock(DomainNameMapping.class)` is a Moq class mock whose default is a mock `SslContext`, because the mocked
  constructor runs and rejects a null default. A `SecureRandom` subclass counting `next*` is a `RandomNumberGenerator`
  subclass counting `GetBytes`/`GetNonZeroBytes`, and an anonymous `PrivateKey` whose `getEncoded()` is null is a nested
  `AsymmetricAlgorithm` whose `ExportPkcs8PrivateKey()` returns null.

**Engine-independent tests (11 files, all `verified`).** `SslUtilsTest.testPacketLength` takes its ClientHello from a
client `SslStream` writing into a capture stream; the little-endian `ByteBuf`/`ByteBuffer` views have no counterpart
(no mutable byte order, [buffer-porting.md](buffer-porting.md)), so both overloads are compared on the same record and
against the record header. `PseudoRandomFunctionTest`'s Bouncy Castle `Hex.decode` is `Convert.FromHexString` without
spaces. `LazyX509CertificateTest`/`LazyJavaxX509CertificateTest` compare the JCA getters through the BCL members and
cover the getters without a BCL member by comparing the whole DER. `SslHandlerCoalescingBufferQueueTest`'s enum with
fields is a sealed class with `[MemberData]`.

**SslHandler tests.** `ParameterizedSslHandlerTest` (the JDK/JDK row), `CloseNotifyTest` (TLSv1.2 and TLSv1.3),
`ApplicationProtocolNegotiationHandlerTest`, `RenegotiateTest` (run by `JdkSslRenegotiateTest`) and
`CipherSuiteCanaryTest` (only the `delegate = false` rows; skipped on Windows) are `verified`. CLR-inapplicable:

| Tests | Reason |
| --- | --- |
| `SslHandlerTest.testHandshake[MTLS]With{ExecutorThatExecuteDirectly,ImmediateExecutor,ImmediateEventExecutor,Executor}{JDK,OpenSsl}` (16), `testClientHandshakeTimeoutBecauseExecutorNotExecute`, `testServerHandshakeTimeoutBecauseExecutorNotExecute` | Their subject is the delegated-task executor of `SslHandler(SSLEngine, Executor)`/`newHandler(alloc, Executor)`; SslStream has no delegated tasks (the OpenSsl rows also need netty-tcnative). |
| `SslHandlerTest.testReleaseSslEngine`, `testSessionTicketsWithTLSv12`/`13`[`AndNoKey`] (4), `testHandshakeFailureCipherMissmatchTLSv12/13OpenSsl`, `testSslCompletionEventsTls12/13Openssl` | OpenSSL (netty-tcnative) is library-not-ported; SslStream has no session cache settings or resumption facts. |
| `SslHandlerTest.testOutboundClosedAfterChannelInactive` | Asserts `SSLEngine.isOutboundDone()` of a `newEngine(...)` engine; SslStream has no such member and `newEngine` is not ported. |
| `PkiTestingTlsTest.peerSupportedSignatureAlgorithmsContainEd25519`, `connectWithX25519MLKEM768`, `x25519MLKEM768Interoperability` | `@EnabledIf("isBoringSSLAvailable")`: BoringSSL netty-tcnative (`OpenSslContextOption.GROUPS`, `OpenSslParametersUtil`); the first also needs `ExtendedSSLSession`. `testTlsConnection` has no `groups` parameter, which only these tests pass. |

**The SSLEngineTest fixture.** `@TestInstance(PER_CLASS)` with `@BeforeEach`/`@AfterEach` is `IAsyncLifetime`, and
`@MethodSource` a static `[MemberData]` (`NewTestParams()` asks `SslProvider.IsTlsv13Supported(JDK)`): 3 buffer types
times TLSv1.2/TLSv1.3, without the `delegate = true` half. The server engine's `setNeedClientAuth(true)` is
`ClientAuth.REQUIRE` on the context, and the client engine's `setSSLParameters` with `"HTTPS"` is
`EndpointIdentificationAlgorithm("HTTPS")`; the negotiated suite is checked against the context's suites (SslStream
applies no Netty cipher order). A mapped C# test must be declared `public void|Task Name(`, so the base theories of the
two `@Disabled` overrides call virtual bodies (`...ClientAuth0`) that `JdkSslEngineTest` overrides with
`Assert.Skip`. `SSLEngineTest.NotApplicable.cs` holds no code: it keeps the original comments of the inapplicable tests
and their helpers in source order, because the checker requires every original test comment in the mapped files.
CLR-inapplicable: 50 of the 64 `SSLEngineTest` tests, whose subjects are SSLEngine `wrap`/`unwrap` and state, the
`SSLSession`/`SSLSessionContext` (ids, times, invalidation, cache size and timeout, binding events), trust and key
manager callbacks that receive a handshake session, `ResumableX509ExtendedTrustManager`, `SslMasterKeyHandler`, the
peer certificate chain (`testMutualAuthSameCertChain`: SslStream sends only the leaf) or engine protocol and cipher
lists; and 2 of `JdkSslEngineTest` (`testTlsExtensionNoCompatibleProtocolsClientHandshakeFailure` uses the deprecated
JDK negotiator types without a selector, `testEnablingAnAlreadyDisabledSslProtocol` the engine protocol list).

**Context tests.** `SniHandlerTest`, `OptionalSslHandlerTest`, `SslContextTrustManagerTest` (the trust check is
`X509Chain.Build` with the policy) and `util/FingerprintTrustManagerFactoryTest` are `verified`;
`FingerprintTrustManagerFactoryTest` calls the internal `EngineGetTrustManagers()[0]` callback with `chain[0]` and no
SslStream sender (the server-side check, so a message says "server" instead of "client"), and
`testValidSHA1Fingerprint` uses `Builder("SHA1")`. `SniClientJava8TestUtil` maps the X509ExtendedKeyManager wrapper to
a `ServerCertificateSelectionCallback` and the trust manager to a validation callback whose sender's `IsServer` tells
`checkClientTrusted` from `checkServerTrusted`. The abstract `SslContextTest` runs through `JdkSslClientContextTest`
and `JdkSslServerContextTest`, whose `newSslContext` uses `SslContextBuilder` (29 tests, 58 cases). CLR-inapplicable:

| Tests | Reason |
| --- | --- |
| `SslContextTest.testPkcs1Des3EncryptedRsa`, `testPkcs1AesEncryptedRsa`, `testPkcs1Des3EncryptedDsa`, `testPkcs1AesEncryptedDsa`, `testPkcs1UnencryptedDsa` | Key forms only Bouncy Castle reads (legacy `Proc-Type: 4,ENCRYPTED` PKCS #1, PKCS #1 DSA); the BCL readers fail (measured). Their no/empty/wrong-password failure cases are ported. |
| `SslContextBuilderTest`: 7 `*Openssl` tests, `testInvalidCipherOpenSSL`, `testUnsupportedPrivateKeyFailsFastForServer`/`Client`, `openSslCredentialApiIsNotAvailableWithJdkProvider` | `SslProvider.OPENSSL` and `OpenSslCredential` (netty-tcnative) are library-not-ported. |
| `PemEncodedTest.testPemEncodedOpenSsl`, `testPemEncodedOpenSslRef` | OPENSSL/OPENSSL_REFCNT contexts. |
| `SniClientTest.testSniSNIMatcherMatchesClient`, `testSniSNIMatcherDoesNotMatchClient` | Their subject is an `SNIMatcher` in the server `SSLParameters`; `SslServerAuthenticationOptions` has none. |
| `DelegatingSslContextTest.testInitEngineOnNewEngine` | `SslContext.newEngine` is not ported; covered through `newHandler`. |
| `JdkSslClientContextTest`/`JdkSslServerContextTest.testTrustManagerFactoryReturningNullDoesNotThrowNpe` | A TrustManagerFactorySpi returning a null trust manager array cannot be built from an `X509ChainPolicy`; the wrapping it guards is jvm-only-replaced. |
| `JdkSslServerContextTest.testWrappingOfTrustManager` | `checkIfWrappingTrustManagerIsSupported` of the deprecated `JdkSslServerContext`, for the jvm-only-replaced `EnhancingX509ExtendedTrustManager`. |

**Adapted assertions** (measured SslStream behaviour, CLR comments in the tests):

| Test | Java | C# | Why |
| --- | --- | --- | --- |
| `ApplicationProtocolNegotiationHandlerTest.testHandshakeSuccess*` | the close writes a close_notify | the close writes SslHandler's empty buffer | SslStream writes a close_notify only on an authenticated stream; the simulated handshake never completed |
| `SslHandlerTest.testIncompleteWriteDoesNotCompletePromisePrematurely` | `finishAndReleaseAll()` true | false | no alert from SslStream before the handshake completed |
| `SslHandlerTest.testHandshakeFailureOnlyFireExceptionOnce`; the `SSLEngineTest` tests whose client rejects the server certificate | the server handshake fails | the server handshake succeeds and the client's alert fails the server's first read | SslStream validates the server certificate after its handshake completed |
| `JdkSslEngineTest.testTlsExtensionNoCompatibleProtocolsNoHandshakeFailure` | the handshake completes without ALPN | both sides fail, asserted | `SEC_E_APPLICATION_PROTOCOL_MISMATCH` (measured on TLSv1.2 and TLSv1.3) |
| `SslContextTest.testSslContextWithUnencryptedPrivateKeyEmptyPass` | `SSLException` | `ArgumentException` with the `CryptographicException` inside | the deprecated constructors are replaced by `SslContextBuilder`, which (as in Java) reports an unreadable key file as `IllegalArgumentException` |
| `SslContextTest.testUnsupportedParams` | `CertificateException` | the certificate loads (asserted) | the BCL accepts explicit EC parameters |
| `SslContextTest.testSupportedCiphers` | runs under a JDK-engine assumption | always runs | the assumption has no counterpart |
| `SslContextBuilderTest.testServerContextWithSecureRandom`, `testClientContextWithSecureRandom` | `getCount() > 0` | `== 0` | no SslStream option takes a random source |
| `SslContextBuilderTest.testInvalidCipherJdk` | after `OpenSsl.ensureAvailability()` | without it; `ArgumentException` from `NewHandler` | an environment guard only |
| `CipherSuiteCanaryTest` | `delegate` true and false | false rows only | `newHandler(alloc, Executor)` is not ported |
| `SslHandlerTest.testHandshakeFailureCipherMissmatchTLSv12Jdk`/`13Jdk`, `CipherSuiteCanaryTest.testHandshake`, `SSLEngineTest.clientInitiatedRenegotiationWithFatalAlertDoesNotInfiniteLoopServer` | run | `Assert.Skip` on Windows | no `CipherSuitesPolicy` there |
| `SniHandlerTest.testTinyFragments` (4 rows), `testTinyFirstFragment` | run | `Assert.Skip` on Windows | SChannel rejects a first record with only a few ClientHello bytes |
| `PkiTestingTlsTest.connectWithEd25519` | JDK 15+ | skipped | the BCL has no Ed25519 |

The stability runs of the tests-handler wave (about 170 full runs) found two failures of
`PkiTestingTlsTest.connectWithClassicalAlgorithms(rsa2048)`, both caused by the pkitesting key lifetime defect that
lead decision 5 fixed.

## Owner review items

- **ResolveAddressHandler<T>.** The generic type parameter stands for `AddressResolverGroup<? extends SocketAddress>`
  (see [Address](#address)); it adds a type parameter to a non-generic Java type.
- **UniqueIpFilter close timing.** The address is forgotten in a `CloseFuture` continuation on a thread-pool thread
  shortly after the close, where Java forgets it during the close. `IpSubnetFilterTest.testUniqueIpFilterHandler`
  therefore retries the channel for the same address for up to 10 seconds before the source's assertion.
- **LoggingHandler address text.** Addresses print as `0.0.0.0:80` (`IPEndPoint.ToString()`), where Java prints
  `0.0.0.0/0.0.0.0:80`; three `LoggingHandlerTest` expectations use the C# form.
- **Listener policies.** `IdleStateHandler.ExecutorTaskScheduler` runs write listeners inline when the completing thread
  is the event loop, because the Transport write Tasks use `RunContinuationsAsynchronously` and a
  `TaskScheduler.Default` continuation made four idle-state tests fail. `ChunkedWriteHandler` checks the event loop
  inside a `TaskScheduler.Default` continuation instead, so its listeners for writes that complete later run as later
  event-loop tasks. Both stand in for `DefaultPromise.notifyListeners()` and could share one Common helper (see the
  shared-API requests below).
- **WriteTimeoutHandler schedules after the write.** The timeout is scheduled right after the write is handed on,
  where Java schedules it right before, because the promise is the Task that `ctx.WriteAsync` returns.
- **protected internal widening (CS0507).** `submitWrite`, `checkWaitReadTime`, `informReadOperation` and
  `doAccounting` of `AbstractTrafficShapingHandler` are `protected internal`, so a subclass in another assembly can
  override them, which Java allows only inside the package.
- **lock(this) in TrafficCounter.** The synchronized methods of `TrafficCounter` and `GlobalChannelTrafficCounter` lock
  `this` to keep the Java monitor shared with subclasses, against the C# guidance.
- **ChunkedWriteHandler progress.** `IChannelConfig.WriteProgress` is channel-wide, so an observer sees the progress of
  the queued write and, from `ChannelOutboundBuffer`, the progress of each chunk write; Java reports only the queued
  write's `ChannelProgressivePromise`.
- **IChunkedInput<out B>.** The covariant parameter with a class constraint stands for `ChunkedInput<?>`; value-type
  chunks are not possible.
- **Pcap IPv4-mapped addresses.** A caller-supplied IPv4-mapped `IPEndPoint` (`forceTcpChannel`/`forceUdpChannel`)
  stays IPv6, where Java converts it to `Inet4Address`; addresses from the Transport channels are already normalized.
- **Byte signs.** pcap keeps signed byte literals (`GLOBAL_HEADER`, the dummy MAC byte, `MAX_TTL`) and `SslUtils` the
  negative DTLS version shorts, through unchecked casts that write and compare the same values; no sign is observable
  (see [Byte values](#byte-values)).
- **Shared-API requests** (the port works around each without copying shared code):
  - A Common continuation helper with `DefaultPromise.notifyListeners()` semantics (at once on the event loop, otherwise
    through `execute`, logging a rejected submission), for `IdleStateHandler`, `WriteTimeoutHandler`,
    `ChunkedWriteHandler` and the other listener continuations. ssl adds users: `SslHandler`, `SslClientHelloHandler`
    and `AbstractSniHandler` attach their listeners with `IdleStateHandler.ExecutorTaskScheduler(ctx)`, and
    `SslHandlerTest` reaches that internal scheduler through `InternalsVisibleTo`.
  - A public `ChannelOutboundBuffer.IsClosed`, so the traffic handlers can test the closed buffer instead of
    `IsOpen`.
  - An overridable `EmbeddedChannel` `doWrite(ChannelOutboundBuffer)` hook, for
    `IdleStateHandlerTest.ObservableChannel`.
  - A public `TimeSpan`/nanoseconds conversion helper (saturating `toNanos`, rounding up to the tick), now private in
    `IdleStateHandler` and repeated in `WriteTimeoutHandler`.
  - An unsealed `ByteBufOutputStream`, for `CloseDetectingByteBufOutputStream`.
  - A `DefaultDatagramChannelConfig` that takes an `IDatagramChannel`, for `PcapWriteHandlerTest`'s
    `EmbeddedDatagramChannel`.
  - A public `NetUtil` any-local-address helper (Transport's `AbstractChannel.IsAnyLocalAddress` is internal), for
    `PcapWriteHandler`.
  - An `InetAddress.getHostAddress()` text helper (the JDK form of IPv6 text), now private in `IpSubnetFilterRule`.
  - The requests of the ssl test waves (an accessor for the handler's options in place of reflection, the
    `Netty.NET.PkiTesting` reference of the test project, and the pkitesting key lifetime defect) were resolved by lead
    decisions 4, 6 and 5 ([Lead decisions](#lead-decisions-2026-11-24)).

### SSL review items

- **Public API shape.**
  - **No public engine or session accessor.** `SslHandler.engine()` is the internal `Engine()` (the `SslStream`), and
    the options are reached through the internal `ClientOptions()`/`ServerOptions()`; applications read session facts
    only through the events and `ApplicationProtocol()`. A public read-only accessor for the `SslStream` or the options
    is open.
  - **`CloseOutbound()`.** `closeOutbound()` and `closeOutbound(ChannelPromise)` fold into one `Task CloseOutbound()`,
    as the Transport folds the `ChannelPromise` overloads; like `HandshakeFuture()`, `SslCloseFuture()` and
    `Renegotiate()`, it returns a Task without the `Async` suffix.
  - **`Renegotiate(TaskCompletionSource<IChannel>)`** takes the producer in place of `Promise<Channel>`.
  - **`TimeSpan` setters.** `SetHandshakeTimeout`, `SetCloseNotifyFlushTimeout` and `SetCloseNotifyReadTimeout` take a
    `TimeSpan` in place of `(long, TimeUnit)`; the `...Millis` members keep `long`.
  - **`SslContext.NewHandler(...)`** is `protected internal abstract`, where Java's protected `newHandler` overloads are
    built on the abstract `newEngine`.
  - **Trust helpers.** `InsecureTrustManagerFactory` is a static class whose `INSTANCE` is a
    `RemoteCertificateValidationCallback`; `FingerprintTrustManagerFactory` converts implicitly to one;
    `BuildTrustManagerFactory` returns an `X509ChainPolicy`. `SslContextBuilder.KeyManager(LocalCertificateSelectionCallback)`
    is the client key manager, `ServerName(string)` stands for `serverName(SNIServerName)` (host names only), and the
    internal `JdkSslContext.NewJdkSslServerContext`/`NewJdkSslClientContext` are named after the deprecated classes they
    replace. `DelegatingSslContext` keeps only `InitHandler(SslHandler)`.
- **Cipher suites on Windows.** A context with configured cipher suites throws `PlatformNotSupportedException` from
  `NewHandler` on Windows and Android, where ignoring the setting would let the handshake run with the OS suites.
- **Builder settings kept but not applied.** `SecureRandom`, `SessionCacheSize`, `SessionTimeout` and `KeyStoreType`
  have no SslStream counterpart; they are stored and ignored, so `testServer/ClientContextWithSecureRandom` assert a
  count of 0.
- **Deprecated and obsolete members.** The deprecated `SslProtocols` constants are not ported, so code names old
  protocols by text. `IApplicationProtocolNegotiator` carries no `[Obsolete]` (the type has the `@deprecated` tag but no
  annotation). `SslHandler.close()`/`close(ChannelPromise)` are recorded deprecated-not-ported from their Javadoc text
  ("Use closeOutbound()"), as they have `@Deprecated` but no `@deprecated` tag.
- **PEM holders and lazy certificates.** `PemX509Certificate` and `PemPrivateKey` are holders, not BCL certificate or
  key types, so `ToPEM` takes `object` and the 25 throwing `X509Certificate` overrides are not ported.
  `LazyX509Certificate`/`LazyJavaxX509Certificate` parse in the constructor, because the BCL base type holds the parsed
  handle, so they are not lazy.
- **SslUtils probes.** The static constructor runs two in-memory TLS handshakes (about 50 ms each here) to answer
  `IsTLSv13SupportedByJDK`/`IsTLSv13EnabledByJDK`, as Java's class initialization probes the JDK.
- **Lead src decisions 2 to 5**: `JdkSslContext` accepting expired trust anchors of a custom trust store; a server
  without client authentication accepting any client certificate (for renegotiation); the `SslHandler` emulation of
  the JDK engine (the close_notify answer below TLS 1.3 and `BUFFER_UNDERFLOW` for a record shorter than its header)
  and its internal accessors; the pkitesting key lifetime fix.
- **Trust manager rejection.** The tests map a throwing trust manager to a callback that returns false (an alert is
  sent); `FingerprintTrustManagerFactory` throws `CryptographicException`, which ends the handshake without an alert.
- **Behaviour-adapted test assertions.** The assertions listed under [SSL tests](#ssl-tests) follow measured SslStream
  behaviour (no close_notify before the handshake, validation after the handshake, ALPN mismatch failing both sides,
  explicit EC parameters, unused random source); `CipherSuiteCanaryTest` is `verified` although its only case is
  skipped on Windows, and `DelayingExecutor` is recorded jvm-only-replaced because nothing in the port takes such an
  executor.
- **Platform limits.** SChannel rejects a first ClientHello record of a few bytes (5 `SniHandlerTest` cases skipped);
  renegotiation works only as a server and loses or rejects client data sent before the client answers it.
- **Linux and macOS are not measured.** Every run and measurement is on Windows (SChannel); the OpenSSL-backed
  SslStream (cipher suites, key logging, renegotiation and fragment behaviour) is untested.
