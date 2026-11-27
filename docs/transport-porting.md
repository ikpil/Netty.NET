# Netty transport: porting notes

The inventory, original test mappings and comment accounting live in
[transport-porting-manifest.json](transport-porting-manifest.json). Check them with
`tools/Test-TransportPortingManifest.ps1` (pwsh, or Windows PowerShell 5.1 since 2026-11-26) against the pinned Netty
`64cc10f38ea5f5bd7eae48507817c66680d0afdc` checkout. The inventory is every pinned file under `transport/src`
(277 since 2026-11-19, when the two GraalVM `reflect-config.json` files and the test ServiceLoader file joined the
Java sources and `module-info.yml`).

## Status (audit of 2026-11-26)

Transport is ported. The current manifest maps 520 declared original methods and 49 inherited scenarios.
The managed UDP revision records three former GRO/scatter methods and the inherited original-destination scenario
as unavailable. Segmented packets use individual socket sends. Entries and numbers in earlier validation sections
describe the implementation at the date of each run.

Windows run of `Netty.NET.Transport.Tests` (2026-11-26, Debug, TMP/TEMP outside `%LOCALAPPDATA%\Temp`): 1180 cases,
1085 passed, 0 failed, 95 skipped (60 Linux-only options and paths, 16 SslStream renegotiation limits of
`SocketSslEchoTest`, 9 source-disabled methods with the source reason, 4 for the source assumption
`supportsChannelIteration()`, 6 other platform conditions of the source). Of three full runs, two had one intermittent
failure each: `ChannelInitializerTest.TestChannelInitializerEventExecutor` reached its 10 s timeout once and did not
recur in 40 reruns under a concurrent full-suite load; `ManualIoEventLoopSourceTest.TestRun` failed its ">= 200 ms
wait" assertion once because the test helper's `SemaphoreSlim.Wait` (millisecond resolution) ended before the
high-resolution deadline. The helper now waits for the remainder, as `Semaphore.tryAcquire(nanos)` does.

What stays open is recorded in `openWork` and `nextWork`:

| Item | State | Detail |
| --- | --- | --- |
| NativeAOT scope | partially verified | Windows x64: 22 of 22 scenarios pass as a native executable (`--require-native`), including two new network scenarios (`NativeAotNetworkScenarios`: a 256 KiB loopback TCP echo through `ServerBootstrap`/`Bootstrap`, and a loopback UDP datagram). Linux x64 passed the 20 earlier scenarios; the network scenarios still need a Linux run, and handlers and codecs are not exercised under NativeAOT. The existing Common/Buffer trim and AOT warnings stay unsuppressed. |
| Mixed-family multicast | platform-limited | The unchanged Java wrapper fails on the IPv4 send from an IPv6 bind; the CLR test checks the same route error. |
| IPv6 traffic class | not verified | Covered on Linux; not checked on Windows or macOS hosts. |
| Windows test host | host-limited | See below. |

The other areas (socket read admission, outbound buffer, TCP and UDP, bootstrap and groups, generic write-failure
fallback, pipeline executor lifecycle) are `verified-for-original-fixtures` or `verified`. Their earlier "remaining
review" text predated the fixture completion; the audit rewrote it from the current entries. The summary counts had
drifted too (for example `portedOriginalFixtures` 92, `testsuiteSources` 52, `testsuiteDeferredSources` 14), so the
checker now recomputes them and fails on a difference.

Checker changes of the audit:

- Under Windows PowerShell 5.1 a BCL replacement type that .NET Framework lacks (the non-generic
  `TaskCompletionSource`) is looked up in the documentation files of the newest .NET 10 reference pack
  (`packs/Microsoft.NETCore.App.Ref/10.*/ref/net10.0/*.xml`, `T:<full name>`), so pwsh 7 is no longer required.
- The summary is recomputed from the entries.

**Windows test host and AF_UNIX.** On this host (Windows 11 10.0.26300, .NET 10.0.11) an AF_UNIX stream connect
fails with `WSAEINVAL` (10022) when the socket file is under `%LOCALAPPDATA%\Temp`, and succeeds under the user
profile or the workspace; a plain `System.Net.Sockets.Socket` probe without Netty.NET reproduces it. A probe of
2026-11-27 (bind, listen and connect with `UnixDomainSocketEndPoint`) narrowed it down: the connect fails anywhere under
`%LOCALAPPDATA%` (`AppData\Local` itself, its `Temp` and a subdirectory of `Temp`, in the long and the 8.3 path form)
and succeeds in the user profile directory, so it is a property of this host, not of the port. The tests create
socket files with `Path.GetTempPath()`, as the source uses the temporary directory, so with the default TEMP the 20
AF_UNIX cases fail (the "known Unix-domain-socket failures" of earlier baselines). With `TMP` and `TEMP` set to a
directory outside it, the full suite has no AF_UNIX failure:

```powershell
$env:TMP = "$PWD\.udstmp"; $env:TEMP = $env:TMP; New-Item -ItemType Directory -Force $env:TMP; dotnet test test/Netty.NET.Transport.Tests
```

**Windows NativeAOT publishing.** `vcvarsall.bat` calls `vswhere.exe` through `PATH`; without the Visual Studio
Installer directory on `PATH`, the link step fails with "'vswhere.exe' is not recognized" in the linker command.
Git Bash also drops the `ProgramFiles(x86)` variable that the ILCompiler's `findvcvarsall.bat` needs, so publish from
PowerShell:

```powershell
$env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"; dotnet publish test/Netty.NET.Transport.NativeAot.Tests -c Release -r win-x64 -p:PublishAot=true
```

## Not-ported Transport sources

Owner decisions of 2026-11-16 add two statuses for Java units that are deliberately
not ported as code. They are neither mapped nor pending; nothing in them is waived
silently. Details are in the manifest's `notPortedPolicy` and in each entry's
`notPorted` object.

- `deprecated-not-ported`: a `@Deprecated` Java API whose javadoc names a
  replacement. The entry records the exact `@deprecated` tag texts, whether the type
  carries `@Deprecated`, the replacement Netty names taken from the tag and what the
  C# port offers instead. No `[Obsolete]` facade is added. A unit without its own
  annotation (for example `AbstractOioByteChannel`, `DefaultOioDatagramChannelConfig`,
  `NioTask`, the OIO `package-info` files and the two fixtures) names the deprecated
  public type it serves (`deprecation.family`).
- `jvm-only-replaced`: a JVM-only API family or JVM-only documentation/metadata,
  replaced without a facade. Promise/Future/listeners become `Task` and
  `TaskCompletionSource`, NIO `Selector`/`SelectorProvider`/`SocketOption` plumbing
  becomes `System.Net.Sockets.Socket` APIs, GraalVM native-image metadata becomes
  NativeAOT, and package-info prose and JPMS metadata become namespace/assembly records.
  Contracts with no CLR counterpart are listed in `replacement.noClrCounterpart`.

Every such entry needs a source contract, a reason, the CLR replacement, existing
repository evidence files and Microsoft Learn / Java SE / GraalVM URLs. For each
entry the checker recomputes from the pinned source:

- whether the top-level type carries `@Deprecated`, and every `@deprecated` tag text
  in order (JVM-only sources with deprecated members record them too);
- for a unit without the annotation, that its family type is an annotated
  `deprecated-not-ported` entry linked by code (or by package, for `package-info`);
- that each recorded replacement name occurs in the `@deprecated` text;
- the original comment inventory: either one whole-file reason with the SHA-256 of
  the ordered normalized comments, or a verbatim per-comment list (used for
  `package-info.java` and `module-info.yml`);
- for fixtures, every declared JUnit method in `methodReview.notPortedMethods` with its
  source contract, reason and CLR evidence. These methods are not counted as mapped,
  skipped or CLR-inapplicable.

