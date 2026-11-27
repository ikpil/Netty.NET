# codec-native-quic porting

`netty-codec-native-quic` of Netty 4.2.19.Final (pinned commit `64cc10f38ea5f5bd7eae48507817c66680d0afdc`) holds the
JNI library of quiche and BoringSSL (C sources, native build templates, GraalVM and Android metadata), the QUIC tests,
which run in Java against that library, and the QUIC examples. Netty.NET runs QUIC on System.Net.Quic (msquic) behind
the API of `Netty.NET.Handler.Codec.Quic` (owner decisions of 2026-11-27, [quic-design.md](quic-design.md); the
implementation is [codec-classes-quic-porting.md](codec-classes-quic-porting.md#stage-2-the-msquic-implementation)). So
the module has no C# project of its own: its JNI library, build templates and metadata are recorded `jvm-only-replaced`,
its tests are ported to `test/Netty.NET.Handler.Codec.Quic.Tests` (namespace `Netty.NET.Handler.Codec.Quic.Tests`, next
to the codec-classes-quic test), where they run on loopback with both peers in .NET, and its runnable examples to the
executable `test/Netty.NET.Handler.Codec.Quic.Examples` (namespace `Netty.NET.Handler.Codec.Quic.Example`). The tests
were ported in two stages: stage 2 (with the msquic implementation) and stage 3 (the remaining tests, the examples and
the build templates). The [manifest](codec-native-quic-porting-manifest.json) records all 56 pinned files of
`codec-native-quic/src`; no entry is pending. Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "& ./tools/Test-CodecPortingManifest.ps1 -Module codec-native-quic"
```

| Status | Files |
| --- | --- |
| `verified` | 18: the shared test helpers (`AbstractQuicTest`, `QuicTestUtils`, `QuicChannelValidationHandler`), `QuicChannelEchoTest`, `QuicStreamChannelCreationTest`, `QuicStreamHalfClosureTest`, `QuicStreamShutdownTest`, `QuicStreamTypeTest`, `QuicStreamFrameTest` (stage 2) and `FlushStrategyTest`, `InsecureQuicTokenHandlerTest`, `NoQuicTokenHandlerTest`, `QuicCodecBuilderTest`, `QuicConnectionIdGeneratorTest`, `QuicStreamIdGeneratorTest`, `SipHashTest`, `QuicReadableTest`, `QuicStreamChannelCloseTest` (stage 3) |
| `clr-adapted` | 15: `QuicChannelConnectTest` (12 of 42 methods mapped), `QuicConnectionAddressTest` (3 of 4), `QuicTest` (3 of 4), `QuicStreamLimitTest` (2 of 4), the six tests whose every method is CLR-inapplicable (`QuicWritableTest`, `QuicChannelDatagramTest`, `QuicConnectionStatsTest`, `QuicConnectionPathStatsTest`, `QuicTransportParametersTest`, `QuicSslContextTest`: 14 methods), the helper `TestQuicTokenHandler` and the examples `QuicServerExample`, `QuicClientExample`, `QuicServerZeroRTTExample`, `QuicClientZeroRTTExample` |
| `jvm-only-replaced` | 23: the C sources and headers (5), the native build templates (3), GraalVM metadata (4), `AndroidManifest.xml`, the test resources (3), the tests of recorded units (`QuicheQuicCodecTest`, `QuicheQuicClientCodecTest`, `QuicheQuicServerCodecTest`, `QuicCodecDispatcherTest`, `QuicPortReuseTest`, `TestLogBackAppender`) and the epoll example `QuicServerSoReusePortExample` |

Of the 117 declared JUnit methods, 64 are mapped, 48 are CLR-inapplicable (each with its source contract, reason and
evidence in the manifest) and 5 belong to the tests of recorded units.

## The tests on System.Net.Quic

The Java tests put the codec built by `QuicServerCodecBuilder`/`QuicClientCodecBuilder` on a `NioDatagramChannel` (or
`EpollDatagramChannel`). msquic owns the UDP socket, so `QuicTestUtils` puts the codec on a `QuicEndpointChannel`, the
CLR-only channel that takes the datagram channel's place, on a `MultiThreadIoEventLoopGroup` with the NIO handler factory
(background threads, so the test host exits as the JVM does with its shutdown hook). The rest of `QuicTestUtils` keeps the
Java builder settings (`maxIdleTimeout(5000 ms)`, `initialMaxData(10000000)`, the stream windows `1000000` and the stream
limits `100`) except `activeMigration(false)`: it asks for the `disable_active_migration` transport parameter, which
System.Net.Quic cannot set, so the msquic builders report it unsupported (stage 3, see [Fixes](#fixes-in-stage-3)); no
test migrates a connection. The windows are rounded up to powers of two by the msquic codec (msquic takes power-of-two
windows). Other adaptations shared by all tests:

- `SelfSignedCertificate` is a self-signed ECDSA P-256 certificate for `localhost` created with
  `System.Security.Cryptography` (`CertificateRequest.CreateSelfSigned`); the context builders turn it into a key store
  with SChannel's persisted key (the PKCS #12 round trip of `SslContext`).
- `@ParameterizedTest @MethodSource("newSslTaskExecutors")` is an xUnit theory over `ImmediateExecutor.INSTANCE` and a
  single-thread `DefaultEventExecutor` (rows not enumerated at discovery). msquic runs TLS on its own threads, so the
  builders accept the executor and do not use it; the rows still exercise the builder API.
- `@Timeout(10)` is the `Timeout` of the test attributes (10 s), and every awaited operation takes
  `TestContext.Current.CancellationToken`.
- Futures and promises are Tasks (`sync()`/`get()` are awaits, listeners continuations, `ChannelFutureListener.CLOSE` and
  `QuicStreamChannel.SHUTDOWN_OUTPUT` continuations that call `CloseAsync()`/`ShutdownOutputAsync()`),
  `CountDownLatch` is `CountdownEvent` (counted down at most to zero, as Java's latch), `BlockingQueue` is
  `BlockingCollection` (taken with the test timeout), `UnsupportedOperationException` is `NotSupportedException`,
  `SSLException`/`SSLHandshakeException` are `AuthenticationException` and `AlreadyConnectedException` is
  `SocketException(SocketError.IsConnected)`.
- A test whose every method is CLR-inapplicable keeps a C# class without tests that carries the reasons and the original
  comments (as `JdkSslServerContextTest` in the handler port).

| Java test | C# test | Methods | Notes |
| --- | --- | --- | --- |
| `QuicChannelEchoTest` | `QuicChannelEchoTest.cs` | 2 (16 theory rows) | 1 MiB echo in both directions with auto-read on/off, heap/direct and composite buffers; the first client stream is stream 0 and the server's first stream 1 (RFC 9000 stream IDs) |
| `QuicStreamChannelCreationTest` | `QuicStreamChannelCreationTest.cs` | 2 | `CreateStreamAsync` and `NewStreamBootstrap()` with an attribute and `AUTO_READ` |
| `QuicStreamHalfClosureTest` | `QuicStreamHalfClosureTest.cs` | 2 | FIN after data: `ChannelInputShutdownEvent`, `ChannelInputShutdownReadComplete` and the close in the Java order |
| `QuicStreamShutdownTest` | `QuicStreamShutdownTest.cs` | 3 | `ShutdownInputAsync()` is STOP_SENDING (the peer's writes fail with `ChannelOutputShutdownException`), `ShutdownOutputAsync(100)` RESET_STREAM (the peer's `QuicStreamResetException` carries 100) |
| `QuicStreamTypeTest` | `QuicStreamTypeTest.cs` | 2 | writes on a peer-created unidirectional stream fail with `NotSupportedException` |
| `QuicStreamFrameTest` | `QuicStreamFrameTest.cs` | 2 | `READ_FRAMES`: `IQuicStreamFrame` reads with the FIN flag and a FIN frame written back |
| `QuicChannelConnectTest` | `QuicChannelConnectTest.cs` | 12 of 42 | see below |
| `FlushStrategyTest` | `FlushStrategyTest.cs` | 2 | `IFlushStrategy.AfterNumBytes`/`AfterNumPackets` |
| `InsecureQuicTokenHandlerTest` | `InsecureQuicTokenHandlerTest.cs` | 4 | IPv4 and IPv6 token round trips; `validateToken(token, address, dcid)` is the default interface member, called through `IQuicTokenHandler` |
| `NoQuicTokenHandlerTest` | `NoQuicTokenHandlerTest.cs` | 3 | |
| `QuicCodecBuilderTest` | `QuicCodecBuilderTest.cs` | 1 | the copy constructor, checked by reflection over every field of `QuicCodecBuilder<B>` (AssertJ's recursive comparison is `AssertRecursiveEquals`) |
| `QuicConnectionAddressTest` | `QuicConnectionAddressTest.cs` | 3 of 4 | `testNullByteBuffer` is CLR-inapplicable: a `ReadOnlyMemory<byte>` cannot be null; the null array's `NullPointerException` (a dereference) is `NullReferenceException` |
| `QuicConnectionIdGeneratorTest` | `QuicConnectionIdGeneratorTest.cs` | 4 | ids are `ReadOnlyMemory<byte>`, compared as arrays; `newId(null, 10)` passes a null array, which becomes an empty memory and fails with `ArgumentException` (no null `ReadOnlyMemory`) |
| `QuicStreamIdGeneratorTest` | `QuicStreamIdGeneratorTest.cs` | 2 | |
| `QuicTest` | `QuicTest.cs` | 3 of 4 | `test` is CLR-inapplicable: it asserts the quiche library's version string (`Quiche.quiche_version()`, a JNI unit); System.Net.Quic reports no msquic version |
| `SipHashTest` | `SipHashTest.cs` | 2 (65 theory rows and facts) | the 64 SipHash-2-4 reference vectors of the paper and the 1-3 hash |
| `QuicReadableTest` | `QuicReadableTest.cs` | 1 (2 rows) | 256 streams, half of them never read (auto-read off) |
| `QuicStreamChannelCloseTest` | `QuicStreamChannelCloseTest.cs` | 12 (24 rows) | close/half-close while active from either peer, and a write after `shutdownOutput()`/`close()` (`ChannelOutputShutdownException` on a half-closed bidirectional stream, else `ClosedChannelException`) |
| `QuicStreamLimitTest` | `QuicStreamLimitTest.cs` | 2 of 4 | the client-side limits map (`QuicStreamLimitChangedEvent`, `PeerAllowedStreams`, `STREAM_LIMIT_ERROR`); the two server-side methods are CLR-inapplicable, see [Stage 3](#stage-3-clr-inapplicable-methods) |
| `QuicWritableTest` | `QuicWritableTest.cs` (no tests) | 0 of 3 | CLR-inapplicable, see [Stage 3](#stage-3-clr-inapplicable-methods) |
| `QuicChannelDatagramTest` | `QuicChannelDatagramTest.cs` (no tests) | 0 of 6 | the QUIC datagram extension (RFC 9221) |
| `QuicConnectionStatsTest` | `QuicConnectionStatsTest.cs` (no tests) | 0 of 1 | connection statistics |
| `QuicConnectionPathStatsTest` | `QuicConnectionPathStatsTest.cs` (no tests) | 0 of 1 | path statistics |
| `QuicTransportParametersTest` | `QuicTransportParametersTest.cs` (no tests) | 0 of 1 | the peer's transport parameters |
| `QuicSslContextTest` | `QuicSslContextTest.cs` (no tests) | 0 of 2 | `SSLSessionContext` and `SSLParameters` |
| `TestQuicTokenHandler` | `TestQuicTokenHandler.cs` | helper | delegates to `InsecureQuicTokenHandler`; its `ByteBuf.order()` checks read a `@Deprecated` API that is not ported (Netty.NET buffers have no byte order of their own and read big-endian) |

### QuicChannelConnectTest

Mapped (12): `testConnectTimeout` (a `UdpClient` on the IPv4 loopback stands for `new DatagramSocket()`: msquic sends to
the given address, and 0.0.0.0 is not a destination on Windows), `testConnectFailsInParentPipeline` (the QuicChannel
connects through its parent's pipeline, so a handler of the endpoint fails it with the same exception),
`testConnectAlreadyConnected`, `testConnectMultipleTimes`, `testExtendedTrustManagerFailureOnTheClient` and
`testTrustManagerFailureOnTheClient` (a trust manager that throws is a `RemoteCertificateValidationCallback` returning
false), `testConnectSuccessWhenTrustManagerBuildFromSameCert`, the four `testConnectMutualAuth*Success` methods and
`testConnectionCloseFrameDeliveredWhenParentClosedImmediately`. `TestQuicTokenHandler` (a token handler of its own) is
reported unsupported by the msquic server codec builder, so the mapped methods that only need a working server use
`InsecureQuicTokenHandler`. The mutual authentication tests wait for the server's `channelActive` before the client
closes (see [Known problems](#known-problems)).

CLR-inapplicable (30), each with its source contract and reason in the manifest: QLOG (2), BoringSSL key logging (3,
with logback's appender), connection ID lengths, generators and the comparison of both peers' connection IDs (8,
System.Net.Quic hides the connection IDs: `testAddressValidation`, the two `testConnectWithCustomIdLength*`, the four
dropped-packet/generator tests, `testConnectWithoutTokenValidation`, `testConnectWithActiveConnectionIdLimit` counts here
too), datagram pipeline access (`testTimedOut` stalls the path by dropping datagrams; the endpoint carries none), custom
token validation, BoringSSL key types (2), 0-RTT, stream priorities, the server-side events of a failed handshake
(`testALPNProtocolMissmatch`, `testConnectMutualAuthFailsIfClientNotSendCertificate`: msquic fails the handshake before a
server QuicChannel exists), `SniCompletionEvent` (4: the SNI mapping selects the context in the listener's options
callback, without the event), keyless signing (2) and session tickets/reuse (2).

### Stage 3: CLR-inapplicable methods

| Java test | Methods | Reason |
| --- | --- | --- |
| `QuicStreamLimitTest` | `testStreamLimitEnforcedWhenCreatingViaServerBidirectional`, `...Unidirectional` | System.Net.Quic does not report the client's initial stream limits to a server connection (`QuicServerConnectionOptions.StreamCapacityCallback` is not called for them, [probe](#stage-3-probe)), so the server's second `OpenOutboundStreamAsync` waits for stream capacity instead of failing; ported and run, the test ended with `ClosedChannelException` at the 5 s idle timeout |
| `QuicWritableTest` | `testCorrectlyHandleWritabilityReadRequestedInReadComplete`, `testCorrectlyHandleWritabilityReadRequestedInRead` | the server's 64 KiB write must stay incomplete until the client (16 KiB stream window, auto-read off) reads; System.Net.Quic's `QuicStream` takes received data into its own buffer ahead of the application, so the write completes without any client read ([probe](#stage-3-probe)); ported and run, the first-read `assertFalse(writePromise.isDone())` failed |
| `QuicWritableTest` | `testBytesUntilUnwritable` | `bytesBeforeUnwritable()` is quiche's stream send capacity, lowered by exactly the bytes of each write; System.Net.Quic exposes no send capacity, so the msquic stream channel's writability follows the write buffer water marks, whose `ChannelOutboundBuffer` counts 96 bytes of overhead per entry (`assertEquals(before, newBefore + size)` cannot hold); ported and run, the test timed out |
| `QuicChannelDatagramTest` | all 6 | the datagram extension: `datagram(...)` reports `NotSupportedException` from `build()`, writes on an `IQuicChannel` fail |
| `QuicConnectionStatsTest`, `QuicConnectionPathStatsTest` | `testStatsAreCollected`, `testPathStatsAreCollected` | `CollectStatsAsync`/`CollectPathStatsAsync` report `NotSupportedException` |
| `QuicTransportParametersTest` | `testParameters` | `PeerTransportParameters()` throws `NotSupportedException` |
| `QuicSslContextTest` | both | no `SSLSessionContext` (session cache size/timeout getters and setters) and no `SSLParameters` in the handler and QUIC ports ([handler-ssl-design.md](handler-ssl-design.md)) |
| `QuicConnectionAddressTest` | `testNullByteBuffer` | `ReadOnlyMemory<byte>` cannot be null |
| `QuicTest` | `test` | the quiche version string has no System.Net.Quic counterpart |

### Stage 3 probe

A scratch console program on System.Net.Quic alone (.NET 10.0.11, Windows 11 10.0.26300, loopback; not part of the
repository), with a listener and a client that both allow one inbound stream of each type and a client receive window of
16 KiB for its own bidirectional streams, printed:

```
   105 [  6] client capacity +1 bidi +1 uni
   126 [  8] client ConnectAsync done
   126 [  8] server AcceptConnectionAsync done
   327 [ 10] client opened 0
   328 [ 11] server read 8
   329 [ 11] server 64 KiB write completed without client reads: True
   330 [ 11] client read 65536; write completed True
   357 [ 11] bidi stream closed on both sides
   357 [ 10] client capacity +1 bidi +0 uni
   861 [ 11] uni stream closed on both sides
   861 [ 10] client capacity +0 bidi +1 uni
  1367 [ 10] server opened 1
  2375 [  8] second server stream completed within 1s: False
```

The client's `StreamCapacityCallback` reports the server's initial limits before `ConnectAsync` completes and a new slot
when a stream is closed on both sides; the server's callback is never called, so the server cannot know the client's
limits and its second `OpenOutboundStreamAsync` waits; a 64 KiB server write completes before the client reads.

## Fixes in stage 3

The stage-3 tests and examples exposed three defects of `Netty.NET.Handler.Codec.Quic` (public API unchanged):

- **`QuicStreamLimitChangedEvent` after the handshake** (`MsQuicChannel`). Java fires the event once the connection is
  established (the streams left change from 0 to the peer's initial limits) and on every later change. msquic reports
  the initial limits before `ConnectAsync` completes, when the channel was not active yet, so no event fired and
  `QuicStreamLimitTest` waited for it. The channel now counts the capacity changes and fires the event after
  `channelActive` (client and server) when changes were reported, and for every later change.
- **Write after `shutdownOutput()`** (`MsQuicStreamChannel`). `QuicStreamChannelUnsafe.write` fails a write on a stream
  whose FIN was sent with `ChannelOutputShutdownException("Fin was sent already")` before it filters the message; the
  msquic stream channel filtered first and reported `NotSupportedException` for an unsupported message
  (`QuicStreamChannelCloseTest.testWriteToBidirectionalAfterShutdownOutput`). `WriteTransportAsync` now checks the FIN
  first.
- **`activeMigration`** (`MsQuicConfig`). Stage 2 rejected `activeMigration(true)` and accepted `activeMigration(false)`
  as quiche's default. quiche's default is `disable_active_migration = false` (migration allowed), and msquic allows
  migration too (its `MigrationEnabled` setting defaults to true and System.Net.Quic leaves it), so
  `activeMigration(true)` describes msquic's behavior and is accepted (`QuicServerExample` sets it), while
  `activeMigration(false)` needs the `disable_active_migration` transport parameter, which System.Net.Quic cannot set,
  and is reported unsupported. `QuicTestUtils` therefore leaves `activeMigration(false)` out.

## Examples

The five examples of `src/test/java/io/netty/handler/codec/quic/example` are one executable,
`test/Netty.NET.Handler.Codec.Quic.Examples` (not in `Netty.NET.sln`, as the QUIC projects). C# allows several `Main`
methods in one assembly only with an explicit startup object, so `ExampleLauncher` (CLR-only, copied from
`src/Netty.NET.Example/ExampleLauncher.cs`) is the `StartupObject`: it runs an example by its Java class name (with or
without the `io.netty.handler.codec.quic.example` prefix) and maps `-Dname=value` arguments to `GetProperty`:

```
dotnet run --project test/Netty.NET.Handler.Codec.Quic.Examples -- QuicServerExample
dotnet run --project test/Netty.NET.Handler.Codec.Quic.Examples -- io.netty.handler.codec.quic.example.QuicClientExample
```

| Java example | Status | Notes |
| --- | --- | --- |
| `QuicServerExample` | `clr-adapted` | the codec on a `QuicEndpointChannel` bound to `IPEndPoint(IPAddress.Any, 9999)`; the deprecated `SelfSignedCertificate` is the PkiTesting `CertificateBuilder` (as in Netty.NET.Example's `ServerUtil`); `collectStats()` fails, so nothing is logged and the listener still closes the endpoint after the first connection |
| `QuicClientExample` | `clr-adapted` | `IQuicChannel.NewBootstrap(endpoint)`; prints the response to `Console.Error` |
| `QuicServerZeroRTTExample`, `QuicClientZeroRTTExample` | `clr-adapted` | ported as written; `earlyData(true)` makes `QuicSslContextBuilder.Build()` throw `NotSupportedException` (System.Net.Quic exposes no 0-RTT), so each ends with that exception |
| `QuicServerSoReusePortExample` | `jvm-only-replaced` | needs the epoll transport (`EpollDatagramChannel`, `EpollChannelOption.SO_REUSEPORT`) and `QuicCodecDispatcher`, both recorded; msquic's listener owns the socket and its worker threads |

Run on loopback (Windows 11 10.0.26300, .NET 10.0.11, 2026-11-27), the server started first, then the client:

```
$ Netty.NET.Handler.Codec.Quic.Examples QuicServerExample          (background)
$ Netty.NET.Handler.Codec.Quic.Examples io.netty.handler.codec.quic.example.QuicClientExample
Hello World!

client exit=0 after 2437 ms
server exit=0 after 5369 ms (started 3 s before the client; it closes after the first connection)
```

The client prints `Hello World!\r\n` (its `Console.Error.WriteLine` adds the empty line), closes the connection once the
server's FIN arrived and exits 0; the server's `channelInactive` closes the endpoint, so it exits 0 too. Each zero-RTT
example ends with `Unhandled exception. System.NotSupportedException: earlyData(true) is not supported by
System.Net.Quic (msquic)` from `QuicSslContextBuilder.Build()`.

## Recorded files

- The C sources and headers (`netty_quic.h`, `netty_quic_boringssl.c/.h`, `netty_quic_quiche.c`, `musl_compat.c`): the
  JNI bindings of quiche and BoringSSL, not ported (owner decision 2026-11-27); System.Net.Quic calls msquic itself.
- The native build templates (`custom.m4.template`, `custom.m4.android.template`, `vs2010.custom.props.template`): the
  autoconf m4 macros and the MSBuild property sheet of the JNI library's build. Their comments (the license) are pinned
  with a whole-file disposition, computed as the checker reads them (contiguous `dnl` blocks for the m4 templates, XML
  comments for the props template).
- GraalVM native-image metadata (`jni-config.json`, `reflect-config.json`, `resource-config.json`,
  `native-image.properties`) and `AndroidManifest.xml`: JVM packaging metadata without a .NET counterpart (NativeAOT needs
  no JNI registration for System.Net.Quic; Android is not one of the owner's platforms).
- Test resources: `logback-test.xml` and the GraalVM agent filters `netty-filter.json`, `test-class-filter.json`
  configure the JVM test run.
- Tests of recorded units: `QuicheQuicCodecTest`, `QuicheQuicClientCodecTest` and `QuicheQuicServerCodecTest` test the
  quiche datagram codecs, `QuicCodecDispatcherTest` the dispatcher of datagrams to codecs, `QuicPortReuseTest` the
  dispatcher on `SO_REUSEPORT` binds (all `jvm-only-replaced` in codec-classes-quic or transport-classes-epoll), and
  `TestLogBackAppender` is the logback appender of the key log tests. Every declared method is recorded with that reason.
- `QuicServerSoReusePortExample` (see [Examples](#examples)).

## Known problems

- System.Net.Quic 10.0.11 on Windows ends the process with an access violation (0xC0000005 in msquic) when a server
  connection is closed, by the peer or by disposing the `QuicListener`, while the server is still validating the client
  certificate. With TLS 1.3 the client's `ConnectAsync` completes before that validation, so a client that presents a
  certificate and closes at once can crash the server process. Reproduced with System.Net.Quic alone (a scratch console
  probe: a listener with `ClientCertificateRequired`, a client with a certificate that closes right after
  `ConnectAsync`, or a listener disposed right after it; 7 of 12 runs of up to 200 connections crashed, the same loop without
  client certificates never did). The Netty.NET code cannot
  prevent a peer's close; the mutual authentication tests wait for the server's `channelActive` before they close.
- A server QuicChannel does not learn the client's stream limits (see the [probe](#stage-3-probe)): its
  `PeerAllowedStreams` stays 0 and a `CreateStreamAsync` beyond the client's limit waits for capacity instead of failing
  with `STREAM_LIMIT_ERROR`.
- Stream writability follows the write buffer water marks, not the peer's flow control credit, and a stream write
  completes once System.Net.Quic took the data, which the peer's QuicStream buffers ahead of its application.

## Test results

Stage 2: see [codec-classes-quic-porting.md](codec-classes-quic-porting.md#test-results-stage-2). Stage 3, Windows 11
(10.0.26300), .NET 10.0.11, 2026-11-27:

- `dotnet build -p:BuildProjectReferences=false --no-incremental` of `src/Netty.NET.Handler.Codec.Quic`,
  `test/Netty.NET.Handler.Codec.Quic.Tests` and `test/Netty.NET.Handler.Codec.Quic.Examples`: 0 errors, 0 warnings each.
- `dotnet test test/Netty.NET.Handler.Codec.Quic.Tests --no-build`: 181 passed, 0 failed, 0 skipped in each of five
  consecutive runs of the whole project (12-13 s each); no case failed or was flaky in these runs. The 117 test cases
  of stage 3 are the unit tests `FlushStrategyTest` (2), `InsecureQuicTokenHandlerTest` (4), `NoQuicTokenHandlerTest`
  (3), `QuicCodecBuilderTest` (1), `QuicConnectionAddressTest` (3), `QuicConnectionIdGeneratorTest` (4),
  `QuicStreamIdGeneratorTest` (2), `QuicTest` (3) and `SipHashTest` (65: the 64 reference rows and `testHash`), and on
  loopback `QuicReadableTest` (2), `QuicStreamChannelCloseTest` (24) and `QuicStreamLimitTest` (4), each network test
  with both executor rows; the 64 cases of stage 2 still pass.
- 2026-11-27: with `QuicEndpointChannelTest` (2 cases, [endpoint shutdown hook](codec-classes-quic-porting.md#endpoint-closes-with-its-event-loop-2026-11-27)),
  183 passed, 0 failed, 0 skipped.
- Before the [fixes](#fixes-in-stage-3), the client methods of `QuicStreamLimitTest` timed out waiting for the first
  `QuicStreamLimitChangedEvent` and `testWriteToBidirectionalAfterShutdownOutput` failed with `NotSupportedException`;
  the stage-2 `MsQuicConfig` rejected the `activeMigration(true)` that `QuicServerExample` sets.
- The checks of the manifests (without `-AllowPending`) print `codec-native-quic manifest verified: 56 pinned files (15
  clr-adapted, 23 jvm-only-replaced, 18 verified), 64 mapped original tests, 48 CLR-inapplicable and 5 not-ported
  original tests, 0 deprecated-member, 0 JVM-only-API and 0 library-not-ported comment dispositions; 0
  deprecated-not-ported, 23 JVM-only-replaced and 0 library-not-ported entries carry 178 original comments.` and
  `codec-classes-quic manifest verified: 108 pinned files (53 clr-adapted, 54 jvm-only-replaced, 1 verified)`.
- `QuicServerExample` with `QuicClientExample` on loopback: three runs, each printing `Hello World!` on the client and
  ending with exit code 0 on both sides ([Examples](#examples)).
