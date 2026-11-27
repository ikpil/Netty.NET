# codec-classes-quic porting

Netty.NET ports `netty-codec-classes-quic` of Netty 4.2.19.Final (pinned commit
`64cc10f38ea5f5bd7eae48507817c66680d0afdc`) to `src/Netty.NET.Handler.Codec.Quic`, with its original test in
`test/Netty.NET.Handler.Codec.Quic.Tests`: the API layer (stage 1) and its implementation on System.Net.Quic (msquic,
stage 2). The design and the owner decisions of 2026-11-27 are in [quic-design.md](quic-design.md); the QUIC tests of
`codec-native-quic` that run on the implementation are recorded in [codec-native-quic-porting.md](codec-native-quic-porting.md).
The [manifest](codec-classes-quic-porting-manifest.json) records every pinned file of `codec-classes-quic/src` (108
files: 106 main Java sources, `module-info.yml` and 1 test) with its C# counterpart and CLR adaptations or the reason it
is not ported; no entry is pending. Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "& ./tools/Test-CodecPortingManifest.ps1 -Module codec-classes-quic"
```

| Status | Files |
| --- | --- |
| `clr-adapted` | 53 (main sources: the API layer and the stage-2 units `QuicChannelBootstrap`, `QuicCodecBuilder`, `QuicServerCodecBuilder`, `QuicClientCodecBuilder`, `QuicSslContextBuilder`, `FlushStrategy`) |
| `verified` | 1 (`QuicHeaderParserTest`, both original tests mapped) |
| `jvm-only-replaced` | 54 (45 quiche/BoringSSL JNI units, the 7 stage-2 units of the datagram/BoringSSL machinery msquic owns, `module-info.yml`, `package-info.java`) |

## Staging

The port was staged (owner decisions of 2026-11-27, [quic-design.md](quic-design.md#plan)):

1. **Stage 1: the API layer.** The interfaces, value, event and exception types, the helpers that need no quiche,
   `QuicStreamChannelBootstrap` and the TLS abstractions `QuicSslContext`/`QuicSslEngine`.
2. **Stage 2: the msquic implementation** on `System.Net.Quic` (`QuicListener`, `QuicConnection`, `QuicStream`), described
   in [Stage 2](#stage-2-the-msquic-implementation). Its 13 units were ported or recorded:

   | Unit | Stage 2 |
   | --- | --- |
   | `QuicChannelBootstrap` | `clr-adapted`: connects a QuicChannel over `QuicConnection.ConnectAsync`; adds the static `IQuicChannel.NewBootstrap(IChannel)` |
   | `QuicCodecBuilder` | `clr-adapted`: the settings become `QuicConnectionOptions` (`MsQuicConfig`) |
   | `QuicClientCodecBuilder` | `clr-adapted`: builds `MsQuicClientCodec` (`QuicClientConnectionOptions`) |
   | `QuicServerCodecBuilder` | `clr-adapted`: builds `MsQuicServerCodec` (`QuicListenerOptions`/`QuicServerConnectionOptions`) |
   | `QuicSslContextBuilder` | `clr-adapted`: builds `MsQuicSslContext`, the factory of the authentication options |
   | `FlushStrategy` | `clr-adapted` (`IFlushStrategy`): the builders take it; only `DEFAULT` is supported |
   | `QuicCodecDispatcher` | `jvm-only-replaced`: several codecs on one UDP socket; msquic owns the socket |
   | `QuicClientSessionCache` | `jvm-only-replaced`: BoringSSL client session cache; msquic resumes sessions itself |
   | `DirectIoByteBufAllocator` | `jvm-only-replaced`: direct buffers for quiche; System.Net.Quic takes managed memory |
   | `SegmentedDatagramPacketAllocator` | `jvm-only-replaced`: GSO datagrams of the codec; msquic writes the datagrams (`QuicChannelOption.SEGMENTED_DATAGRAM_PACKET_ALLOCATOR` has no counterpart) |
   | `EpollQuicUtils` | `jvm-only-replaced`: epoll GSO helper (`transport-classes-epoll` is recorded, not ported) |
   | `ConnectionIdChannelMap` | `jvm-only-replaced`: connection ID routing of the codec; msquic routes packets |
   | `GroupsConverter` | `jvm-only-replaced`: TLS group names for BoringSSL; System.Net.Quic has no group selection |

3. **Not ported: quiche and BoringSSL** (JNI), recorded `jvm-only-replaced` ([JNI inventory](#jni-inventory)).

## Namespace and assembly

The project, folder, assembly and namespace are `Netty.NET.Handler.Codec.Quic`, the namespace of the module's package
`io.netty.handler.codec.quic` (owner decision 2026-11-19, [codec-base-porting.md](codec-base-porting.md)); the package
summary of `package-info.java` ("QUIC implementation") maps to this namespace. The tests use
`Netty.NET.Handler.Codec.Quic.Tests`. The project references `Netty.NET.Handler` (which brings codec-base, Transport,
Resolver, Buffer and Common), the CLR counterpart of `module-info.yml`; `System.Net.Quic` is part of the shared
framework, so no package is added. `InternalsVisibleTo` opens the package-private members (ported `internal`) to
`Netty.NET.Handler.Codec.Quic.Tests`, `Netty.NET.Handler.Codec.Http3` and `Netty.NET.Handler.Codec.Http3.Tests`.

The class `Quic` keeps its Java name inside the namespace of the same last segment. Within `Netty.NET.Handler.Codec.*`
namespaces other than this one (codec-http3 is `Netty.NET.Handler.Codec.Http3`), the simple name `Quic` finds the
namespace `Netty.NET.Handler.Codec.Quic` before any imported type, so code there writes `Codec.Quic.Quic` or uses an
alias (`using QuicUtil = Netty.NET.Handler.Codec.Quic.Quic;`). Code in other namespaces that imports
`Netty.NET.Handler.Codec.Quic` reaches it as `Quic`.

## API shapes for stage 2

The channel interfaces follow the Transport conventions ([transport-porting.md](transport-porting.md#decisions)): a
returned `Task` replaces `ChannelFuture`/`Future`, the `ChannelPromise`/`Promise` overloads fold with their default
overloads into one member, Java interfaces take the `I` prefix and default methods are default interface members.

`IQuicChannel : IChannel` (stage 2 implements it on `AbstractChannel` over a `QuicConnection`):

| Java | C# |
| --- | --- |
| `sslEngine()` | `QuicSslEngine SslEngine()` |
| `peerAllowedStreams(type)` | `long PeerAllowedStreams(QuicStreamType type)` |
| `isTimedOut()` | `bool IsTimedOut()` |
| `peerTransportParameters()` | `IQuicTransportParameters PeerTransportParameters()` |
| `localSocketAddress()`, `remoteSocketAddress()` | `EndPoint LocalSocketAddress()`, `EndPoint RemoteSocketAddress()` |
| `createStream(type, handler[, promise])` | `Task<IQuicStreamChannel> CreateStreamAsync(QuicStreamType, IChannelHandler, CancellationToken = default)` |
| `newStreamBootstrap()` | default member `QuicStreamChannelBootstrap NewStreamBootstrap()` |
| `close(applicationClose, error, reason[, promise])` | `Task CloseAsync(bool applicationClose, int error, ByteBuf reason, CancellationToken = default)` |
| `collectStats([promise])` | `Task<IQuicConnectionStats> CollectStatsAsync(CancellationToken = default)` |
| `collectPathStats(pathIdx[, promise])` | `Task<IQuicConnectionPathStats> CollectPathStatsAsync(int pathIdx, CancellationToken = default)` |
| `localAddress()`, `remoteAddress()` (narrowed to `QuicConnectionAddress`) | `IChannel.LocalAddress`/`RemoteAddress`, whose values are `QuicConnectionAddress` or null |
| `config()` (narrowed to `QuicChannelConfig`) | `IChannel.Config`, whose value is an `IQuicChannelConfig` |
| `static newBootstrap(Channel)` | `static QuicChannelBootstrap NewBootstrap(IChannel channel)` (a static interface method, added in stage 2) |

`IQuicStreamChannel : IDuplexChannel` (stage 2 implements it over a `QuicStream`):

| Java | C# |
| --- | --- |
| `shutdown(error[, promise])` | `Task ShutdownAsync(int error)` |
| `shutdownInput(error[, promise])` | `Task ShutdownInputAsync(int error)`; the default `IDuplexChannel.ShutdownInputAsync()` calls it with 0 |
| `shutdownOutput(error[, promise])` | `Task ShutdownOutputAsync(int error)` |
| `isLocalCreated()`, `type()`, `streamId()`, `priority()` | `bool IsLocalCreated()`, `QuicStreamType Type()`, `long StreamId()`, `QuicStreamPriority Priority()` |
| `updatePriority(priority[, promise])` | `Task UpdatePriorityAsync(QuicStreamPriority, CancellationToken = default)` |
| `parent()`, `localAddress()`, `remoteAddress()`, `config()` | inherited: `Parent` (an `IQuicChannel`), `LocalAddress`/`RemoteAddress` (`QuicStreamAddress`), `Config` (`IQuicStreamChannelConfig`) |
| `SHUTDOWN_OUTPUT` (a `ChannelFutureListener`) | no counterpart: continue the write `Task` with `ShutdownOutputAsync()` |

Both interfaces give default implementations of the `IChannelOutboundInvoker` members that delegate to the pipeline (the
Java defaults of `bind`, `connect`, `disconnect`, `close`, `deregister`, `write`, `writeAndFlush`); a class's own
members, such as `AbstractChannel`'s, take precedence. The `newPromise()`-style defaults have no Task counterpart.
`read()`, `flush()`, `config()`, `parent()`, `localAddress()` and `remoteAddress()` are re-declared in Java only to
narrow their return types and are inherited unchanged (the `ISocketChannel` rule: a C# interface cannot narrow an
inherited member without hiding it), so callers cast; their Javadoc stays in the C# file with a `// CLR:` note.
`IQuicChannelConfig : IChannelConfig` has no members of its own and `IQuicStreamChannelConfig : IDuplexChannelConfig`
adds `SetReadFrames(bool)`/`IsReadFrames()`; `QuicChannelOption.READ_FRAMES` and `QLOG` are on a static class, as
codec-http2's `Http2StreamChannelOption`.