The checker reports 27 `deprecated-not-ported` and 25 `jvm-only-replaced` entries.

| Java source | Disposition | Replacement in the C# port |
| --- | --- | --- |
| `ThreadPerChannelEventLoop`, `ThreadPerChannelEventLoopGroup` (+ disabled test) | deprecated; the tag names no replacement and only `OioEventLoopGroup` uses the group | `DefaultEventLoopGroup` multiplexing channels over CLR socket completions. There is no per-channel thread and no `maxChannels` limit |
| `channel/oio/*`, `channel/socket/oio/*` (14 sources, 2 package-info, `OioEventLoopTest`) | deprecated, "use NIO / EPOLL / KQUEUE transport" | `NioSocketChannel`, `NioServerSocketChannel`, `NioDatagramChannel`, `NioDomainSocketChannel` on `DefaultEventLoopGroup`. There is no blocking I/O, no applied `SO_TIMEOUT` and no channel-count limit |
| `NioEventLoop`, `NioTask` | deprecated, "SingleThreadIoEventLoop with NioIoHandler" (`NioTask` serves `NioEventLoop.register`) | `DefaultEventLoop` + `CompletionIoHandler`; `IIoEventLoop.RegisterIoAsync(IIoHandle)` returning `IoRegistration`. There is no `selectorProvider()` or `rebuildSelector()` |
| `NioEventLoopGroup` | deprecated, "MultiThreadIoEventLoopGroup with NioIoHandler#newFactory()" | `DefaultEventLoopGroup` (thread count, chooser factory, `Func<CompletionIoHandler>`). There are no selector or queue-factory arguments |
| `LocalEventLoopGroup` | deprecated, "MultiThreadIoEventLoopGroup with LocalIoHandler#newFactory()" | `DefaultEventLoopGroup` used by `LocalChannel`/`LocalServerChannel` |
| `ChannelPromiseAggregator`, `ChannelPromiseNotifier` | deprecated, "PromiseCombiner" / "PromiseNotifier" | `Task.WhenAll`; a shared `Task` or `TaskCompletionSource.TrySetFromTask` ([common-task-composition.md](common-task-composition.md)) |
| `EventLoopTaskQueueFactory` | deprecated, not used by `IoEventLoopGroup`/`IoEventLoop` | override `SingleThreadEventExecutor.NewTaskQueue(int)` |
| `ChannelFuture`, `ChannelFutureListener`, `ChannelPromise` | JVM-only | `Task` returned by every channel operation, `IChannel.CloseFuture`, `await`/`ContinueWith`, `TaskCompletionSource` producers. `channel()`, `isVoid()`, listener removal and the `CLOSE*`/`FIRE_EXCEPTION_ON_FAILURE` constants have no counterpart |
| `ChannelProgressivePromise`, `DefaultChannelProgressivePromise` | JVM-only | `IChannelConfig.WriteProgress` (`IProgress<ChannelWriteProgress>`) reported by `ChannelOutboundBuffer` |
| `VoidChannelPromise`, `group/VoidChannelGroupFuture` | JVM-only | discard the returned `Task`. There is no fire-exception-on-failure route and no void group write |
| `ChannelFlushPromiseNotifier` | JVM-only (4 deprecated aliases recorded) | per-write `TaskCompletionSource` completed by `ChannelOutboundBuffer`. There is no public checkpoint notifier |
| `group/ChannelGroupFutureListener` | JVM-only | await the aggregate `Task`, per-channel `IReadOnlyDictionary<IChannel, Task>`, `ChannelGroupException` |
| `SelectedSelectionKeySet`, `SelectedSelectionKeySetSelector` (+ 5-method test) | JVM-only | per-operation socket completions queued through `CompletionIoHandler`/`IoRegistration` |
| `SelectorProviderUtil`, `NioChannelOption` | JVM-only | `new Socket(AddressFamily, SocketType, ProtocolType)`; named options through `Socket.Get/SetSocketOption` and `Get/SetRawSocketOption`. There is no arbitrary-option wrapper |
| `nativeimage/ChannelHandlerMetadataUtil` | JVM-only | `DynamicallyAccessedMembers` on handler types and the NativeAOT scenario executable |
| `module-info.yml` | JVM-only | `Netty.NET.Transport.csproj` project references and `InternalsVisibleTo` |
| `META-INF/native-image/.../reflect-config.json` | JVM-only | `DynamicallyAccessedMembers(PublicParameterlessConstructor)` on `Channel<T>()`/`Channel(Type)` for the reflective channel constructors; `NioIoHandler` selects with `Socket.Select` (no `SelectorImpl` fields); `DefaultChannelId` reads `Environment.ProcessId` instead of the JMX bean |
| `META-INF/native-image/.../generated/handlers/reflect-config.json` | JVM-only | `DynamicallyAccessedMembers` on `IChannelHandler`/`ChannelHandlerAdapter`, as for codec-base |
| test `META-INF/services/io.netty.bootstrap.ChannelInitializerExtension` | JVM-only | explicit `ChannelInitializerExtensions.Register` of `SourceInitializerExtension` in the bootstrap tests instead of ServiceLoader discovery |

### Package documentation

