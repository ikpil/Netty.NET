# testsuite-http2 porting

Netty.NET ports `netty-testsuite-http2` of Netty 4.2.19.Final (pinned commit
`64cc10f38ea5f5bd7eae48507817c66680d0afdc`) to `test/Netty.NET.Testsuite.Http2`. The
[manifest](testsuite-http2-porting-manifest.json) records every pinned file of `testsuite-http2/src` (6 files) with its
C# counterpart, its CLR adaptations and evidence, or the reason it is not ported. Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module testsuite-http2
```

| Status | Files |
| --- | --- |
| `clr-adapted` | 5 (the server, its initializer, the HTTP/1.1 and HTTP/2 handlers and the handler builder) |
| `jvm-only-replaced` | 1 (`package-info.java`) |

The module declares no JUnit test. Its Maven build starts `Http2Server` and runs [h2spec](https://github.com/summerwind/h2spec)
against it (`h2spec-maven-plugin` in `pom.xml`, see [Running h2spec](#running-h2spec)). On 2026-11-26 h2spec ran
against the C# server. With h2spec 2.2.0, the version of the Java build, every failed case is one that the pom's
`excludeSpecs` accepts. h2spec 2.6.0 also fails `3.5 - Sends invalid connection preface`, which it checks more strictly.
See [h2spec runs of 2026-11-26](#h2spec-runs-of-2026-11-26). The port adds 5 CLR supplemental cases in
`test/Netty.NET.Testsuite.Http2.Tests` (see [Smoke tests](#smoke-tests)); they are not a conformance run.

## Namespaces and assembly

The project, folder and assembly are `Netty.NET.Testsuite.Http2`, the namespace root of `io.netty.testsuite.http2` (the
2026-11-19 rule, see [codec-base-porting.md](codec-base-porting.md); no new name choice); the smoke tests use
`Netty.NET.Testsuite.Http2.Tests`. The project references `Netty.NET.Handler.Codec.Http2`, which brings the other
modules the pom lists (common, buffer, transport, handler and codec-http); `InternalsVisibleTo` opens internals only to
the test assembly.

The project is an executable (`OutputType` `Exe`) under `test/`: the Maven module is a runnable h2spec target and is
not deployed (`skipDeploy` in `pom.xml`), so it is neither a library under `src/` nor an xUnit project. Its entry point
is `Http2Server.Main`.

| Java package | C# namespace | Original package summary |
| --- | --- | --- |
| `io.netty.testsuite.http2` | `Netty.NET.Testsuite.Http2` | This package is intended to test the http2 implementation against the specification using h2spec |

## CLR adaptations

- **Program shape.** `Http2Server.run()` and `main(String[])` block on `bind(port).sync()` and
  `closeFuture().sync()`; they are `RunAsync()` and the async `Main`, which await the `Task` of
  `ServerBootstrap.BindAsync` and the channel's `CloseFuture`. `group.shutdownGracefully()` is
  `ShutdownGracefullyAsync()`, whose `Task` is discarded as Java discards the future. `Integer.parseInt` is `int.Parse`
  with an optional sign and the invariant culture, as codec-http's `HttpResponseDecoder` maps it.
- **Futures and promises.** `ctx.write(msg)` and the encoder's `writeHeaders`/`writeData` return `Task`s that are
  discarded, as Java discards the futures; `ctx.newPromise()` is a new `TaskCompletionSource`, as codec-http2's internal
  `Http2CodecUtil.NewPromise()`. `ChannelFutureListener.CLOSE` on the HTTP/1.1 response write is a `Task` continuation
  that closes the channel on its event loop through Common's `EventExecutorTaskScheduler`, as codec-http's
  `CorsHandler` maps it.
- **Strings passed as CharSequence.** `request.headers().get(HOST)` returns a `String` that Java keeps as a
  `CharSequence`, and `request.uri()` is passed as one; both are wrapped in Common's `StringCharSequence`
  ([codec-http-porting.md](codec-http-porting.md), Shared CLR adaptations).
- **Accessibility.** Package-private constructors, `run()` and `RESPONSE_BYTES` are `internal`. The public
  `initChannel(SocketChannel)` widens the protected `ChannelInitializer.initChannel` and the public `build()` the
  protected `AbstractHttp2ConnectionHandlerBuilder.build()`; a C# override cannot change the accessibility of the
  virtual method, so `InitChannel` stays protected and `Build()` is a public member declared `new` that calls base, as
  codec-http2's `Http2ConnectionHandlerBuilder`.
- **Anonymous classes.** The anonymous `UpgradeCodecFactory` is the nested `CleartextUpgradeCodecFactory`, and the
  anonymous `SimpleChannelInboundHandler<HttpMessage>` is the nested `NoUpgradeAttemptedHandler`, which takes
  `maxHttpContentLength` from the initializer.
- **Console output.** `System.out.println`/`System.err.println` are `Console.WriteLine`/`Console.Error.WriteLine`, and
  `cause.printStackTrace()` writes the exception to `Console.Error`, as codec-http2's `LastInboundHandler`.

## Not ported

| Unit | Reason |
| --- | --- |
| `package-info.java` | Documentation-only package descriptor; the summary is kept in the manifest and the namespace table above. |

## Running h2spec

Start the server (the port argument defaults to 9000, as in Java):

```
dotnet run --project test/Netty.NET.Testsuite.Http2 -- 9000
```

and run h2spec against it, for example `h2spec -h 127.0.0.1 -p 9000`. h2spec is an external binary that is not in this
repository. The Java build runs it through `h2spec-maven-plugin` 0.6 (`com.github.madgnome`, version in the parent
`pom.xml`), which bundles h2spec 2.2.0, runs `h2spec -p <port> -j <report> -o <timeout> --max-header-length <n>` and
fails the build on a failed case whose `<section> - <description>` is not listed. One case runs alone when its id is
given, as in `h2spec -h 127.0.0.1 -p 9000 http2/3.5/2`. The `h2spec-maven-plugin` configuration in `pom.xml` lists the
cases whose failure the Java build accepts (`excludeSpecs`). The `duplicated ":method"` line appears twice in the pom
because h2spec 2.2.0 gives two 8.1.2.3 cases (5 and 7) that description; in 2.6.0 the second one reads
`duplicated ":path"`.

- 4.2 - Sends a dynamic table size update at the end of header block
- 5.1 - idle: Sends a DATA frame
- 5.1 - half closed (remote): Sends a HEADERS frame
- 5.1 - closed: Sends a HEADERS frame
- 5.1.1 - Sends stream identifier that is numerically smaller than previous
- 8.1.2.2 - Sends a HEADERS frame that contains the connection-specific header field
- 8.1.2.2 - Sends a HEADERS frame that contains the TE header field with any value other than "trailers"
- 8.1.2.3 - Sends a HEADERS frame with empty ":path" pseudo-header field
- 8.1.2.3 - Sends a HEADERS frame that omits ":method" pseudo-header field
- 8.1.2.3 - Sends a HEADERS frame that omits ":scheme" pseudo-header field
- 8.1.2.3 - Sends a HEADERS frame that omits ":path" pseudo-header field
- 8.1.2.3 - Sends a HEADERS frame with duplicated ":method" pseudo-header field
- 8.1.2.3 - Sends a HEADERS frame with duplicated ":scheme" pseudo-header field
- 8.1.2.6 - Sends a HEADERS frame with the "content-length" header field which does not equal the DATA frame payload length
- 8.1.2.6 - Sends a HEADERS frame with the "content-length" header field which does not equal the sum of the multiple DATA frames payload length

### h2spec runs of 2026-11-26

Two h2spec versions ran against the Debug build of `Http2Server`: 2.2.0, the version of the Java build, and 2.6.0, the
latest release. Each binary is the `h2spec_windows_amd64.zip` of the official GitHub release; the releases publish no
checksum. Both ran on port 9000 as `h2spec -h 127.0.0.1 -p 9000 -o 3 -j <report>`, with net10.0 on Windows
10.0.26300. h2spec reports each failed case as a JUnit `error`.

| h2spec | `h2spec --version` | SHA-256 of the downloaded zip | Cases |
| --- | --- | --- | --- |
| 2.2.0 | `2.2.0 (85d0156653e729584ef463e32f647007f4ecc92d)` | `250fd054a1e57cf20318b9cf982a7d772c32484f764f20b1e3dc5d0a1e628a95` | 145 |
| 2.6.0 | `2.6.0 (70ac2294010887f48b18e2d64f5cccd48421fad1)` | `57b608578bd9c8c3b6b821c2ab64925a6caee16185b356fff611a7a86fe8e697` | 146 |

The case lists differ in one case: 2.6.0 adds `hpack/2.3.3/2`. They also differ in the descriptions of `5.3.1/1`,
`8.1.2.3/7` and `hpack/2.3.3/1`.

The first full run with h2spec 2.2.0 did not end (see below), so 2.2.0 also ran each case alone. Each case ran by its
id with a 20-second limit, three times over the whole list. Two more full runs followed.

| h2spec 2.2.0 run | Passed | Failed | Did not end |
| --- | --- | --- | --- |
| Full run 1 | – | – | stopped in `4.3/2` and killed |
| Case by case 1 | 139 | 6 | 0 |
| Case by case 2 | 139 | 5 | 1 (`6.10/6`) |
| Case by case 3 | 139 | 5 | 1 (`6.10/6`) |
| Full run 2 | 140 | 5 | 0 |
| Full run 3 | 140 | 5 | 0 |

| h2spec 2.6.0 run | Passed | Failed |
| --- | --- | --- |
| Full run 1 | 139 | 7 |
| Full run 2 | 140 | 6 |
| Full run 3 | 139 | 7 |

- **The pom's `excludeSpecs` cover every h2spec 2.2.0 failure.** These five cases failed in every finished run of both
  versions, and the Java build accepts all of them:
  - `5.1 - closed: Sends a HEADERS frame` and `5.1.1 - Sends stream identifier that is numerically smaller than
    previous`: the server sends RST_STREAM where h2spec expects a GOAWAY.
  - The three `8.1.2.3` cases that omit `:method`, `:scheme` or `:path`: the server serves the request.

  `5.1 - half closed (remote): Sends a HEADERS frame` (the response HEADERS arrive where h2spec expects
  STREAM_CLOSED) failed in case-by-case run 1 of 2.2.0 and in runs 1 and 3 of 2.6.0, and passed in the other finished
  runs. It is also in `excludeSpecs`. With h2spec 2.2.0, `3.5 - Sends invalid connection preface` passed in every run.
- **Accepted failures that pass.** The other 9 `excludeSpecs` entries passed in every finished run of both versions.
  The `4.2` entry is the `hpack/4.2` case. With 2.2.0, both `duplicated ":method"` cases (5 and 7) passed.
- **h2spec 2.2.0 runs that do not end on Windows.** `4.3/2` and `6.10/6` send frames that are a connection error. The
  server answers with GOAWAY and closes the connection while h2spec is still sending (in `6.10/6`, a 3512-byte
  CONTINUATION), and the close resets the connection.
  - On Windows the next read fails with `wsarecv: An existing connection was forcibly closed by the remote host`.
    h2spec 2.2.0 ends a check only on EOF, `ECONNRESET` or a timeout. On this error it reads the connection again and
    again: with `-v`, `6.10/6` logged that error 561,968 times in 5 seconds and kept one CPU core busy.
  - In the h2spec sources, 2.6.0 ends a check on any read error and treats `WSAECONNRESET` and `WSAECONNABORTED` as a
    closed connection, so its runs end.
  - Whether the reset arrives before h2spec reads the GOAWAY depends on timing, so the same case ends in some runs and
    not in others.
- **h2spec 2.6.0 fails one case outside `excludeSpecs`.** `3.5 - Sends invalid connection preface` failed in every
  run with `Error: unexpected EOF`, for this reason:
  - `CleartextHttp2ServerUpgradeHandler` finds no HTTP/2 preface in `INVALID CONNECTION PREFACE\r\n\r\n`, so the bytes
    reach the HTTP/1.1 codec. `PREFACE` is not an HTTP version, so the decoder emits its invalid `HTTP/1.0`
    `/bad-request` message; the server printed `Directly talking: HTTP/1.0 (no upgrade was attempted)`.
  - `HelloWorldHttp1Handler` answers that message with `HTTP/1.1 200 OK` and closes the connection, because an HTTP/1.0
    request is not keep-alive. The Java handlers take the same path.
  - h2spec reads the first 9 bytes of that response as a frame header, and the length it reads exceeds the bytes that
    follow, so the read ends in `unexpected EOF`.
  - In the h2spec sources, 2.2.0 checks this case with `VerifyConnectionClose` and keeps reading after this read
    error, so the EOF that follows passes the case. 2.6.0 checks it with `VerifyConnectionError(PROTOCOL_ERROR)`, where
    any read error ends the check.
  - The failure therefore comes from the stricter h2spec, not from the port: h2spec 2.2.0 passes the case.
- **Server output.** `exceptionCaught` printed these exceptions:
  - `SocketException`s 10053 and 10054, as h2spec aborts or resets connections: 126 over the three 2.6.0 runs, and 247
    over the 2.2.0 runs.
  - `ObjectDisposedException`s from `Socket.Receive`: 6 over the 2.6.0 runs, and 12 over the 2.2.0 runs. In each, a
    handler had closed the channel inside the read loop and the loop read again. The Java read loop is the same, and
    there that read throws `ClosedChannelException`.

## Smoke tests

`Http2ServerSmokeTest` (CLR supplemental) binds `Http2ServerInitializer` on a loopback port and takes each path through
the pipeline once, with the BCL `HttpClient` or a raw socket that writes HTTP/2 frames:

| Case | Path |
| --- | --- |
| `Http1WithoutUpgradeIsAnsweredByHelloWorldHttp1Handler` | No upgrade attempted: `NoUpgradeAttemptedHandler` installs `HttpObjectAggregator` and `HelloWorldHttp1Handler`; keep-alive response. |
| `Http1ConnectionCloseClosesTheChannelAfterTheResponse` | `Connection: close`: the `ChannelFutureListener.CLOSE` continuation closes the channel after the response. |
| `CleartextUpgradeIsAnsweredOnStreamOneOverHttp2` | `Upgrade: h2c`: 101, then `HelloWorldHttp2Handler` answers the upgrade event on stream 1 (`:status 200` and the body). |
| `PriorKnowledgeGetIsAnsweredOverHttp2` | Prior knowledge (`HttpClient` with HTTP/2 and `RequestVersionExact`): `onHeadersRead` with end of stream. |
| `PriorKnowledgeRequestBodyIsEchoedByOnDataRead` | Prior knowledge with a request body: `onDataRead` echoes the last DATA frame. |

On 2026-11-26 the 5 cases passed in each of 3 runs.
