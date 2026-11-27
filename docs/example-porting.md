# Netty example: porting notes

Netty 4.2.19.Final (pinned commit `64cc10f38ea5f5bd7eae48507817c66680d0afdc`) ships its runnable examples as the
`example` Maven module: 208 Java sources in 31 packages under `io.netty.example` and 210 resources. They are ported
as one executable, `src/Netty.NET.Example` (namespace root `Netty.NET.Example`), recorded file by file in
[example-porting-manifest.json](example-porting-manifest.json) and checked by `tools/Test-CodecPortingManifest.ps1`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -Command "& ./tools/Test-CodecPortingManifest.ps1 -Module example"
```

The module declares no JUnit test.

## Running an example

Each Java example class keeps its `main(String[])` as `Main(string[] args)`. C# allows several `Main` methods in one
assembly only with an explicit startup object, so `ExampleLauncher` (CLR-only, the project's `StartupObject`) takes the
Java class name and the example's arguments:

```powershell
dotnet run --project src/Netty.NET.Example -- echo.EchoServer -Dport=8007
```

```powershell
dotnet run --project src/Netty.NET.Example -- io.netty.example.echo.EchoClient -Dport=8007
```

- The class name may carry the `io.netty.example.` prefix; the package and class are matched case-insensitively
  against `Netty.NET.Example.<Package>.<Class>`. Without arguments the launcher lists every example.
- `-Dname=value` (or `-Dname`, an empty value as on the JVM) becomes a process environment variable. The examples
  read Java system properties through `SystemPropertyUtil.Get`, which reads the environment in this port
  (`src/Netty.NET.Common/Internal/SystemPropertyUtil.cs`). The other arguments go to the example's `Main`.

## Namespaces

`io.netty.example` is `Netty.NET.Example` (owner decision 2026-11-19: project and namespace root are the package
root). Each package segment is PascalCased; where the package's Java type names spell the segment, their casing is
kept:

| Java package segment | C# | Java package segment | C# |
| --- | --- | --- | --- |
| `helloworld` | `HelloWorld` | `websocketx` | `WebSocketx` |
| `socksproxy` | `SocksProxy` | `portunification` | `PortUnification` |
| `localecho` | `LocalEcho` | `haproxy` | `HAProxy` |
| `ipfilter` | `IpFilter` | `securechat` | `SecureChat` |
| `heartBeat` | `HeartBeat` | `benchmarkserver` | `BenchmarkServer` |
| `websocket` | `WebSocket` | others | first letter capitalized (`echo` `Echo`, `qotm` `Qotm`, `http2` `Http2`, `dot` `Dot`) |

Segments such as `File`, `Dns` and `Proxy` shadow BCL types inside `Netty.NET.Example.*`, so the ported code
qualifies `System.IO.File`, `System.Net.Dns` and the like where it uses them. The package summaries of the
`package-info.java` files are kept verbatim in the manifest (`jvm-only-replaced`), as for the other modules.

## Shared pieces

| Java | C# | Notes |
| --- | --- | --- |
| (none) | `ExampleLauncher` | CLR-only startup object, see above. |
| `util/ServerUtil` | `Util/ServerUtil` | `System.getProperty("ssl")` is `SystemPropertyUtil.Get("ssl")`; the self-signed certificate comes from the PkiTesting port (`CertificateBuilder`, `X509Bundle.ToKeyManagerFactory()`), as in Java. |
| `resources/logback.xml` | none | `jvm-only-replaced`: the Logback configuration of the JVM examples; Netty.NET logs through `InternalLoggerFactory` and its default factory. |

## Ported examples

25 packages, 156 Java files, each `clr-adapted` with its `review.scope` in the manifest; the 206 resources they use
are byte-for-byte copies (`verified`). `dotnet build src/Netty.NET.Example` reports 0 warnings and 0 errors.

| Package | Files | Main CLR adaptations |
| --- | --- | --- |
| `discard`, `echo`, `uptime`, `localecho`, `qotm`, `telnet`, `securechat`, `factorial`, `file` | 4, 4, 4, 3, 4, 6, 6, 8, 2 | `sync()` is an awaited Task in an `async Main`; `addListener` is a continuation on the channel's event loop (`EventExecutorTaskScheduler`); `ChannelFutureListener.CLOSE` closes the channel after the write Task. `java.lang.Number` has no CLR base type, so the factorial `NumberEncoder` takes `object`; `BlockingQueue` is `BlockingCollection`. `ChannelGroup` is `DefaultChannelGroup`. |
| `http` (cors, file, helloworld, snoop, upload, websocketx) | 32 | `MimetypesFileTypeMap` (JavaBeans Activation) is a small extension table; the file-transfer progress listener is the channel's `WriteProgress` observer for that write; `java.io.File` is `FileInfo`/`DirectoryInfo`; HTTP dates use the invariant culture in GMT. |
| `http2` (file, helloworld client/server/frame/multiplex, tiles) | 34 | ALPN runs on `SslStream` (OpenSSL is library-not-ported); `Http2ExampleUtil.PlatformCiphers` returns no explicit cipher list on Windows, where `SslStream` rejects one. The tile images are embedded resources under their Java resource names. |
| `spdy`, `portunification` | 9, 2 | SPDY negotiated through ALPN on `SslStream`; port unification detects SSL, gzip, HTTP and the factorial protocol as in Java. |
| `dns`, `haproxy`, `ipfilter`, `memcache`, `mqtt`, `proxy`, `redis`, `socksproxy`, `stomp` | 4, 3, 1, 2, 4, 4, 2, 7, 10 | `await(timeout)` is `Task.WhenAny` with `Task.Delay`; `Promise<Channel>` is `TaskCompletionSource<IChannel>`; the STOMP WebSocket page reads its files next to the assembly and rejects names that leave that directory. |
| `util` | 1 | See "Shared pieces". |

The package-info files of `http`, `spdy` and `util` are `jvm-only-replaced` with their summaries kept verbatim.

### Smoke runs (2026-11-26, Windows, loopback)

Each server ran with its client (or curl) on loopback ports 18080-18139:

- HTTP: snoop (plain and TLS), hello world, CORS (GET and preflight), the static file server (listing, 200, 302, 304,
  403, 404, progress output, TLS), upload (GET, POST and multipart), both WebSocket servers with the WebSocket client.
- HTTP/2: the h2c upgrade client and the TLS/ALPN frame client against the three hello-world servers (also prior
  knowledge, and the HTTP/1.1 fallback over curl), the tiles server (HTTP/1 and HTTP/2 pages, images identical to the
  source jpegs) and the static file server.
- SPDY over ALPN, port unification (factorial, HTTP and HTTPS), telnet, secure chat (two clients,
  `TLS_AES_256_GCM_SHA384`), the file server, echo, discard, HAProxy, ipfilter, local echo, uptime (with reconnects), the
  hex-dump proxy in front of the echo server, SOCKS4 and SOCKS5 through curl, and the STOMP-over-WebSocket page with the
  full CONNECT/SUBSCRIBE/SEND/MESSAGE/RECEIPT sequence.
- DNS, QOTM and MQTT hard-code public resolvers, the broadcast address or ports 53/1883; scratch copies pointed at
  127.0.0.1 ran (TCP and UDP DNS against local responders, a quote over UDP, MQTT CONNECT and PINGREQ).
- Not run: the DNS-over-TLS client (it needs a resolver with a publicly trusted certificate), and the memcache, redis
  and STOMP clients (no local server).

In Git Bash, set `MSYS_NO_PATHCONV=1` before passing arguments such as `-Durl2=/post`, or MSYS rewrites them as paths.

### Behavior that follows the Java examples

- `FactorialClient` with its default `-Dcount=1000` stops answering: the client's `JdkZlibDecoder` throws
  `DecompressionException: Decompression buffer has reached maximum size: 65536` when one read inflates past the
  example's own limit (`MAX_CONTENT_LENGTH = 65536` in both initializers, as in Java; the decoder logic matches
  `ZlibDecoder.prepareDecompressBuffer` line for line), and `getFactorial()` waits for an answer that does not come.
  Counts up to 500 complete. The Java original could not be run on this host (no JDK), so whether it hits the limit
  with the same read sizes is not verified.
- The CORS preflight response carries `access-control-allow-methods:` and `access-control-allow-headers:` with empty
  values and a quoted `date`: `CorsHandler.handlePreflight` builds the response with
  `DefaultHttpHeadersFactory.headersFactory().withCombiningHeaders(true)`, and `CombinedHttpHeaders` joins an empty set
  to an empty value and CSV-quotes a value with a comma, as in Java.
- `TelnetClient -Dssl` builds its client context through `ServerUtil.buildSslContext()` in Java too, so the TLS
  handshake does not complete; the HTTP/2 static file server sends `:status` values with the reason phrase
  (`200 OK`), which Netty's frame client accepts and .NET's `HttpClient` rejects. Both are kept as written.
- `LoggingHandler` logs through `InternalLoggerFactory`; its default factory does not print INFO messages to the
  console, where Logback does in Java.

## Examples that are not ported

Six packages need a unit that is recorded as not ported in its own module's manifest. Their entries name that unit
as the family owner (`deprecation.family` or `library.family` with `familyManifest`), and the checker verifies the code
link (a file names the owner, or reaches it through another file of the same package, `via`).

| Package | Files | Status | Reason |
| --- | --- | --- | --- |
| `objectecho` | 4 | `deprecated-not-ported` | Java serialization through `ObjectEncoder`/`ObjectDecoder`, `@Deprecated` and deprecated-not-ported in codec-base. The echo examples show the same pipeline with bytes. |
| `rxtx` | 2 | `deprecated-not-ported` | The RXTX serial transport is `@Deprecated` (docs/record-only-modules.md). |
| `udt` | 23 | `deprecated-not-ported` (17), `jvm-only-replaced` (6 `package-info.java`) | The UDT transport is `@Deprecated` and needs the barchart UDT JNI library. |
| `sctp` | 6 | `library-not-ported` | transport-sctp needs the JDK SCTP API (`com.sun.nio.sctp`); an SCTP socket cannot be created on Windows. |
| `ocsp` | 5 + 2 resources | `library-not-ported`, resources `jvm-only-replaced` | OCSP requests and responses are built and parsed with Bouncy Castle, and the server staples through OpenSSL (netty-tcnative). `Netty.NET.Handler.Ssl.Ocsp` validates OCSP responses on the client side (docs/handler-ssl-ocsp-porting.md). |
| `worldclock` | 7 + `WorldClockProtocol.proto` | `library-not-ported`, schema `jvm-only-replaced` | `WorldClockProtocol` is protoc-generated code on protobuf-java (it names `com.google.protobuf` types fully qualified, without imports), and the pipelines use the library-not-ported protobuf codecs. |

The checker gained four rules for these records (2026-11-26): a deprecated or library family may be recorded in
another module's manifest (`familyManifest`); a library family may be reached through another unit of the same package
(`library.via`, as `deprecation.family.via`), and a `via` link may run in either direction (`MsgEchoPeerOne` extends
`MsgEchoPeerBase`); a library is also recognized by fully qualified type names in code; and a `.proto` schema is
recorded with its comments like Java.