C# documentation comments must precede a type or member; a namespace cannot carry
one ([XML documentation comments](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/xmldoc/),
[C# specification annex D](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/documentation-comments)).
The package summaries are therefore kept verbatim in the manifest's per-comment
dispositions, and this table maps each one to its namespace. The `pool` package
documentation was ported earlier as a source comment in `Channels/Pool/IChannelPool.cs`.

| Java package | C# namespace | Original package summary |
| --- | --- | --- |
| `io.netty.bootstrap` | `Netty.NET.Transport.Bootstrapping` | The helper classes with fluent API which enable an easy implementation of typical client side and server side channel initialization. |
| `io.netty.channel` | `Netty.NET.Transport.Channels` | The core channel API which is asynchronous and event-driven abstraction of various transports such as a NIO Channel. |
| `io.netty.channel.embedded` | `Netty.NET.Transport.Channels.Embedded` | A virtual Channel that helps wrapping a series of handlers to unit test the handlers or use them in non-I/O context. |
| `io.netty.channel.group` | `Netty.NET.Transport.Channels.Groups` | A channel registry which helps a user maintain the list of open Channels and perform bulk operations on them. |
| `io.netty.channel.internal` | `Netty.NET.Transport.Channels.Internal` | Internal utilities for channel implementations. |
| `io.netty.channel.local` | `Netty.NET.Transport.Channels.Local` | A virtual transport that enables the communication between the two parties in the same virtual machine. |
| `io.netty.channel.nio` | `Netty.NET.Transport.Channels.Nio` (`AbstractNioChannel`, `AbstractNioByteChannel`, `AbstractNioMessageChannel`); event-loop role in `Netty.NET.Transport.Channels`, socket channels in `Netty.NET.Transport.Channels.Sockets` | NIO-based channel API implementation - recommended for a large number of connections (>= 1000). |
| `io.netty.channel.socket.nio` | `Netty.NET.Transport.Channels.Sockets` | NIO-based socket channel API implementation - recommended for a large number of connections (>= 1000). |
| `io.netty.channel.socket` | `Netty.NET.Transport.Channels.Sockets` | Abstract TCP and UDP socket interfaces which extend the core channel API. |
| `io.netty.channel.oio`, `io.netty.channel.socket.oio` | not ported (deprecated) | Old blocking I/O based (socket) channel API implementation - recommended for a small number of connections (< 1000). |
| `io.netty.testsuite.transport` | `Netty.NET.Transport.Tests` | Common test suite classes. |
| `io.netty.testsuite.transport.socket` | `Netty.NET.Transport.Tests` | Test suite classes for socket |
| `io.netty.testsuite.util` | `Netty.NET.Transport.Tests` | Utility classes that help testing a network application. |
| `io.netty.testsuite.transport.sctp`, `io.netty.testsuite.transport.udt` | deferred with their packages (see Testsuite inventory) | Test suite classes for sctp transport; Test suite classes for UDT. |

The connection-count recommendations compare Java NIO with blocking OIO. The CLR
port has only the non-blocking transport, so they guide no CLR choice.

## Java type names and hierarchy (2026-11-16)

Owner decisions of 2026-11-16: channel classes take the Java names, the socket channels
follow the Java hierarchy, missing Java interfaces are added with the `I` prefix, and
`AbstractServerChannel`/`FailedChannel` are ported. Behavior is unchanged: the Transport
suite keeps its Windows baseline (1148 passed, 80 skipped, 35 known Unix-domain-socket
failures). The CLR `System.Net.Sockets.Socket` under the `Nio*` names is recorded in
`// CLR:` comments and in the manifest (`sourceIdentifierPolicy.ownerDecisions.typeNamesAndHierarchy`
and the clrDecision "Java type names and hierarchy for socket channels, pipeline and handler context").

### Type map

| Before | Java name, C# type | File |
| --- | --- | --- |
| `Sockets.TcpSocketChannel` | `NioSocketChannel` | `Channels/Sockets/NioSocketChannel.cs` |
| `Sockets.TcpServerSocketChannel` | `NioServerSocketChannel` | `Channels/Sockets/NioServerSocketChannel.cs` |
| `Sockets.DatagramChannel` (class) | `NioDatagramChannel` | `Channels/Sockets/NioDatagramChannel.cs` |
| `Sockets.DomainSocketChannel` | `NioDomainSocketChannel` | `Channels/Sockets/NioDomainSocketChannel.cs` |
| `Sockets.DomainServerSocketChannel` | `NioServerDomainSocketChannel` | `Channels/Sockets/NioServerDomainSocketChannel.cs` |
| `Sockets.SocketStreamChannel` | `Nio.AbstractNioByteChannel` | `Channels/Nio/AbstractNioByteChannel.cs` |
| `Sockets.SocketListenerChannel` | `Nio.AbstractNioMessageChannel` | `Channels/Nio/AbstractNioMessageChannel.cs` |
| (none) | `Nio.AbstractNioChannel` | `Channels/Nio/AbstractNioChannel.cs` |
| `ChannelHandlerContext` (public class) | `AbstractChannelHandlerContext` (internal abstract) + `DefaultChannelHandlerContext` (internal sealed) | `Channels/AbstractChannelHandlerContext.cs`, `Channels/DefaultChannelHandlerContext.cs` |
| `DefaultChannelPipeline` head/tail contexts holding `HeadHandler`/`TailHandler` | `DefaultChannelPipeline.HeadContext`/`TailContext`, their own handlers | `Channels/DefaultChannelPipeline.cs` |
| `CombinedChannelDuplexHandler.DelegatingContext` (subclass of the context class) | `DelegatingChannelHandlerContext` implementing `IChannelHandlerContext` | `Channels/CombinedChannelDuplexHandler.cs` |
| (none) | `AbstractServerChannel`, base of `LocalServerChannel` | `Channels/AbstractServerChannel.cs` |
| (none) | `Bootstrapping.FailedChannel` (internal sealed; deleted by the I/O core alignment below) | - |

Hierarchy: `AbstractChannel` <- `AbstractNioChannel` <- `AbstractNioByteChannel` <-
`NioSocketChannel` (`ISocketChannel`), `NioDomainSocketChannel` (`IDuplexChannel`);
`AbstractNioChannel` <- `AbstractNioMessageChannel` <- `NioServerSocketChannel`
(`IServerSocketChannel`), `NioServerDomainSocketChannel` (`IServerChannel`),
`NioDatagramChannel` (`IDatagramChannel`); `AbstractChannel` <- `AbstractServerChannel` <-
`LocalServerChannel`. `io.netty.channel.nio` now maps to the namespace
`Netty.NET.Transport.Channels.Nio` for the three abstract channel classes.

### Interfaces added

| Java | C# | Members |
| --- | --- | --- |
| `SocketChannel extends DuplexChannel` | `ISocketChannel : IDuplexChannel` | none of its own (see below) |
| `ServerSocketChannel extends ServerChannel` | `IServerSocketChannel : IServerChannel` | none of its own |
| `DatagramChannel extends Channel` | `IDatagramChannel : IChannel` | `IsConnected`, `JoinGroupAsync` x3, `LeaveGroupAsync` x3, `BlockAsync` x2 |
| `ChannelInboundInvoker` | `IChannelInboundInvoker` | the nine `FireXxx` members, returning `IChannelInboundInvoker` |
| `ChannelPipeline extends ChannelInboundInvoker, ChannelOutboundInvoker, Iterable<Entry<String, ChannelHandler>>` | `IChannelPipeline : IChannelInboundInvoker, IChannelOutboundInvoker, IEnumerable<KeyValuePair<string, IChannelHandler>>` | add/remove/replace/get/context/first/last/names/toMap/channel |
| `ChannelHandlerContext extends AttributeMap, ChannelInboundInvoker, ChannelOutboundInvoker` | `IChannelHandlerContext : IAttributeMap, IChannelInboundInvoker, IChannelOutboundInvoker` | `Channel`, `Executor`, `Name`, `Handler`, `IsRemoved`, `Pipeline`, `Alloc()`, `Attr`, `HasAttr` |

The Java comments of these interfaces moved from the classes to the interface
declarations; the class description of `ChannelPipeline` is ported for the first time.

### Decisions

- Covariant re-declarations (`parent()`, `config()`, `localAddress()`, `remoteAddress()`,
  the `fireXxx()`/`read()`/`flush()` narrowings of `ChannelPipeline` and
  `ChannelHandlerContext`) are inherited unchanged, the rule already used for the socket
  config interfaces: C# 9 covariant returns cover class overrides only
  ([proposal](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-9.0/covariant-returns.md)),
  so an interface could narrow them only by hiding. The `ChannelPromise` overloads fold
  into the Task-returning members; `newPromise()`-style defaults have no Task counterpart;
  the deprecated `addFirst/addLast(EventExecutorGroup, ChannelHandler...)` are not ported.
- Properties whose name equals the Java method (`Channel`, `Pipeline`, `Name`, `Handler`,
  `IsRemoved`, `First`, `FirstContext`, ...) stay properties (owner decision 2026-11-16).
- Java repeats the stream members in `NioSocketChannel` and `NioDomainSocketChannel` and
  the accept loop in both NIO server channels. Both CLR channels of each pair drive the
  same `Socket` operations, so those members live once in `AbstractNioByteChannel` and
  `AbstractNioMessageChannel`; the server-only bind/close/unsupported-operation overrides
  stay in each server class, as in Java. `AbstractNioChannel` owns the socket as
  `JavaChannel()`, the read-pending flag of the three native read loops and
  `clearReadPending`. `closeOnReadError` is the Java `AbstractNioMessageChannel` rule with
  the `NioDatagramChannel` `SocketException` override, which gives the former listener
  and UDP results.
- Handler methods take `IChannelHandlerContext` (668 type references in 107 files were
  retyped by a Roslyn pass) and `IChannel.Pipeline` returns `IChannelPipeline`.
  `AbstractChannelHandlerContext`/`DefaultChannelHandlerContext` are internal like the
  package-private Java classes. `ToString()` keeps Java's text `ChannelHandlerContext(...)`.
  `TailContext` keeps `channelRegistered`/`channelUnregistered` skippable so those no-op
  events are not re-dispatched to its executor, as before the split.
- `FailedChannel` is ported, but nothing instantiates it: Java passes it to
  `initAndRegister()`'s failed promise only so that `ChannelFuture.channel()` is not null
  when the factory throws, and the CLR `RegisterAsync`/`BindAsync`/`ConnectAsync` return a
  faulted `Task<IChannel>`, which has no channel accessor. `IsOpen`/`IsActive` became
  virtual on `AbstractChannel` (abstract in Java) for its overrides.
- Overridable Unsafe (owner decision 2026-11-25). The folded `Channel.Unsafe` operations of
  `AbstractChannel` (`CloseForcibly`, `BindTransportAsync`, `ConnectTransportAsync`,
  `DisconnectTransportAsync`, `Close`, `DeregisterTransportAsync`, `WriteTransportAsync`,
  `FlushTransport`, `BeginRead`) are `protected internal virtual`, and `IsWritable`,
  `BytesBeforeUnwritable` and `BytesBeforeWritable` are `virtual`. A Java subclass in another
  package overrides them by returning its own `AbstractUnsafe` from the protected `newUnsafe()`
  (`AbstractChannel.java`); codec-http2's `AbstractHttp2StreamChannel` overrides them with the code
  of Java's `Http2ChannelUnsafe`, and computes its writability without a `ChannelOutboundBuffer`.
  "If you define the derived class in a different assembly from the base class, overridden members
  have protected access"
  ([protected internal](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/protected-internal)),
  so overrides outside the Transport are `protected override`; Transport's own overrides
  (`EmbeddedChannel`, `LocalChannel`, `AbstractNioChannel`) are `protected internal override`.
- Lifecycle checks read `IsActive` (2026-11-25). Java's `AbstractUnsafe` decides register
  (`if (isActive())` before `fireChannelActive`), bind, disconnect, close and `flush0` with the virtual
  `isActive()` (`AbstractChannel.java`), as `AbstractNioChannel.connect` does. `AbstractChannel` read its
  private `_active` flag there, so an `IsActive` override did not change them; the handler-proxy tests'
  `EmbeddedChannel` subclasses that report an inactive channel then received `ChannelActive` on
  registration, which Java does not fire. Those points (register, bind, connect, disconnect, close and its
  deregistration, `flush0`) now read `IsActive`; `BeginRead` keeps the flag, because it holds a read for the
  CLR socket and 4.2's `beginRead()` has no `isActive()` check ([handler-proxy-porting.md](handler-proxy-porting.md)).
- `PendingWriteQueue` producers complete with the write (2026-11-25). Java passes the `ChannelPromise` itself to
  `write(msg, promise)`, so it completes when the write does. The queue's producer of a write that completes later
  was completed by an awaited continuation, which ran on a thread-pool thread and submitted the completion to the
  executor; on an `EmbeddedChannel` it then waited for `RunPendingTasks`, and handler-proxy's
  `testRemoveOnProxyConnectionEventWritesPendingWrites` failed under load. The producer is now completed by a
  continuation on Common's `EventExecutorTaskScheduler` over the queue's executor, which the completing thread
  queues before the operation's `TrySetResult` returns and which runs at once on the event loop (otherwise through
  `Execute`); a rejected hand-off faults that continuation with a `TaskSchedulerException`, without throwing into the
  completing thread, and the producer fails with the rejection as before (both measured on .NET 10).
- `UnknownHostException` (owner decision 2026-11-25) is Common's `Netty.NET.Common.Internal.UnknownHostException`
  (an `IOException`, as `java.net.UnknownHostException` extends `java.io.IOException`), no longer
  `SocketException(SocketError.HostNotFound)`, which cannot carry the cause that resolver-dns records. Resolver's
  `DefaultNameResolver` throws it (a `SocketException` from `Dns.GetHostAddressesAsync` becomes its cause), and
  `AbstractBootstrap.SocketAddress` keeps an address unresolved when it is thrown. `NioDatagramChannel`'s
  `SocketError.HostNotFound` stands for `UnresolvedAddressException` and is unchanged.
- TCP_NODELAY: Java's `DefaultSocketChannelConfig` constructor enables it only when
  `PlatformDependent.canEnableTcpNoDelayByDefault()` and ignores any exception.
  `NioSocketChannel`'s constructor (the CLR config is created lazily) now has the same
  guard and ignores any exception instead of only `SocketException`/`ObjectDisposedException`
  ([Socket.NoDelay](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.nodelay?view=net-10.0)).
- Tests: `TcpSocketChannelSourceTest` is `NioSocketChannelTest` and
  `DatagramChannelSourceTest` is `NioDatagramChannelTest` (each maps to exactly that one
  Java fixture). `DomainSocketSourceTest` (two Java fixtures) and `NioLifecycleSourceTest`
  (three) keep their names. Tests that reached the socket through reflection on
  `_socket` now invoke `AbstractNioChannel.JavaChannel()`.

## Transport I/O core alignment (2026-11-16)

Owner decisions of 2026-11-16: the event-loop and I/O core follow the Java 4.2 interfaces and classes,
the NIO channels register through `IoEventLoop.register(IoHandle)` and react to `NioIoEvent` ready
ops, the NIO read/write loops read like the Java code, the `SelectStrategy` family is ported and
used by `NioIoHandler.run`, and the unused `Bootstrapping/FailedChannel.cs` is deleted
(`bootstrap/FailedChannel.java` is `jvm-only-replaced`, Future family).

### Type map

| Before | Java name, C# type | File |
| --- | --- | --- |
| `IIoHandle` (`HandleCompletion(IoRegistration, object)`, `Registered(IoRegistration)`, `Dispose`) | `IoHandle`: `IIoHandle` (`Handle(IIoRegistration, IIoEvent)`, `Registered()`, `Unregistered()`, `Close()`) | `Channels/IIoHandle.cs` |
| `IoRegistration` (sealed class, `PublishAsync`, `CancelAsync`) | `IoRegistration`: `IIoRegistration` (`Attachment<T>()`, `Submit(IIoOps)`, `IsValid()`, `Cancel()`) | `Channels/IIoRegistration.cs` |
| `CompletionIoHandler`, `IoCompletion` | `IoHandler`: `IIoHandler`; `nio/NioIoHandler`, `local/LocalIoHandler` | `Channels/IIoHandler.cs`, `Channels/Nio/NioIoHandler.cs`, `Channels/Local/LocalIoHandler.cs` |
| (none) | `IoOps`/`IoEvent`/`IoHandlerContext`/`IoHandlerFactory`: `IIoOps`, `IIoEvent`, `IIoHandlerContext`, `IIoHandlerFactory` | `Channels/IIoOps.cs`, `IIoEvent.cs`, `IIoHandlerContext.cs`, `IIoHandlerFactory.cs` |
| `IIoEventLoop(Group).RegisterIoAsync(IIoHandle, CancellationToken)` | `register(IoHandle)`: `RegisterAsync(IIoHandle)` returning `Task<IIoRegistration>` | `Channels/IIoEventLoop.cs`, `IIoEventLoopGroup.cs` |
| `DefaultEventLoop` (I/O loop) | `SingleThreadIoEventLoop` | `Channels/SingleThreadIoEventLoop.cs` |
| `DefaultEventLoopGroup` (I/O group, `Func<CompletionIoHandler>`) | `MultithreadEventLoopGroup`, `MultiThreadIoEventLoopGroup(IIoHandlerFactory)` | `Channels/MultithreadEventLoopGroup.cs`, `MultiThreadIoEventLoopGroup.cs` |
| (none) | `DefaultEventLoop`/`DefaultEventLoopGroup`: the non-I/O loops of Java | `Channels/DefaultEventLoop.cs`, `DefaultEventLoopGroup.cs` |
| `ManualIoEventLoop` (completion queue) | `ManualIoEventLoop(owningThread, IIoHandlerFactory[, parent, ticker])` | `Channels/ManualIoEventLoop.cs` |
| (none) | `AbstractEventLoop`, `AbstractEventLoopGroup` | `Channels/AbstractEventLoop.cs`, `AbstractEventLoopGroup.cs` |
| (none) | `SelectStrategy`, `SelectStrategyFactory`: `ISelectStrategy`, `ISelectStrategyFactory`; `DefaultSelectStrategy` (internal), `DefaultSelectStrategyFactory` | `Channels/ISelectStrategy.cs`, ... |
| (none) | `NioIoOps`, `NioIoEvent`: `INioIoEvent`, `NioIoHandle`: `INioIoHandle`, `NioSelectableChannelIoHandle<S>` | `Channels/Nio/*.cs` |
| (none) | `LocalIoHandle`: `ILocalIoHandle`, `LocalIoOps` | `Channels/Local/*.cs` |
| `Bootstrapping.FailedChannel` | deleted, `jvm-only-replaced` | - |

### CLR readiness: `NioIoHandler` over `Socket.Select`

`java.nio.channels.Selector` stays `jvm-only-replaced`. `NioIoHandler` keeps the Java `run`, `select`
(wakenUp protocol, deadline rounding, premature-return rebuild), `processSelectedKeys`,
`DefaultNioRegistration`, `prepareToDestroy` and `wakeup`, but its selector is a private
`Socket.Select` set with a self-connected loopback UDP socket as wakeup pipe. This is the design of
OpenJDK's Windows selector (`sun.nio.ch.WindowsSelectorImpl`: `select()` fd sets and a wakeup pipe).
Every interest maps to the same readiness the JDK key reports:

| `NioIoOps` (SelectionKey value) | Select set | Channel action on the event |
| --- | --- | --- |
| `READ` (1) | read set | `read()`: non-blocking `Socket.Receive`/`ReceiveFrom`; `WouldBlock` = 0, EOF = -1 |
| `ACCEPT` (16) | read set (listening socket) | `read()`: non-blocking `Socket.Accept`; `WouldBlock` = `null` |
| `CONNECT` (8) | write set and error set (Winsock reports a failed non-blocking connect in `exceptfds`) | `finishConnect()`: `SO_ERROR`, then `Socket.Connected` |
| `WRITE` (4) | write set | `forceFlush()`: non-blocking `Send`/`Send(IList<ArraySegment<byte>>)`; `WouldBlock` = 0 |

The owner's suggested completion-arming mapping (zero-byte receive, `AcceptAsync`, `ConnectAsync`, send
completion; precedent `IoUringIoOps`/`IoUringIoEvent`/`IoUringIoHandler` of the pinned repository) was
measured and not chosen: a zero-length receive consumes a UDP datagram (no readiness without data), an
`OP_WRITE` signal would need a data-carrying send, and in-flight completions survive deregistration.
`Socket.Select` reports all four readinesses on Windows, Linux and macOS, so no completion carries data and
deregistration simply removes the socket from the set. Cost: each selection is O(n) in the registered
sockets, as in the JDK Windows selector.

