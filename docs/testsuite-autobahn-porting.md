# testsuite-autobahn porting

Netty.NET ports `netty-testsuite-autobahn` of Netty 4.2.19.Final (pinned commit
`64cc10f38ea5f5bd7eae48507817c66680d0afdc`) to `test/Netty.NET.Testsuite.Autobahn`. The
[manifest](testsuite-autobahn-porting-manifest.json) records every pinned file of `testsuite-autobahn/src` (4 files) with
its C# counterpart, its CLR adaptations and evidence, or the reason it is not ported. Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module testsuite-autobahn
```

| Status | Files |
| --- | --- |
| `clr-adapted` | 3 (the server, its initializer and the WebSocket handler) |
| `jvm-only-replaced` | 1 (`package-info.java`) |

The module declares no JUnit test. Its Maven build starts `AutobahnServer` and runs the
[Autobahn|Testsuite](https://github.com/crossbario/autobahn-testsuite) fuzzing client against it
(`autobahntestsuite-maven-plugin` in `pom.xml`, see [Running the Autobahn fuzzing client](#running-the-autobahn-fuzzing-client)).
The port adds 6 CLR supplemental cases in `test/Netty.NET.Testsuite.Autobahn.Tests` (see [Smoke tests](#smoke-tests));
they are not a conformance run.

## Namespaces and assembly

The project, folder and assembly are `Netty.NET.Testsuite.Autobahn`, the namespace root of
`io.netty.testsuite.autobahn` (the 2026-11-19 rule, see [codec-base-porting.md](codec-base-porting.md); no new name
choice); the smoke tests use `Netty.NET.Testsuite.Autobahn.Tests`. The project references
`Netty.NET.Handler.Codec.Http`, which brings the other modules the pom lists (common, buffer and transport);
`InternalsVisibleTo` opens internals only to the test assembly.

The project is an executable (`OutputType` `Exe`) under `test/`: the Maven module is a runnable fuzzing-client target
and is not deployed (`skipDeploy` in `pom.xml`), so it is neither a library under `src/` nor an xUnit project. Its entry
point is `AutobahnServer.Main`.

| Java package | C# namespace | Original package documentation |
| --- | --- | --- |
| `io.netty.testsuite.autobahn` | `Netty.NET.Testsuite.Autobahn` | For use with the Python AutoBahn test suite; steps 01-10 install it on Ubuntu, write `fuzzing_clinet_spec.json`, run `AutobahnServer` and `wstest -m fuzzingclient` and open the report (kept verbatim in the manifest; restated for the C# server below). |

## CLR adaptations

- **Program shape.** `AutobahnServer.run()` and `main(String[])` block on `bind(port).sync()` and
  `f.channel().closeFuture().sync()`; they are `RunAsync()` and the async `Main`, which await the `Task<IChannel>` of
  `ServerBootstrap.BindAsync` (the `ChannelFuture f`; `f.channel()` is its result) and the channel's `CloseFuture`.
  `group.shutdownGracefully()` is `ShutdownGracefullyAsync()`, whose `Task` is discarded as Java discards the future.
  `PooledByteBufAllocator.DEFAULT` is `PooledByteBufAllocator.Default`. `Integer.parseInt` is `int.Parse` with an
  optional sign and the invariant culture, as codec-http's `HttpResponseDecoder` maps it.
- **Logging.** `java.util.logging` (`Logger.getLogger`, `isLoggable(Level.FINE)`, `fine`) has no CLR counterpart; the
  handler uses Common's `InternalLoggerFactory` logger, and `Level.FINE` is its debug level, as Netty's own `JdkLogger`
  maps `isDebugEnabled()` and `debug()` to `Level.FINE`. `String.format` is `string.Format` with the invariant culture;
  `ctx.channel().hashCode()` is `GetHashCode()` and `frame.getClass().getName()` the type's `FullName`.
- **Futures.** `handshake`/`close` are `HandshakeAsync`/`CloseAsync` and `sendUnsupportedVersionResponse` is
  `SendUnsupportedVersionResponseAsync`; their `Task`s and those of `ctx.write` are discarded, as Java discards the
  futures. `ChannelFutureListener.CLOSE` on the `writeAndFlush` future of `ctx.channel()` is a `Task` continuation that
  closes that channel on its event loop through Common's `EventExecutorTaskScheduler`, as codec-http's `CorsHandler`
  maps it.
- **Exceptions and text.** `IllegalStateException` is `InvalidOperationException` and `UnsupportedOperationException`
  `NotSupportedException` ([codec-base-porting.md](codec-base-porting.md)). `CharsetUtil.UTF_8` is `Encoding.UTF8`.
  Java string concatenation writes a missing `Host` header as `"null"`, which `getWebSocketLocation` keeps.
- **Accessibility.** The public `initChannel(SocketChannel)` widens the protected `ChannelInitializer.initChannel`; a C#
  override cannot change the accessibility of the virtual method, so it stays protected.

## Not ported

| Unit | Reason |
| --- | --- |
| `package-info.java` | Documentation-only package descriptor; its text is kept in the manifest and the namespace table above. |

## Running the Autobahn fuzzing client

Start the server (the port argument defaults to 9000, as in Java):

```
dotnet run --project test/Netty.NET.Testsuite.Autobahn -- 9000
```

It prints `Web Socket Server started at port 9000`. Then run the fuzzing client of Autobahn|Testsuite with a spec
like the one in the package documentation (`"servers": [{"agent": "Netty4", "url": "ws://localhost:9000", "options":
{"version": 18}}]`, `"cases": ["*"]`) as `wstest -m fuzzingclient -s <spec>.json`, and open
`reports/servers/index.html`. The package documentation installs `wstest` with Python 2 on Ubuntu; the project also
publishes a Docker image. The `autobahntestsuite-maven-plugin` configuration in `pom.xml` runs every case (`*`), excludes
none and sets `failOnNonStrict` to false. Autobahn|Testsuite is an external tool that is not in this repository, and
the fuzzing run has not been made with the C# server yet.

## Smoke tests

`AutobahnServerSmokeTest` (CLR supplemental) binds `AutobahnServerInitializer` on a loopback port, with the child
allocator `AutobahnServer` sets, and takes each branch of `AutobahnServerHandler` once with the BCL `ClientWebSocket`
or a raw request:

| Case | Branch |
| --- | --- |
| `TextAndBinaryMessagesAreEchoed` | Handshake, then `TextWebSocketFrame` and `BinaryWebSocketFrame` are written back. |
| `FragmentedMessageIsEchoedThroughContinuationFrames` | A text frame without FIN and its `ContinuationWebSocketFrame` are written back. |
| `PingIsAnsweredWithPong` | `PingWebSocketFrame` is answered with a `PongWebSocketFrame`; the client's `KeepAliveTimeout` aborts the connection when no pong arrives (the case fails when the handler drops pings). |
| `CloseFrameIsEchoedByTheHandshaker` | `CloseWebSocketFrame` goes to `handshaker.close`, which echoes the status and reason. |
| `NonGetRequestIsForbiddenAndClosed` | A POST gets `403 Forbidden` with the status text as its body, and the channel closes. |
| `UnsupportedWebSocketVersionIsAnsweredWithUpgradeRequired` | `Sec-WebSocket-Version: 99`: no handshaker, `426 Upgrade Required` with `Sec-WebSocket-Version: 13`. |

On 2026-11-26 the 6 cases passed in each of 3 runs.