## CLR decisions

### Availability (`Quic`)

QUIC is System.Net.Quic on msquic (owner decision 2026-11-27). `Quic.IsAvailable()` is true when the platform is
Windows or Linux (the owner's platforms; the guard also satisfies the CA1416 platform analyzer for the
`[SupportedOSPlatform]` types) and `QuicListener.IsSupported && QuicConnection.IsSupported`, which needs msquic (Windows
11 / Windows Server 2022, the `libmsquic` package on Linux). Otherwise `UnavailabilityCause()` is a
`PlatformNotSupportedException`, and `EnsureAvailability()` throws a `PlatformNotSupportedException` with that cause
(Java: an `UnsatisfiedLinkError` whose cause is the load failure). `IsVersionSupported(version)` asks quiche in Java;
System.Net.Quic does not let the application choose the version, so it is `IsAvailable() && version == 1` (QUIC version
1, RFC 9000 section 15). `InsecureQuicTokenHandler` and `QuicConnectionAddress` still call `EnsureAvailability()` in
their constructors, as in Java, so they cannot be created where QUIC is unavailable. On this host (Windows 11
10.0.26300) `IsAvailable()` is true ([quic-design.md](quic-design.md#the-bcl-counterpart-systemnetquic)).

### Quiche constants and random IDs

The units that call quiche only for a constant or a random ID use the RFC 9000 values and System.Security.Cryptography:
`Quiche.QUICHE_MAX_CONN_ID_LEN` is `Quic.MAX_CONN_ID_LEN` (20, RFC 9000 17.2) in `QuicConnectionAddress`,
`SecureRandomQuicConnectionIdGenerator` and `HmacSignQuicConnectionIdGenerator`; `SecureRandom` is
`RandomNumberGenerator.Fill` and `javax.crypto.Mac` HmacSHA256 is `HMACSHA256` (one instance per thread, through
`FastThreadLocalFunc`, as Java's `FastThreadLocal<Mac>`). `MAX_CONN_ID_LEN`, `MAX_DATAGRAM_SIZE` (1350) and
`RESET_TOKEN_LEN` (16) keep their values.

### NIO buffers

`java.nio.ByteBuffer` is `ReadOnlyMemory<byte>` (the bytes from position to limit), as in codec-compression and the
handler ssl port: the connection ID and reset token generators take and return it, `SipHash.MacHash` reads it, and
`QuicConnectionAddress` takes it in a constructor. A `ReadOnlyMemory<byte>` cannot be null, so the null checks of these
parameters have no counterpart. `QuicConnectionAddress` keeps a private copy of the ID (Java keeps a read-only view of
the caller's buffer). `SipHash` reads words little-endian with `BinaryPrimitives`, which the Java code reaches by
reversing big-endian reads, and widens the tail bytes with sign extension as Java does.

### TLS: `QuicSslContext` and `QuicSslEngine`

The handler port has no `SSLEngine`-shaped facade ([handler-ssl-design.md](handler-ssl-design.md), decision 2): the
engine of a TLS connection is the `SslClientAuthenticationOptions`/`SslServerAuthenticationOptions` a context builds
for it, and `SslContext` does not port `newEngine` or `sessionContext()`. QUIC carries TLS 1.3 inside the transport, and
System.Net.Quic takes exactly these options per connection (`QuicClientConnectionOptions.ClientAuthenticationOptions`,
`QuicServerConnectionOptions.ServerAuthenticationOptions`) and reports the negotiated facts on the `QuicConnection`.
So the two abstractions are:

- `QuicSslContext : SslContext` (abstract). The Java overrides of `newEngine` and `sessionContext()` are new abstract
  members: `QuicSslEngine NewEngine(IByteBufAllocator alloc)` (a server engine, or a client engine without peer
  information), `QuicSslEngine NewEngine(IByteBufAllocator alloc, string peerHost, int peerPort)` (a client engine;
  `peerHost` becomes `TargetHost`, which drives SNI and host name validation) and `IQuicSslSessionContext
  SessionContext()`. The `SslContext` members stay abstract for stage 2: `IsClient()` (selects the options type),
  `CipherSuites()`, `ApplicationProtocolNegotiator()` (its `Protocols()` is the ALPN list, also needed before any
  connection by `QuicListenerOptions.ApplicationProtocols`) and the protected `NewHandler(alloc, startTls)` and
  `NewHandler(alloc, peerHost, peerPort, startTls)`, which a QUIC context implements by throwing
  `NotSupportedException` (QUIC has no `SslHandler`; Java's `QuicheQuicSslContext` throws
  `UnsupportedOperationException`). `SslContext` declares them `protected internal` in another assembly, so the
  overrides are `protected override`. The package-private `toX509Certificates0` is internal and calls
  `SslContext.ToX509Certificates(System.IO.Stream)` for `QuicSslContextBuilder`.
- `QuicSslEngine` (abstract; Java's is an empty abstract `SSLEngine` subclass). It holds the configuration of one
  connection and, after the handshake, its negotiated facts, through CLR-only abstract members named after the
  `SSLEngine`/`SSLSession` accessors:

  | Member | Java | Stage 2 source |
  | --- | --- | --- |
  | `bool GetUseClientMode()` | `SSLEngine.getUseClientMode()` | which options type the context built |
  | `string GetPeerHost()`, `int GetPeerPort()` | `SSLEngine.getPeerHost()/getPeerPort()` | the `NewEngine` arguments (null and -1 without them) |
  | `SslClientAuthenticationOptions ClientAuthenticationOptions()` | (engine configuration) | `TargetHost`, `ApplicationProtocols`, client certificates or `LocalCertificateSelectionCallback`, `RemoteCertificateValidationCallback`; null on a server |
  | `SslServerAuthenticationOptions ServerAuthenticationOptions()` | (engine configuration) | `ServerCertificateContext` or `ServerCertificateSelectionCallback`, `ApplicationProtocols`, `ClientCertificateRequired`, `RemoteCertificateValidationCallback`; null on a client |
  | `string GetApplicationProtocol()` | `SSLEngine.getApplicationProtocol()` | `QuicConnection.NegotiatedApplicationProtocol` (UTF-8, as `SslHandler` decodes it); null before the handshake |
  | `X509Certificate2[] GetPeerCertificates()` | `getSession().getPeerCertificates()` | `QuicConnection.RemoteCertificate` with the chain its validation callback receives (as `SslHandler.PeerCertificates()`); null while unknown |
  | `TlsCipherSuite GetCipherSuite()` | `getSession().getCipherSuite()` | `QuicConnection.NegotiatedCipherSuite` (IANA names, as Java's); `TLS_NULL_WITH_NULL_NULL` before the handshake |
  | `SslProtocols GetProtocol()` | `getSession().getProtocol()` | `QuicConnection.SslProtocol` (TLS 1.3); `None` before the handshake |

  Stage 2 hands the options of `NewEngine(...)` to the client connection options or, on a server, returns them from
  `QuicListenerOptions.ConnectionOptionsCallback` per incoming connection, and records the facts in its engine subclass
  when `QuicConnection.ConnectAsync`/`AcceptConnectionAsync` completes. `IQuicChannel.SslEngine()` returns that engine.
  The other `SSLEngine` members (wrap/unwrap, delegated tasks, handshake status, the enabled protocol and cipher suite
  setters) served quiche's BoringSSL engine and have no counterpart: msquic runs the handshake. The BoringSSL callbacks
  (keyless private keys, key logging, session tickets, SNI context selection) have no counterpart either; certificate
  selection and validation map to the callbacks of the options.
- `IQuicSslSessionContext` has no base interface (`SSLSessionContext` is not ported) and keeps `SetTicketKeys(params
  SslSessionTicketKey[])`; System.Net.Quic manages session tickets inside msquic, so stage 2 reports it as unsupported.

### Features System.Net.Quic lacks

The datagram extension (`QuicDatagramExtensionEvent`), stream priorities (`QuicStreamPriority`, `UpdatePriorityAsync`),
connection and path statistics (`IQuicConnectionStats`, `IQuicConnectionPathStats`, the `Collect*Async` members), path
events (`QuicPathEvent`), congestion control selection (`QuicCongestionControlAlgorithm`), QLOG (`QLogConfiguration`,
`QuicChannelOption.QLOG`), 0-RTT early data (`SslEarlyDataReadyEvent`), session ticket keys (`SslSessionTicketKey`), the
peer's transport parameters (`IQuicTransportParameters`) and the connection ID, reset token and retry token handlers
keep their Java API (owner decision 2026-11-27). The msquic implementation reports them as unsupported where they
are used, as `SslHandler` does for a client renegotiation ([table](#unsupported-features)).

### Other adaptations

- Java enums without methods are C# enums (`QuicStreamType`, `QuicCongestionControlAlgorithm`); `QuicPacketType`, which
  has a method, is a sealed class with static readonly constants (the codec-http2 `Http2Error` rule), and the
  `QuicHeaderParser` switch over it uses case guards.
- `SocketAddress` is `EndPoint` (`QuicStreamAddress`, `QuicConnectionAddress`), `InetSocketAddress` is `IPEndPoint`.
- `QuicException : Exception` takes `ThrowableUtil.ThrowableToString(cause)` as the message of `QuicException(cause,
  error)`; it is not `System.Net.Quic.QuicException`, which stage 2 translates. `QuicClosedChannelException` and
  `QuicTimeoutClosedChannelException` derive from Transport's `ClosedChannelException`.
- `QuicHeaderParser` is `IDisposable` with `Close()` kept; its nested `QuicHeaderProcessor` is `IQuicHeaderProcessor`.
  The Buffer port's `GetByte` is unsigned; the header bit tests give the same results as with the signed Java byte.
- `IQuicStreamFrame.EMPTY_FIN` is a static interface field over a private nested class; `DefaultQuicStreamFrame`
  narrows the `DefaultByteBufHolder` members with covariant overrides (as codec-haproxy `HAProxyTLV`).
- `QuicStreamChannelBootstrap` and `Quic` keep options as `KeyValuePair<ChannelOption, object>` and attributes with
  their value and typed setter, as Transport's `Bootstrap`; `create()` is `CreateAsync(CancellationToken)`.
- `equals`/`hashCode`/`toString` keep the Java logic, values (`Objects.hash`, `Arrays.hashCode` spelled out) and text
  (Java booleans `true`/`false`, signed bytes in `Arrays.toString`, invariant-culture numbers); `IPEndPoint` prints
  `address:port` where `InetSocketAddress` prints `host/address:port`. `QuicConnectionAddress.EPHEMERAL` is equal only
  to itself (Java's `equals` throws `NullPointerException` on its null ID).
- `@Nullable`/`@NotNullByDefault` have no counterpart (nullable reference types are not enabled in this port).
- Ported as is, though it looks wrong upstream: `QuicTransportError.valueOf` subtracts `0x0100` from a code above 17,
  but the `CRYPTO_ERROR` entries start at index 17 of its table, so `valueOf(0x101)` returns `INTERNAL_ERROR` in Java and
  here (measured with the scratch probe below).

## JNI inventory

Recorded `jvm-only-replaced` (kind `implementation`) with the reason "JNI binding of quiche/BoringSSL, owner decision
2026-11-27", the CLR counterpart System.Net.Quic on msquic (the stage-2 implementation) and a whole-file comment
disposition (the SHA-256 of the normalized unique comments):

- quiche (22): `Quiche`, `QuicheConfig`, `QuicheError`, `QuicheLogger`, `QuicheNativeStaticallyReferencedJniMethods`,
  `QuicheQuicChannel`, `QuicheQuicChannelAddress`, `QuicheQuicChannelConfig`, `QuicheQuicClientCodec`,
  `QuicheQuicCodec`, `QuicheQuicConnection`, `QuicheQuicConnectionPathStats`, `QuicheQuicConnectionStats`,
  `QuicheQuicServerCodec`, `QuicheQuicSslContext`, `QuicheQuicSslEngine`, `QuicheQuicSslEngineMap`,
  `QuicheQuicStreamChannel`, `QuicheQuicStreamChannelConfig`, `QuicheQuicTransportParameters`, `QuicheRecvInfo`,
  `QuicheSendInfo`.
- BoringSSL (22): `BoringSSL`, `BoringSSLAsyncPrivateKeyMethod`, `BoringSSLCertificateCallback`,
  `BoringSSLCertificateCallbackTask`, `BoringSSLCertificateVerifyCallback`, `BoringSSLCertificateVerifyCallbackTask`,
  `BoringSSLContextOption`, `BoringSSLHandshakeCompleteCallback`, `BoringSSLKeylessManagerFactory`,
  `BoringSSLKeylessPrivateKey`, `BoringSSLKeylog`, `BoringSSLKeylogCallback`, `BoringSSLLoggingKeylog`,
  `BoringSSLNativeStaticallyReferencedJniMethods`, `BoringSSLPrivateKeyMethod`, `BoringSSLPrivateKeyMethodDecryptTask`,
  `BoringSSLPrivateKeyMethodSignTask`, `BoringSSLPrivateKeyMethodTask`, `BoringSSLSessionCallback`,
  `BoringSSLSessionTicketCallback`, `BoringSSLTask`, `BoringSSLTlsextServernameCallback`.
- `SockaddrIn` (1): native `sockaddr_in`/`sockaddr_in6` encoding for quiche's packet I/O.

`BoringSSLAsyncPrivateKeyMethod`, `BoringSSLContextOption`, `BoringSSLKeylessManagerFactory` and `BoringSSLKeylog` are
public Java API tied to BoringSSL (keyless signing, BoringSSL context options, key logging); they have no System.Net.Quic
counterpart. None of the 45 units has a `@Deprecated` type or `@deprecated` tag (`QuicheQuicChannelConfig` has a
`@Deprecated` member only). `module-info.yml` and `package-info.java` are `jvm-only-replaced` as in the other modules
(project file and namespace).

## Stage 2: the msquic implementation

### Architecture: the endpoint channel (listener model)

In Java the codec built by `QuicServerCodecBuilder`/`QuicClientCodecBuilder` is the handler of a `NioDatagramChannel`
and runs QUIC in user space on its datagrams. msquic owns the UDP socket, so the owner's listener model (2026-11-27,
[quic-design.md](quic-design.md)) puts the codec on a CLR-only channel that takes the datagram channel's place:
`QuicEndpointChannel` (public, in this namespace). It is named after RFC 9000's *endpoint* ("an entity that can
participate in a QUIC connection by generating, receiving, and processing QUIC packets") because it is neither a datagram
channel nor a server socket: it stands for msquic's UDP endpoint, and the client and server codecs share it. It derives
from the Transport's `AbstractChannel`, needs no I/O handle (any Transport event loop runs it) and carries no messages
of its own (writes fail with `NotSupportedException`; `connect(...)` only carries the QUIC channels' own requests).

```csharp
// Server (QuicServerExample): binding starts a QuicListener; each connection is a child IQuicChannel.
IChannel server = await new Bootstrap().Group(group).Channel<QuicEndpointChannel>()
        .Handler(new QuicServerCodecBuilder()
                .SslContext(QuicSslContextBuilder.ForServer(key, null, cert).ApplicationProtocols("hq-29").Build())
                .MaxIdleTimeout(TimeSpan.FromSeconds(5))
                .InitialMaxData(10000000)
                .InitialMaxStreamDataBidirectionalLocal(1000000)
                .InitialMaxStreamDataBidirectionalRemote(1000000)
                .InitialMaxStreamsBidirectional(100)
                .TokenHandler(InsecureQuicTokenHandler.INSTANCE)
                .Handler(new MyQuicChannelHandler())
                .StreamHandler(new MyStreamHandler())
                .Build())
        .BindAsync(new IPEndPoint(IPAddress.Loopback, 9999));

// Client (QuicClientExample): binding keeps the local address; IQuicChannel.NewBootstrap connects through it.
IChannel endpoint = await new Bootstrap().Group(group).Channel<QuicEndpointChannel>()
        .Handler(new QuicClientCodecBuilder()
                .SslContext(QuicSslContextBuilder.ForClient()
                        .TrustManager(InsecureTrustManagerFactory.INSTANCE).ApplicationProtocols("hq-29").Build())
                .MaxIdleTimeout(TimeSpan.FromSeconds(5))
                .InitialMaxData(10000000)
                .InitialMaxStreamDataBidirectionalLocal(1000000)
                .Build())
        .BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
IQuicChannel quicChannel = await IQuicChannel.NewBootstrap(endpoint)
        .StreamHandler(new ChannelInboundHandlerAdapter())
        .RemoteAddress(new IPEndPoint(IPAddress.Loopback, 9999))
        .ConnectAsync();
IQuicStreamChannel stream = await quicChannel.CreateStreamAsync(QuicStreamType.BIDIRECTIONAL, new MyStreamHandler());
await stream.WriteAndFlushAsync(Unpooled.CopiedBuffer("GET /\r\n", Encoding.ASCII));
await stream.ShutdownOutputAsync();
```

- **Server.** Binding the endpoint with a server codec starts a `QuicListener` on the address (the endpoint's
  `LocalAddress` is the listener's endpoint). The listener's ALPN list, needed before any connection, is that of the first
  connection's engine, which binding creates in advance (with `SslContext(...)` it is the context's list). For each new
  connection msquic asks for its options (`QuicListenerOptions.ConnectionOptionsCallback`): the codec creates the server
  QuicChannel there, applies the builder's handler, options and attributes (`Quic.SetupChannel`), asks the
  `sslEngineProvider` for its `QuicSslEngine` (a server engine of this implementation, else `ArgumentException` as in
  Java), selects the SNI context when the context has a mapping, and returns the `QuicServerConnectionOptions`. The
  endpoint's `read()` (AUTO_READ by default) accepts the next connection, which completes after the handshake; the accepted
  QuicChannel passes the endpoint's pipeline as `channelRead` and the codec registers it with the endpoint's event loop
  (`channelRegistered`, `channelActive`, then `SslHandshakeCompletionEvent.SUCCESS`), as `QuicheQuicServerCodec` registers
  its channels. A connection whose handshake fails never becomes a QuicChannel (the listener drops it).
- **Client.** Binding the endpoint with a client codec only records its local address. `IQuicChannel.NewBootstrap(endpoint)`
  (the static `newBootstrap(Channel)` stage 1 left out) creates the client QuicChannel, registers it on the endpoint's event
  loop and connects it; as in Java, the channel's connect goes through its parent's pipeline (`MsQuicChannelAddress`, the
  counterpart of `QuicheQuicChannelAddress`), where the client codec validates the engine and starts
  `QuicConnection.ConnectAsync` with the endpoint's address as `LocalEndPoint`. The connect completes with the handshake
  (`channelActive`, `SslHandshakeCompletionEvent.SUCCESS`); the connect timeout, cancellation, `ConnectionPendingException`
  and `AlreadyConnectedException` of `QuicChannelUnsafe.connect` keep their behavior.
- Closing the endpoint (or removing the codec) stops the listener and closes its QuicChannels, as the Java codec closes its
  channels with the datagram channel.

### Class map

| Java (codec-classes-quic) | CLR (`Netty.NET.Handler.Codec.Quic`) |
| --- | --- |
| `NioDatagramChannel` as the codec's channel | `QuicEndpointChannel` (public, CLR-only) |
| `QuicheQuicCodec` | `MsQuicCodec` (internal, CLR-only; the `ChannelHandler` that `Build()` returns) |
| `QuicheQuicServerCodec` | `MsQuicServerCodec` (`QuicListener`, connection options callback, accept) |
| `QuicheQuicClientCodec` | `MsQuicClientCodec` (`QuicConnection.ConnectAsync`) |
| `QuicheConfig` | `MsQuicConfig` (the builder settings as `QuicConnectionOptions`) |
| `QuicheQuicChannel`, `QuicheQuicChannelConfig` | `MsQuicChannel`, `MsQuicChannelConfig` (over `QuicConnection`) |
| `QuicheQuicStreamChannel`, `QuicheQuicStreamChannelConfig` | `MsQuicStreamChannel`, `MsQuicStreamChannelConfig` (over `QuicStream`) |
| `QuicheQuicChannelAddress` | `MsQuicChannelAddress` |
| `QuicheQuicSslContext`, `QuicheQuicSslEngine` | `MsQuicSslContext`, `MsQuicSslEngine` |

The Java classes on the left stay recorded `jvm-only-replaced` (JNI units); the CLR-only classes carry `// CLR-only:`
comments that name their Java counterparts.

### QuicChannel over QuicConnection (`MsQuicChannel`)

- Runs on its parent endpoint's event loop (the only compatible one); events and errors complete there.
- Inbound streams are accepted as soon as the connection is established (`AcceptInboundStreamAsync`, which also reports the
  end of the connection) and delivered as `channelRead(IQuicStreamChannel)` while a `read()` is pending (all accepted
  streams per read, then `channelReadComplete`), as Java gates them on `recvStreamPending`. A stream that reaches the tail
  is set up with the stream handler, options and attributes and registered (`onUnhandledInboundMessage`).
- `CreateStreamAsync(type, handler)` is `OpenOutboundStreamAsync`; it fails with `QuicException` `STREAM_LIMIT_ERROR` when
  the peer allows no stream of the type. `PeerAllowedStreams` counts the capacity reported by
  `QuicConnectionOptions.StreamCapacityCallback` minus the streams opened here; `QuicStreamLimitChangedEvent` fires once
  the connection is established (msquic reports the peer's initial limits before `ConnectAsync` completes) and on every
  later increase (stage 3). System.Net.Quic does not call the callback of a server connection for the client's initial
  limits, so a server channel's `PeerAllowedStreams` stays 0 and its `CreateStreamAsync` waits for stream capacity
  instead of failing ([codec-native-quic-porting.md](codec-native-quic-porting.md#stage-3-probe)).
- The end of the connection closes the channel: a peer's `CONNECTION_CLOSE` (`QuicError.ConnectionAborted`) fires
  `QuicConnectionCloseEvent` with its application error code (or transport code), an idle timeout
  (`QuicError.ConnectionIdle`/`ConnectionTimeout`) sets `IsTimedOut()`, and the open streams close with
  `QuicTimeoutClosedChannelException` or `ClosedChannelException`. System.Net.Quic refuses `AcceptInboundStreamAsync` when
  both inbound stream limits are 0 (the quiche default); the end of such a connection is seen by its stream operations.
- `close()` is `QuicConnection.CloseAsync(error)` and `DisposeAsync()` (error 0, or the one of
  `CloseAsync(applicationClose, error, reason)`). msquic sends application `CONNECTION_CLOSE` frames only and no reason
  phrase: the reason is released unused, and a transport close with a non-zero error reports `NotSupportedException`.
- `SslEngine()` is the connection's `MsQuicSslEngine`, which records `NegotiatedApplicationProtocol`, the peer's
  certificate chain, `NegotiatedCipherSuite` and `SslProtocol` once the handshake completed.
- `LocalAddress`/`RemoteAddress`: System.Net.Quic hides the connection IDs, so they are `QuicConnectionAddress` values that
  identify the connection in this process (those given to `QuicChannelBootstrap.LocalConnectionAddress`/
  `RemoteConnectionAddress`, else random ones of `Quic.MAX_CONN_ID_LEN` bytes), not the wire IDs; null before the
  handshake. `LocalSocketAddress()`/`RemoteSocketAddress()` are the connection's `LocalEndPoint`/`RemoteEndPoint`.

### QuicStreamChannel over QuicStream (`MsQuicStreamChannel`)

- Open and active from its creation; `channelRegistered`/`channelActive` when the parent's event loop registers it. A
  locally created unidirectional stream has its input shut down from the start; the stream ID, type and
  `IsLocalCreated()` follow RFC 9000 2.1 (`QuicStream.Id`).
- Reads honor `read()`/AUTO_READ: each `read()` starts a batch of `QuicStream.ReadAsync` calls into the buffers of the
  `RecvByteBufAllocator` handle, which continues while `continueReading()` allows it and data is already buffered; a read
  that does not complete synchronously ends the batch with `channelReadComplete` and is delivered by the next one, and a
  result that arrives while no `read()` is pending waits for the next. A FIN (`ReadAsync` returns 0, or `ReadsClosed`
  completed with the last bytes) shuts the input down and, with half-closure (the default), fires
  `ChannelInputShutdownEvent` and `ChannelInputShutdownReadComplete`; without it the stream closes. `READ_FRAMES` delivers
  `IQuicStreamFrame`s whose FIN flag carries it. A peer's RESET_STREAM is `QuicStreamResetException` with its error code.
- Writes go through the `ChannelOutboundBuffer` (writability follows the write buffer water marks, where quiche's stream
  capacity decides it in Java), one `QuicStream.WriteAsync` per message (composite buffers by segment). An
  `IQuicStreamFrame` with FIN completes the writes, after which writes fail with
  `ChannelOutputShutdownException("Fin was sent already")`, checked before the message is filtered as
  `QuicStreamChannelUnsafe.write` does (stage 3); a peer's STOP_SENDING fails the flushed writes with
  `ChannelOutputShutdownException("STOP_SENDING frame received")` without closing the stream; a write on a peer-created
  unidirectional stream fails with `NotSupportedException`. Once the FIN was sent and the peer's FIN received (or on a
  local unidirectional stream), the stream closes itself (`closeIfDone`).
- `ShutdownOutputAsync()` writes `IQuicStreamFrame.EMPTY_FIN`; `ShutdownOutputAsync(error)`, `ShutdownInputAsync(error)`
  and `ShutdownAsync(error)` are `QuicStream.Abort(Write/Read/Both, error)` (RESET_STREAM/STOP_SENDING).
- `close()` sends the FIN if none was sent and disposes the `QuicStream`, which also stops a read side that is still open
  (STOP_SENDING with error 0, as `DisposeAsync` does; quiche keeps the unread data instead).

### Codec and TLS settings

| Builder setting | System.Net.Quic (`MsQuicConfig`) |
| --- | --- |
| `MaxIdleTimeout(TimeSpan)` | `IdleTimeout`; unset or 0 is `Timeout.InfiniteTimeSpan` (quiche: no idle timeout) |
| `InitialMaxData` | `InitialReceiveWindowSizes.Connection` |
| `InitialMaxStreamDataBidirectionalLocal`/`Remote`, `InitialMaxStreamDataUnidirectional` | `InitialReceiveWindowSizes.LocallyInitiatedBidirectionalStream`/`RemotelyInitiatedBidirectionalStream`/`UnidirectionalStream` |
| `InitialMaxStreamsBidirectional`/`Unidirectional` | `MaxInboundBidirectionalStreams`/`MaxInboundUnidirectionalStreams` (unset: 0, the quiche default; at most 65535) |
| `SslContext(QuicSslContext)`, `SslEngineProvider(Func<IQuicChannel, QuicSslEngine>)` | the engine's `SslClientAuthenticationOptions`/`SslServerAuthenticationOptions`; the server listener's ALPN list |
| `SslTaskExecutor(IExecutor)` | accepted, not used (msquic runs TLS on its own threads) |
| `TokenHandler`: null, `NoQuicTokenHandler`, `InsecureQuicTokenHandler` | accepted; msquic validates addresses with its own retry tokens |
| (none) | `DefaultStreamErrorCode` and `DefaultCloseErrorCode` are 0, the code of Java's `shutdownInput()`/`close()` |

msquic takes receive windows that are powers of two (another value fails the connection options, measured with .NET
10.0.11), so a window is rounded up to the next power of two, at most 2^30; an unset window keeps msquic's default and 0
is unsupported. (long, TimeUnit) setters take a `TimeSpan` (the Transport convention).

### Unsupported features

They keep their Java API (owner decision 2026-11-27) and report `NotSupportedException` where they are used, as
`SslHandler` does for a client renegotiation:

| Feature | API | Reported by |
| --- | --- | --- |
| QUIC versions other than 1 | `Version(int)` | `Build()` |
| GREASE off, Hystart off, path MTU discovery, disabling active migration | `Grease(false)`, `Hystart(false)`, `DiscoverPmtu(true)`, `ActiveMigration(false)` | `Build()` |
| UDP payload sizes, ACK delay exponent, max ACK delay | `MaxSendUdpPayloadSize`/`MaxRecvUdpPayloadSize` other than 1200/65527, `AckDelayExponent` other than 3, `MaxAckDelay` other than 25 ms | `Build()` |
| Congestion control | `CongestionControlAlgorithm` other than `CUBIC`, `InitialCongestionWindowPackets` other than 10 | `Build()` |
| Datagram extension (RFC 9221) | `Datagram(...)`; writes on an `IQuicChannel` | `Build()`; the write Task |
| Connection IDs and reset tokens | `LocalConnectionIdLength` other than 20, `ActiveConnectionIdLimit` other than 2, `StatelessResetToken`, `ConnectionIdAddressGenerator`, `ResetTokenGenerator` | `Build()` |
| Retry token handlers of their own | `TokenHandler(other)` | `Build()` |
| Flush strategy of the datagram writes | `FlushStrategy` other than `IFlushStrategy.DEFAULT` | `Build()` |
| QLOG | `QuicChannelOption.QLOG` | `SetOption` (logged by `Quic.SetupChannel`) |
| Statistics, path statistics, transport parameters | `CollectStatsAsync`, `CollectPathStatsAsync`, `PeerTransportParameters()` | the Task / the call |
| Stream priorities | `UpdatePriorityAsync` (`Priority()` stays null) | the Task |
| Transport `CONNECTION_CLOSE` with an error code, reason phrases | `CloseAsync(false, error != 0, reason)` | the Task (the reason is released) |
| 0-RTT early data, key logging | `QuicSslContextBuilder.EarlyData(true)`, `Keylog(true)` | `QuicSslContextBuilder.Build()` |
| Session ticket keys | `IQuicSslSessionContext.SetTicketKeys` | the call |
| BoringSSL context options (groups, signature algorithms, key types) | `QuicSslContextBuilder.Option(...)` | skipped with a debug log, as quiche skips an unknown option |
| SNI completion event | `QuicSslContextBuilder.Sni`/`BuildForServerWithSni` select the context, `SniCompletionEvent` is not fired | - |

Quiche's defaults are accepted (for example `Grease(true)`, `ActiveMigration(true)`, `CongestionControlAlgorithm(CUBIC)`),
so code written for the Java builders keeps working where it states the defaults. Active migration is allowed by quiche
(`disable_active_migration` is false by default) and by msquic (`MigrationEnabled` is true by default, and
System.Net.Quic does not change it); stage 2 had it the other way round and rejected `ActiveMigration(true)`, which
stage 3 corrected ([codec-native-quic-porting.md](codec-native-quic-porting.md#fixes-in-stage-3)).

### Platform analyzer

The msquic classes carry `[SupportedOSPlatform("windows"/"linux"/"macos")]`, the platforms of System.Net.Quic's own
attributes; the public types that reach them (`QuicEndpointChannel`, the codec builders, `QuicChannelBootstrap`) check
`MsQuicConfig.IsSupportedPlatform` (a `[SupportedOSPlatformGuard]` property) and throw `PlatformNotSupportedException`
outside them, so callers of the public API need no platform guard of their own; `Quic.IsAvailable()` still decides the
owner's platforms (Windows and Linux).

## Test results (stage 1)

Windows 11 (10.0.26300), .NET 10, 2026-11-27:

- `dotnet build src/Netty.NET.Handler.Codec.Quic/Netty.NET.Handler.Codec.Quic.csproj -p:BuildProjectReferences=false`
  (and `--no-incremental`): 0 errors, 0 warnings.
- `dotnet build test/Netty.NET.Handler.Codec.Quic.Tests/Netty.NET.Handler.Codec.Quic.Tests.csproj
  -p:BuildProjectReferences=false`: 0 errors, 0 warnings.
- `dotnet test test/Netty.NET.Handler.Codec.Quic.Tests --no-build`: 2 passed, 0 failed, 0 skipped
  (`TestThrowsOnTruncatedDestinationConnectionIdLength`, `TestThrowsOnTruncatedSourceConnectionIdLength`).
- The manifest check above prints `codec-classes-quic manifest verified`.
- A scratch console probe (not part of the repository) exercised the public and internal API on this host:
  `Quic.IsAvailable()` and `IsVersionSupported(1)` are true; `QuicConnectionAddress` equality and Java hash value;
  the SipHash-2-4 reference vector of the paper (`a129ca6149be45e5`); idempotent HMAC connection IDs and 16-byte
  reset tokens; the `InsecureQuicTokenHandler` round trip; `QuicHeaderParser` on Initial, short and version
  negotiation headers; and an `EmbeddedChannel` subclass implementing `IQuicChannel`/`IQuicStreamChannel` (the shape of
  codec-http3's test channels) with `NewStreamBootstrap().CreateAsync()`, `CloseAsync(applicationClose, error, reason)`
  and the default `ShutdownInputAsync()` (error 0).

## Test results (stage 2)

Windows 11 (10.0.26300), .NET 10.0.11, 2026-11-27:

- `dotnet build src/Netty.NET.Handler.Codec.Quic/Netty.NET.Handler.Codec.Quic.csproj -p:BuildProjectReferences=false
  --no-incremental`: 0 errors, 0 warnings (the platform analyzer CA1416 included).
- `dotnet build test/Netty.NET.Handler.Codec.Quic.Tests/Netty.NET.Handler.Codec.Quic.Tests.csproj
  -p:BuildProjectReferences=false --no-incremental`: 0 errors, 0 warnings.
- `dotnet test test/Netty.NET.Handler.Codec.Quic.Tests --no-build`: 64 passed, 0 failed, 0 skipped (6 consecutive runs of
  the whole project): `QuicHeaderParserTest` (2) and the ported codec-native-quic tests on the msquic implementation, both
  peers on loopback: `QuicChannelEchoTest` (16 theory rows), `QuicStreamChannelCreationTest` (4),
  `QuicStreamHalfClosureTest` (4), `QuicStreamShutdownTest` (6), `QuicStreamTypeTest` (4), `QuicStreamFrameTest` (4) and
  `QuicChannelConnectTest` (24: 12 methods x 2 executors), see [codec-native-quic-porting.md](codec-native-quic-porting.md).
- `tools/Test-CodecPortingManifest.ps1 -Module codec-classes-quic` (without `-AllowPending`) prints `codec-classes-quic
  manifest verified: 108 pinned files (53 clr-adapted, 54 jvm-only-replaced, 1 verified)`;
  `-Module codec-native-quic -AllowPending` prints `codec-native-quic manifest verified: 56 pinned files (1 clr-adapted,
  18 jvm-only-replaced, 28 pending, 9 verified), 25 mapped original tests, 30 CLR-inapplicable and 4 not-ported original
  tests`.
- Probes on this host (scratch console programs, not part of the repository): System.Net.Quic requires receive windows
  that are powers of two (a connection with `QuicReceiveWindowSizes.Connection = 10000000` fails the server's options
  with a TLS `user_canceled` alert, 16777216 works); `StreamCapacityCallback` reports the peer's initial stream limits
  before `ConnectAsync` completes; `ReadAsync` returns 0 after the FIN and `ReadsClosed` completes with the last bytes;
  STOP_SENDING and RESET_STREAM surface as `QuicException` `StreamAborted` with the peer's code; the idle timeout as
  `ConnectionIdle`; a peer's `CloseAsync(code)` as `ConnectionAborted` with the code; an ALPN mismatch and a rejected
  certificate as `AuthenticationException`. A server connection closed while the server validates the client
  certificate crashes the process in msquic ([codec-native-quic-porting.md](codec-native-quic-porting.md#known-problems)).

## Test results (stage 3)

The remaining codec-native-quic tests, the QUIC examples and three fixes of the msquic implementation (the
`QuicStreamLimitChangedEvent` after the handshake, the FIN check of a stream write, `activeMigration`) are described in
[codec-native-quic-porting.md](codec-native-quic-porting.md#test-results): `dotnet test
test/Netty.NET.Handler.Codec.Quic.Tests --no-build` passes 181 of 181 tests in five consecutive runs, and the manifest
check above still prints `codec-classes-quic manifest verified: 108 pinned files (53 clr-adapted, 54 jvm-only-replaced, 1
verified)`.

At stage 3 the project was not yet in `Netty.NET.sln`, and the `deferred-to-module` entries of
[codec-http3-porting-manifest.json](codec-http3-porting-manifest.json) that import the stage-2 units (the codec
builders, `QuicChannelBootstrap`, `QuicSslContextBuilder`) failed their check until codec-http3 ported them. Both are
done (checked 2026-11-27): `Netty.NET.sln` contains `Netty.NET.Handler.Codec.Quic`, its tests and examples, and the
codec-http3 manifest has no `deferred-to-module` entry.

## Endpoint closes with its event loop (2026-11-27)

An event loop that shuts down closes the channels registered with its I/O handler (`NioIoHandler`), so in Java the
datagram channel under the QUIC codec closes and the codec closes its QUIC channels. `QuicEndpointChannel` has no I/O
handle for the handler to close; it registers a shutdown hook with its event loop (`SingleThreadEventExecutor`
`AddShutdownHook` in `DoRegister`, removed in `DoDeregister`) that closes the endpoint. Without it,
`Http3FrameToHttpObjectCodecTest.MultipleFramesInFin`, which shuts its group down without closing the channel as in
Java, left QUIC streams and their receive buffers open; the leak presence detector of
[testsuite-common](testsuite-common-porting.md) reported them. The CLR-only `QuicEndpointChannelTest` (server and
client endpoint) fails without the hook (timeout) and passes with it.