Evidence: [Socket.Select](https://learn.microsoft.com/dotnet/api/system.net.sockets.socket.select),
[Winsock select](https://learn.microsoft.com/windows/win32/api/winsock2/nf-winsock2-select),
[Socket.Blocking](https://learn.microsoft.com/dotnet/api/system.net.sockets.socket.blocking),
[SocketError](https://learn.microsoft.com/dotnet/api/system.net.sockets.socketerror),
[Winsock shutdown](https://learn.microsoft.com/windows/win32/api/winsock/nf-winsock-shutdown),
[WindowsSelectorImpl](https://github.com/openjdk/jdk/blob/master/src/java.base/windows/classes/sun/nio/ch/WindowsSelectorImpl.java).

### Channel side

- `AbstractNioChannel` sets `Socket.Blocking = false`, registers its nested `AbstractNioUnsafe`
  (`INioIoHandle`) in `doRegister`, cancels it in `doDeregister`, submits interest through
  `addAndSubmit`/`removeAndSubmit` (the registration attachment is the current `NioIoOps`, the
  `interestOps()` of the JDK key), ports `connect`/`finishConnect` (connect promise, timeout,
  cancellation), `flush0`/`forceFlush`/`isFlushPending` and `handle(IoRegistration, IoEvent)`.
- `AbstractNioByteChannel` is the Java `NioByteUnsafe.read`, `doWrite0`/`doWriteInternal`/`doWrite`,
  `incompleteWrite`/`setOpWrite`/`clearOpWrite`; `NioSocketChannel` and `NioDomainSocketChannel` each
  keep the Java gathering `doWrite` over `ChannelOutboundBuffer.NioBuffers`.
- `AbstractNioMessageChannel` is the Java `NioMessageUnsafe.read` and `doWrite` (maxMessagesPerWrite);
  the server channels implement `doReadMessages` with a non-blocking accept, `NioDatagramChannel`
  implements `doReadMessages`/`doWriteMessage`.
- `AbstractChannel` lost the CLR completion pump (`DispatchSocketAsync`, `DispatchReadAsync`,
  `TryGatherWriteAsync`, `DoWrite0`); `flush0()` calls `doWrite(ChannelOutboundBuffer)`, and the
  Local/Embedded transports keep their per-message Task writes.
- `LocalChannel`/`LocalServerChannel` register a `LocalIoHandle` with an `IoEventLoop` and use a
  shutdown hook on a non-I/O `SingleThreadEventLoop`, as in Java.

CLR adaptations kept as `// CLR:` comments: Windows datagrams are received into 64 KiB staging and
truncated like `DatagramChannel.receive`; a read requested after `shutdownInput()` runs once on the loop
because Winsock stops reporting that socket readable; `DefaultFileRegion` transfers one positional chunk
per attempt with a non-blocking send, and other `FileRegion`s write into a Stream whose unaccepted bytes
are sent first (the io_uring transport queues non-`DefaultFileRegion` chunks the same way);
`SingleThreadIoEventLoop.ConfirmShutdown` also calls `prepareToDestroy()` because an immediate `StopAsync`
can arrive after the loop body checked `isShuttingDown()`.

### Tests

Java-ported fixtures construct loops as Java does: `new MultiThreadIoEventLoopGroup(n, NioIoHandler.NewFactory())`
for sockets, `new MultiThreadIoEventLoopGroup(n, LocalIoHandler.NewFactory())` or `new DefaultEventLoopGroup(n)`
where the Java test uses them, `new ManualIoEventLoop(thread, factory)`. `SingleThreadIoEventLoopTest`,
`ManualIoEventLoopTest`, `NioIoHandlerTest`, `IoEventLoopGroupTest` and `MultiThreadIoEventLoopGroupTest`
are ported with their Java test handlers; `NioEventLoopTest.testSelectableChannel` is now mapped. CLR
contract tests that pinned the completion pump were rewritten for readiness (gathering, file-region,
outbound I/O-phase, half-close write failure) or lost the methods that only described consumed-but-withheld
native input.

## Comment and contract completion (2026-11-16)

Owner rule: an original comment is ported together with the Java code it documents. A ported entry
(`clr-adapted`) may leave a comment out of the C# source only through `comments.status: reviewed`, which lists
every unmatched original comment verbatim, in source order, with a per-comment disposition:

- `deprecated-not-ported`: the documented Java member carries `@Deprecated` (its `@deprecated` text names the
  replacement); `clr` names the C# replacement.
- `jvm-only-replaced`: the documented member uses a JVM-only API (`javaApi`, for example `ChannelPromise`,
  `SelectorProvider`, `ClassLoader`, `CancelledKeyException`, `fillInStackTrace`, `Unsafe` for the folded
  `Channel.Unsafe`); `clr` and `evidence` (https URL or repository file) are required.

`tools/Test-TransportPortingManifest.ps1` locates `member` in the pinned source (outside comments and literals)
and requires the comment to document it (Javadoc or line comment directly before it, trailing on its line, or
inside its body). A `deprecated-not-ported` member must carry `@Deprecated`; a `jvm-only-replaced` member must
contain the `javaApi` token. Ten deliberately broken manifest copies (missing, reordered or edited dispositions,
a member the comment does not document, an API token outside the member, `@Deprecated` missing, a reviewed entry
still `in-progress`, missing reason or evidence) are all rejected. Test entries never use this mode.

Code ported to carry comments (behavior changes have supplemental cases in
`JavaSourceAlignmentContractTest`, and DefaultChannelIdTest maps all six original methods):

| Java | C# change |
| --- | --- |
| `DefaultChannelId` | Java layout (machine ID, PID, sequence, timestamp, random) replaces the Guid; `Environment.ProcessId` replaces ProcessHandle/JMX reflection; JSON carries the long text |
| `AbstractBootstrap`, `Bootstrap`, `ServerBootstrap` | public abstract `AbstractBootstrap` base (internal constructor, abstract `Config`, `toString`); `initAndRegister`/`doBind`/`doBind0` and `doResolveAndConnect`/`doResolveAndConnect0`/`doConnect` follow the Java structure with Tasks; `ExternalAddressResolver` holder; CLOSE_ON_FAILURE completes before the promise fails |
| `AbstractChannel` | `newId()`, final `hashCode`/`equals`, cached `localAddress()`/`remoteAddress()` over abstract `LocalAddress0()`/`RemoteAddress0()`, `neverRegistered`, executor-cache clearing, SO_BROADCAST bind warning, `closeIfClosed()` after bind failure and disconnect, `annotateConnectException`, `failFlushed` in `flush0` |
| `ChannelOutboundBuffer` | `failFlushed(cause, notify)`, `remove0(cause, notifyWritability)` |
| `AbstractChannelHandlerContext` | `childExecutor`/`contextExecutor` cache |
| `ChannelHandlerAdapter`, `DefaultChannelPipeline.checkMultiplicity` | non-volatile `added` flag, per-thread `isSharable` cache, source messages |
| `SimpleChannelInboundHandler`, `SimpleUserEventChannelHandler` | the four Java constructors, including the `Class` (C# `Type`) overloads |
| `ChannelInboundHandler`, `ChannelInboundHandlerAdapter` | explicit re-abstraction of `exceptionCaught`; `[Skip]` override in the adapter |
| `ChannelException`, `EventLoopException` | shared constructor, `newStatic`; `EventLoopException : ChannelException`; the cause-only constructor's message is Java's `cause.toString()` through `ThrowableUtil.ThrowableToString` (2026-11-24, owner decision; also the CLR-only `ConnectTimeoutException(Exception)`) |
| `EventLoop` | `IEventLoop : IOrderedEventExecutor, IEventLoopGroup` (`next()` returns `this`) |
| `LocalServerChannel`, `LocalChannelRegistry`, `LocalAddress` | state field, `readInbound`/`serve0` with `incMessagesRead`; source messages; ephemeral `E:` + hash id |
| `NioDatagramChannel`, `DefaultDatagramChannelConfig`, `AbstractNioChannel` | `filterOutboundMessage` with `newDirectBuffer`, `(DatagramChannel)` constructor, JDK default family (IPv6 dual mode), broadcast warning, buffer-size checks |
| NIO server/domain channels | `isActive()`, `localAddress0()`, `closeOnReadError` overrides, `newChannel()`, `NioSocketChannelUnsafe` |
| `DatagramPacket` | extends `DefaultAddressedEnvelope<ByteBuf, IPEndPoint>` and implements `IByteBufHolder` |
| `EmbeddedEventLoop`, `ChannelGroup`, `DefaultChannelConfig` | source structure and messages; matcher-less overloads |

Still `in-progress` (the remaining comments need the Java structure ported first; each entry's review lists
it): `ChannelOutboundBuffer` (62), `LocalChannel` (38).

### Context, pipeline and EmbeddedChannel structure (2026-11-17)

`AbstractChannelHandlerContext`, `DefaultChannelPipeline` and `EmbeddedChannel` are `clr-adapted`. Every original
comment is at its code except seven per-comment dispositions: `ensurePromiseUseCorrectExecutor` and the
`VoidChannelPromise` logging of `notifyOutboundHandlerException` (JVM-only), the `ChannelPromise` overloads of
`writeOneInbound`/`writeOneOutbound` (JVM-only) and the `@Deprecated` `lastInboundBuffer()`/`lastOutboundBuffer()`.

| Java | C# |
| --- | --- |
| handler states `INIT`/`ADD_PENDING`/`ADD_COMPLETE`/`REMOVE_COMPLETE` | `SetAddPending`, `SetAddComplete`, `SetRemoved`, `CallHandlerAdded`, `CallHandlerRemoved`, `InvokeHandler`; `Interlocked` replaces the field updater; a new context starts at `INIT` |
| `fireXxx` and the outbound operations | Java shape: find (do-while with `skipContext`), head check, handler call, `invokeExceptionCaught`, re-invocation from this context on the next executor through `safeExecute`; interface dispatch replaces the `instanceof` adapter branches (a HotSpot JDK-8180450 workaround), because a call through the adapter class would bypass an explicit interface re-implementation |
| `Tasks`, `WriteTask` | cached `Action` delegates; `WriteTask` pooled on the Common `Recycler` with the sign-bit flush flag, pending outbound bytes, `cancel()` after a rejected submission and `lazyExecute` for writes without flush |
| promise arguments | the returned `Task`; `isNotValidPromise` checks the `CancellationToken`; a `TaskCompletionSource` carries the outcome of the re-invoked operation across an executor hop; `safeExecute` without a promise (flush) fires `exceptionCaught` as `voidPromise()` does |
| pending handler callbacks | `PendingHandlerCallback` list, `callHandlerAddedForAllHandlers`, `invokeHandlerAddedIfNeeded`; a rejecting executor removes the handler (added task) or only marks it removed (removed task) |
| pipeline mutation | `internalAdd` with `AddStrategy`, `add*0`, `filterName`/`checkDuplicateName` (head and tail names are not reserved), identity `childExecutors`, `tryRemove`/`isLinked`/`atomicRemoveFromHandlerList`, `replace0`, `destroyUp`/`destroyDown`; `lock (_gate)` stands for `synchronized (this)` |
| pipeline events | every inbound event starts at `HeadContext` (`channelRegistered` runs `invokeHandlerAddedIfNeeded`, `channelUnregistered` destroys a closed pipeline unless an offloaded close is pending); `TailContext` has no `[Skip]`; `onUnhandled*` have the source bodies |
| `EmbeddedChannel` | `Builder` (public constructor: a `builder()` method cannot share the nested type's name, CS0102), every constructor through the builder, `setup` with a `ChannelInitializer`, `EmbeddedChannelPipeline`, `finish(boolean)`, `cancelRemainingScheduledTasks`, channel-level and folded `EmbeddedUnsafe` wrappers that run the pending tasks, `checkException(promise)` as the returned `Task`, `inboundMessages()`/`outboundMessages()`/`hasPendingTasks()` as methods; `finishAndReleaseAll()` is `virtual` (2026-11-25) because the Java method is public and non-final and codec-http2's `CompressorHttp2ConnectionEncoderTest` overrides it |

Supplemental cases: `ContextPipelineEmbeddedAlignmentContractTest` (15). `JavaStringConversionContractTest` expects
`EmbeddedChannel+EmbeddedChannelPipeline{...}` (the JVM prints `EmbeddedChannel$EmbeddedChannelPipeline{...}`).

### Separate executors and registration at shutdown: Java parity (2026-11-26)

Owner decision: the open "I/O-handler/group rejection" and "separate-executor shutdown ownership" items are closed
as Java parity. No shutdown ownership beyond the pinned source is added across an event loop and a handler's
separate `EventExecutorGroup`. `PipelineExecutorShutdownContractTest` (6) records the source behavior:

| Case | Behavior (as in Netty 4.2.19) |
| --- | --- |
| separate executor stopped before registration | `PendingHandlerAddedTask` is rejected; the handler is unlinked without `handlerAdded`/`handlerRemoved` |
| separate executor stopped before close, I/O loop | the rejected inbound hand-off reaches `exceptionCaught` of the handler before it; `destroy()` is rejected before any `handlerRemoved`; `runAllTasks` (`safeExecute`) logs it and the loop keeps running |
| the same on `DefaultEventLoop` | `DefaultEventLoop.run()` calls `runTask()`, so the rejection ends the loop with that cause |
| loop shut down with a zero quiet period | the separate executor's hand-back to the loop is rejected; no handler is removed, and `DefaultEventExecutor.run()` (`runTask()`) ends the separate executor too |
| loop shut down with a quiet period | `ST_SHUTTING_DOWN` still admits the hand-backs; every handler is removed |
| registration during the quiet period | admitted; the shutdown then closes the channel |

`AbstractChannel.RegisterAsync` now follows `AbstractUnsafe.register` for the rejected hand-off: it logs "Force-closing
a channel whose registration task was not accepted by an event loop: {}" (only when the loop did not admit the task;
a failure inside the admitted task is the source's `register0` path) and fails with the source messages
"registered to an event loop already" and "incompatible event loop type: " + the loop's type name
(`EventLoopLifecycleContractTest.RegistrationFailuresKeepTheSourceMessages`).

## Testsuite inventory (2026-11-18)

The `testsuite` module is ported only where it needs nothing but the modules in the manifest's
`testsuiteInventoryPolicy.portedModules` (common, buffer, resolver, transport, since 2026-11-18 codec-base, since
2026-11-19 the codec modules that need only codec-base, since 2026-11-23 codec-compression, codec-protobuf and
codec-xml, and since 2026-11-24 pkitesting, handler and codec-http). Until 2026-11-18 the manifest listed hand-selected testsuite sources, so four
transport-only sources (`AbstractSingleThreadEventLoopTest`, `DefaultEventLoopTest`, `NioEventLoopTest`,
`ServerSocketSuspendTest`) were missed. The manifest now records every pinned testsuite file, and the checker
enforces it:

- `testsuiteEntries` (60): ported sources, plus `util/TestUtils.java` (JVM-only heap/thread dump and xz
  compression members recorded per comment) and three `package-info.java` summaries mapped in the table above.
- `testsuiteDeferredEntries` (6, with 3 declared JUnit tests): sources that need a module that is not ported yet:
  `transport-sctp` or `transport-udt`. They are ported with that module; this is not a decision against porting them.

`tools/Test-TransportPortingManifest.ps1` lists `testsuite/src` and requires each file to be recorded exactly once.
For a deferred file it resolves every `io.netty` import against the pinned `*/src/main/java` sources (nested
types resolve to their compilation unit) and recomputes the blocking modules, same-package deferred types it names
(their blockers carry over, as `AbstractSctpTest` needs `transport-sctp` through `SctpTestPermutation`), its
declared tests and its kind; a deferred `package-info.java` takes the blockers of its whole package. A deferred
file that needs only ported modules fails the check as a porting gap. A ported testsuite entry may import a type
of an unported module only through a recorded CLR stand-in (`unportedImports`); none remains.

Codec-base was ported on 2026-11-18 ([codec-base-porting.md](codec-base-porting.md)), so it joined the ported
modules: `SocketFixedLengthEchoTest` and `SocketStringEchoTest` are ported, `SocketObjectEchoTest` is
`deprecated-not-ported` with the codec-base serialization package (its family owner `ObjectEncoder` is recorded in
the codec-base manifest, which the checker also reads), `SocketChannelNotYetConnectedTest` uses the real
`ByteToMessageDecoder` instead of its stand-in, and codec-base no longer blocks the TLS and UDT fixtures.

On 2026-11-19 codec-dns, codec-haproxy, codec-memcache, codec-mqtt, codec-redis, codec-smtp, codec-socks and
codec-stomp were ported (one manifest each, checked by `tools/Test-CodecPortingManifest.ps1`) and joined the ported
modules. No testsuite file imports them, so the testsuite entries and the 14 deferred files are unchanged; only the
module list in the deferred reasons changed.

On 2026-11-23 codec-compression, codec-protobuf and codec-xml joined the ported modules (codec-marshalling has no
project); no testsuite file imports them, so again only the module list in the deferred reasons changed. On
2026-11-24 pkitesting joined them, so the six TLS sources `SocketSslClientRenegotiateTest`, `SocketSslEchoTest`,
`SocketSslGreetingTest`, `SocketSslLargeCertificateTest`, `SocketSslSessionReuseTest` and `SocketStartTlsTest` lost
their pkitesting blocker and imports (the checker recomputes them). The same day the handler module was completed with
`io.netty.handler.ssl` ([handler-porting.md](handler-porting.md)) and joined the testsuite's ported modules, so the
seven sources it blocked, those six and `TrafficShapingHandlerTest`, are ported (testsuite step of 2026-11-24).
`Netty.NET.Transport.Tests` references `Netty.NET.Handler` and `Netty.NET.PkiTesting`, and `Netty.NET.Handler` opens
its internals to it as to `Netty.NET.Handler.Tests` (`SslHandler.Engine()`, the SslStream of an `SSLEngine` session,
and the protected internal `SslContext.NewHandler(alloc, startTls)`, the counterpart of `new SslHandler(newEngine(alloc),
startTls)`). They follow the ssl port's mappings (docs/handler-porting.md, "SSL tests"):

| Source | Status | C# |
| --- | --- | --- |
| `SocketSslEchoTest` | `clr-adapted` | `[Theory]` over the JDK pair (OPENSSL is library-not-ported) without the delegated-task executor rows (`SslHandler(SSLEngine, Executor)` is not ported): 24 rows; the 8 `NONE` rows pass, the 8 `CLIENT_INITIATED` rows are skipped (SslStream renegotiates only as a server) and the 8 `SERVER_INITIATED` rows are skipped (on TLS 1.2 a server renegotiation fails with "Received data during renegotiation." while the client keeps sending, measured 8 of 8, `SslStream.IO.cs` `ReceiveHandshakeFrameAsync`). The cipher-suite change before renegotiating has no SslStream counterpart; `logStats` logs `NegotiatedCipherSuite`, or `SSL_NULL_WITH_NULL_NULL` before the handshake as an `SSLEngine` session reports it. |
| `SocketSslGreetingTest` | `clr-adapted` | The server writes before the handshake completed; the three `SSLPeerUnverifiedException` checks of the server session are one `Assert.Null(Engine().RemoteCertificate)`. |
| `SocketSslSessionReuseTest` | `clr-adapted` | `testSslSessionReuse` observes resumption on the wire: a CLR-only handler before the client SslHandler records the non-empty `session_id` of each ServerHello (RFC 5246 7.4.1.3) in place of `sessionContext().getIds()` (SChannel resumed the session, measured). `testSslSessionTrustManagerResumption` is CLR-inapplicable (`ResumableX509ExtendedTrustManager` and SSLSession values are JVM-only). |
| `SocketStartTlsTest` | `verified` | Both tests over the JDK pair; the server handler is `NewHandler(alloc, true)` and the client's `NewHandler(alloc, false)`. |
| `TrafficShapingHandlerTest` | `verified` | All nine tests; the scheduled thread pool is a `DefaultEventExecutorGroup(10)`, as in the handler module's own `TrafficShapingHandlerTest`. |
| `SocketSslLargeCertificateTest` | `clr-adapted` | `resumptionWithLargeCertificates` is CLR-inapplicable: its subject is OpenSSL's `MAX_CERTIFICATE_LIST_BYTES` (enabled only with netty-tcnative). |
| `SocketSslClientRenegotiateTest` | `clr-adapted` | `testSslRenegotiationRejected` is CLR-inapplicable: it checks that an OpenSSL server rejects a client-initiated renegotiation (`OpenSslServerContext` is library-not-ported, `JdkSslClientContext` deprecated-not-ported, and an SslStream client cannot start a renegotiation). |

The seven classes run 37 cases: 21 pass and 16 are skipped (the renegotiation rows above), identical in two runs.

Later on 2026-11-24 codec-http was ported ([codec-http-porting.md](codec-http-porting.md)) and joined the ported
modules, so `SocketSpdyEchoTest`, the last source it blocked, is ported (testsuite step of 2026-11-24).
`Netty.NET.Transport.Tests` references `Netty.NET.Handler.Codec.Http`:

| Source | Status | C# |
| --- | --- | --- |
| `SocketSpdyEchoTest` | `verified` | Both tests (`testSpdyEcho`, `testSpdyEchoNotAutoRead`) run through `AbstractSocketTest.Run` (both allocators x the NIO socket combination) with `SpdyFrameCodec(SpdyVersion.SPDY_3_1)` of `Netty.NET.Handler.Codec.Spdy` on the server and a raw `ByteBuf` client, as in the source: the server decodes every frame of `createFrames(3)`, drops the UNKNOWN and NOOP frames (20 ignored bytes) and re-encodes the rest byte for byte. The adaptations are syntactic (the unseeded `java.util.Random` is an unseeded `System.Random`, `switch (version)` over the `SpdyVersion` sealed class is a `when` case guard, the anonymous `ChannelInitializer` is an `ActionChannelInitializer<IChannel>`, `Thread.sleep(50)` is `Task.Delay(50)`); no assertion or scenario step is changed. Both tests pass, identical in two runs. |

The testsuite now has 60 entries and 6 deferred files with 3 declared tests. The remaining deferred files need
`transport-sctp` (four files: `AbstractSctpTest`, `SctpEchoTest`, `SctpTestPermutation` and the sctp `package-info`)
or `transport-udt` (two files: `UDTClientServerConnectionTest` and the udt `package-info`).

## Fixed-rate scheduling tests (2026-11-27)

`SingleThreadEventLoopSourceTest.ScheduleLaggyTaskAtFixedRateA` was recorded several times as failing in loaded runs.
Under a CPU load of twice the logical processor count, the `Schedule*` methods of the fixture failed in 7 of 12 runs,
for two causes that the source does not have:

- `CountdownEvent.Signal()` throws once the count is zero, where `CountDownLatch.countDown()` does nothing. When the
  laggy run's `Thread.Sleep(401)` overslept, one more overdue run came before the cancel; its `Signal()` faulted the
  periodic Task, so `Assert.True(periodic.IsCanceled)` failed (the recorded failure). The tests count down through a
  helper that does nothing at zero; only the event loop thread counts down.
- The test waited for the latch in `Task.Run`, adding a thread-pool hop between the last count down and the cancel, in
  which another run could start. The test thread now waits, as `allTimeStampsLatch.await()` does.

After the fix no run faults the Task. What remains are the source's own timing assertions: exactly five runs before
the cancel (the cancel has 50 ms after the fifth run of `testScheduleTaskAtFixedRate`), cumulative 80..120 ms windows,
<= 10 ms catch-up gaps. They are kept unchanged (owner decision 2026-11-27: no wider CLR tolerance). Under the 2x CPU load (8 of 12 runs) and while every test project of
the solution ran in parallel (8 of 10 runs; Common.Tests, Handler.Ssl.Ocsp.Tests and two other Transport timing tests
failed in the same run), OS scheduling delays of 20-60 ms broke them (a late first run makes the next gap 69 ms, an
oversleeping run adds a sixth or seventh timestamp). The Java build runs the tests of one module at a time; the
corresponding run, `Netty.NET.Transport.Tests` alone, passes them (five consecutive runs of the fixture).

## NioSocketChannel remote and local addresses after close (2026-11-27)

`NioSocketChannel.remoteAddress0()` and `localAddress0()` return `javaChannel().socket().getRemoteSocketAddress()` and
`getLocalSocketAddress()`. In the JDK those are the `SocketChannelImpl.remoteAddress` and `localAddress` fields
([SocketChannelImpl](https://github.com/openjdk/jdk17u/blob/master/src/java.base/share/classes/sun/nio/ch/SocketChannelImpl.java),
[SocketAdaptor](https://github.com/openjdk/jdk17u/blob/master/src/java.base/share/classes/sun/nio/ch/SocketAdaptor.java)),
and close clears neither:

- **Remote address:** the accepted-channel constructor sets it to the address accept returned, and `beginConnect` sets it
  to the target before the native connect. `connect` first runs `checkRemote`, which replaces a wildcard (any-local)
  target with the loopback address of the same family. A Java TCP channel therefore reports its remote address after it
  is closed, and after a failed connect it reports the target.
- **Local address:** the accepted-channel constructor, `bind` and a completed connect (`endConnect`,
  `endFinishConnect`) set it from the socket (`Net.localAddress(fd)`). A closed channel reports it; after a failed
  connect it is the explicitly bound address, or null when the channel was not bound.

The port read the live `Socket.RemoteEndPoint` and `LocalEndPoint`, which throw `ObjectDisposedException` once the
socket is closed (measured 2026-11-27), so `AbstractChannel.RemoteAddress` and `LocalAddress` were null for a channel
closed before the address was first read. Resolver-dns's `DnsQueryContext.RetryWithTcp` carried a fallback for the
remote address. On Windows `Socket.Connect` to a wildcard target fails with `WSAEADDRNOTAVAIL` (10049, measured
2026-11-27), where Java connects to loopback.

`NioSocketChannel` now records both addresses at the JDK's points:

- In the constructor, from `RemoteEndPoint` over a connected (accepted) socket and from `LocalEndPoint` over a bound one.
- In `Connect`, the remote address after replacing a wildcard target with loopback, before the socket connects, and the
  local address when the connect completes at once.
- In `Bind`, the local address. `AbstractNioByteChannel.DoBind0` now calls an internal virtual `Bind(Socket, EndPoint)`,
  as it calls `Connect`, standing for `SocketUtils.bind(SocketChannel, SocketAddress)`.
- In `DoFinishConnect`, the local address of a pending connect that completed.

`RemoteAddress0` and `LocalAddress0` return the recorded address, normalized like the live one (IPv4-mapped IPv6 to
IPv4), and read the live endpoint only when nothing was recorded (an unresolved `DnsEndPoint` target, which Java
rejects before connecting; an unbound socket). After a Linux address-only bind (`IP_BIND_ADDRESS_NO_PORT`) the local
address recorded at connect is read with `getsockname`, as the existing `LocalAddress` override does, because
`LocalEndPoint` may keep port 0; that path does not run on Windows and was not run on this host.
`NioDomainSocketChannel` is unchanged: its source reads `javaChannel().getRemoteAddress()` and `getLocalAddress()`,
which throw `ClosedChannelException` after close, and returns null, as the port does. The `RetryWithTcp` fallback is
removed and the source's `(InetSocketAddress) tcpCh.remoteAddress()` cast is back.

`NioSocketChannelAddressContractTest` (CLR supplemental, 9 cases) checks, after close, the remote and local addresses
of a connected and an accepted channel, the local address of a bound channel, the target and the explicitly bound
local address after a failed connect, and IPv4/IPv6 wildcard connects. The five remote-address cases fail with the
previous `RemoteAddress0` and `Connect` (null addresses; `AddressNotAvailable`), the four local-address cases fail with
the previous `LocalAddress0` (null), and all pass with the change. No Java test asserts these addresses;
`SocketConnectTest.testLocalAddressAfterConnect` still asserts null addresses before connect.
