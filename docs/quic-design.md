# QUIC and HTTP/3: design investigation (2026-11-26)

Netty 4.2.19.Final (pinned commit `64cc10f38ea5f5bd7eae48507817c66680d0afdc`) splits QUIC and HTTP/3 over three
Maven modules. None of them has a Netty.NET project yet, except the part of codec-http3 that needs no QUIC type
([codec-http3-porting.md](codec-http3-porting.md)). This page records what the modules contain, how the QUIC API maps
onto the .NET 10 BCL (`System.Net.Quic`), what has no counterpart, and the staged plan. The decisions marked
**owner decision needed** are open.

## Inventory

| Module | Files | Content |
| --- | --- | --- |
| `codec-classes-quic` | 108 (106 Java, `module-info.yml`, 1 test) | The QUIC API (`QuicChannel`, `QuicStreamChannel`, `QuicStreamType`, `QuicStreamFrame`, codec builders, `QuicSslContext`, events, stats) and its implementation on quiche and BoringSSL through JNI (`Quiche*`, `BoringSSL*`, `SockaddrIn`, `QuicheQuicCodec`). |
| `codec-native-quic` | 56 (40 Java tests, 3 C, 2 H, templates, GraalVM metadata) | The JNI library (C) and the QUIC tests, which run against the native implementation. |
| `codec-http3` | 119 (79 Java, 39 tests, `module-info.yml`) | HTTP/3 frames, QPACK, connection/stream handlers and the HTTP/1 conversion, on `QuicChannel`/`QuicStreamChannel`. |

Of the 106 `codec-classes-quic` sources, 48 touch quiche, BoringSSL or JNI directly (by name). The 58 others are
the API and value types, but the API is not separable as it stands: the closure of the types codec-http3 imports
(`QuicChannel`, `QuicStreamChannel`, `QuicStreamType`, `QuicStreamChannelBootstrap`, `QuicStreamFrame`,
`QuicException`) reaches 79 files and the quiche implementation, because `QuicChannel.newBootstrap(Channel)` returns a
`QuicChannelBootstrap` that creates `QuicheQuicChannel`, and `QuicConnectionAddress` generates connection IDs through
`Quiche` (measured with a source-closure script, scratch `quic_closure.py`).

### Why codec-http3 is ported only in part

`Http3CodecUtils` imports `QuicStreamChannel`, and almost every frame and QPACK type names it (frame type constants,
stream checks). `QpackDecoder`, `QpackEncoder` and `QpackAttributes` hold the QPACK encoder and decoder streams as
`QuicStreamChannel`. So the source closure without QUIC types is 23 of 79 main files (headers, error codes,
`HttpConversionUtil`, the QPACK Huffman decoder, value types) and 3 of 39 tests. The other 56 main files and 36 tests
are recorded `deferred-to-module` (blocked by `codec-classes-quic`), which `tools/Test-CodecPortingManifest.ps1`
recomputes from the imports and same-package type references.

## The BCL counterpart: System.Net.Quic

.NET ships QUIC as `System.Net.Quic` (`QuicListener`, `QuicConnection`, `QuicStream`) on msquic. It supports Windows
11 / Windows Server 2022 and later (msquic and TLS 1.3 through SChannel) and Linux with the `libmsquic` package;
macOS is not supported. `QuicListener.IsSupported` and `QuicConnection.IsSupported` report it at run time.

Probe on this host (Windows 11 10.0.26300, .NET 10.0.11, loopback, self-signed ECDSA certificate, ALPN `h3`):

| Check | Result |
| --- | --- |
| `IsSupported` (listener, connection) | `True`, `True` |
| Handshake | TLS 1.3, negotiated ALPN `h3` on both sides |
| Client bidirectional stream | id 0, `Type` Bidirectional, echo with `completeWrites: true` (FIN) |
| Client unidirectional stream | id 2, `CanRead` false on the client, `CanWrite` false on the server |
| Server unidirectional stream (as an HTTP/3 control stream) | id 3, accepted by `AcceptInboundStreamAsync` |
| Server `CloseAsync(0x100)` | the client's pending read fails with `QuicException`, `QuicError.ConnectionAborted`, `ApplicationErrorCode` 256 |

The stream IDs follow RFC 9000 2.1 (client-initiated bidirectional 0, client unidirectional 2, server unidirectional
3), which is what codec-http3's stream checks (`isServerInitiatedQuicStream`, `streamId % 2`) rely on.

### Mapping

| Netty QUIC | System.Net.Quic | Notes |
| --- | --- | --- |
| `QuicChannel` (a connection) | `QuicConnection` | `localAddress`/`remoteSocketAddress` are `LocalEndPoint`/`RemoteEndPoint`; `sslEngine()` facts are `NegotiatedApplicationProtocol`, `SslProtocol`, `NegotiatedCipherSuite`, `RemoteCertificate`, `TargetHostName`. |
| `QuicStreamChannel` | `QuicStream` | `streamId()` is `Id`, `type()` is `Type`, `isLocalCreated()` follows from the ID; `shutdownOutput()` is `CompleteWrites()`, `shutdownOutput(error)`/`shutdownInput(error)` are `Abort(QuicAbortDirection.Write/Read, error)`; `ReadsClosed`/`WritesClosed` report the peer side. |
| `QuicChannel.createStream(type, handler)`, `newStreamBootstrap()` | `OpenOutboundStreamAsync(QuicStreamType)` | The stream channel is created around the opened `QuicStream`. |
| Inbound streams (child channels of `QuicChannel`) | `AcceptInboundStreamAsync()` | A loop per connection, as the NIO server channel accepts sockets. |
| `close(applicationClose, error, reason)` | `CloseAsync(errorCode)` | No reason phrase. |
| `QuicException`, `QuicTransportError` | `QuicException` (`QuicError`, `ApplicationErrorCode`, `TransportErrorCode`) | |
| Codec builder limits (`initialMaxStreamsBidirectional`, `maxIdleTimeout`, ...) | `QuicConnectionOptions` (`MaxInboundBidirectionalStreams`, `MaxInboundUnidirectionalStreams`, `IdleTimeout`, `KeepAliveInterval`, `HandshakeTimeout`, `InitialReceiveWindowSizes`, `DefaultStreamErrorCode`, `DefaultCloseErrorCode`) | Flow-control windows and stream limits only as far as the options expose them. |
| `QuicStreamLimitChangedEvent`, `peerAllowedStreams(type)` | `QuicConnectionOptions.StreamCapacityCallback` | Capacity increments per stream type. |
| `QuicSslContext` (BoringSSL) | `SslServerAuthenticationOptions` / `SslClientAuthenticationOptions` | As the handler port maps `SslContext` to `SslStream` (docs/handler-porting.md); certificate selection and validation callbacks map, BoringSSL callbacks do not. |

### The structural difference

quiche runs QUIC in user space: the Java codec (`QuicheQuicCodec`, built by `QuicServerCodecBuilder` and
`QuicClientCodecBuilder`) is a `ChannelHandler` on a UDP `DatagramChannel`, parses and sends the datagrams itself, and
creates `QuicChannel`s as children of that UDP channel (`QuicChannel.newBootstrap(datagramChannel)`). msquic owns the
UDP socket, so there is no datagram channel under a `QuicConnection`. A port on the BCL therefore needs:

- a server channel over `QuicListener` whose children are `QuicChannel`s (as `NioServerSocketChannel` over a
  listening `Socket`), and a client `QuicChannelBootstrap` that connects with `QuicConnection.ConnectAsync`;
- the codec builders to produce `QuicListenerOptions`/`QuicClientConnectionOptions` instead of a datagram handler;
- `QuicChannel` and `QuicStreamChannel` implementations whose reads and writes are `QuicStream` operations completed
  on the channel's event loop, as the Transport port completes `Socket` operations (docs/transport-porting.md).

A managed QUIC stack over `NioDatagramChannel` (to keep the datagram-channel API) is not an option within the BCL:
it would need a QUIC implementation and TLS 1.3 secrets, which `SslStream` does not expose.

### No counterpart in System.Net.Quic

- The datagram extension (RFC 9221: `QuicDatagramExtensionEvent`, datagram writes on `QuicChannel`).
- Stream priorities (`QuicStreamPriority`, `updatePriority`).
- Connection and path statistics (`QuicConnectionStats`, `QuicConnectionPathStats`) and connection migration events
  (`QuicPathEvent`).
- Congestion control selection (`QuicCongestionControlAlgorithm`), QLOG (`QLogConfiguration`), the flush strategy of
  the datagram writes (`FlushStrategy`, `SegmentedDatagramPacketAllocator`).
- Connection ID and reset token generators, the retry token handler (`QuicConnectionIdGenerator`,
  `QuicResetTokenGenerator`, `QuicTokenHandler`) and `QuicCodecDispatcher` (several codecs on one port): msquic owns
  these.
- 0-RTT early data (`SslEarlyDataReadyEvent`), session ticket keys, keyless private keys and the other BoringSSL
  callbacks.
- macOS.

## Plan

1. **QUIC API layer** (project `Netty.NET.Handler.Codec.Quic`, namespace of `io.netty.handler.codec.quic`): port the
   API interfaces, value types and events of `codec-classes-quic`; record the quiche/BoringSSL/JNI units as not ported
   (see the owner decisions below). Then complete codec-http3 on the API: its tests run on `EmbeddedQuicChannel` and
   `EmbeddedQuicStreamChannel` (codec-http3 test helpers that implement the interfaces over `EmbeddedChannel`), so the
   36 deferred tests and the 56 deferred main files need no QUIC implementation. Done 2026-11-27
   ([codec-classes-quic-porting.md](codec-classes-quic-porting.md), [codec-http3-porting.md](codec-http3-porting.md)).
2. **msquic-backed channels**: `QuicChannel`/`QuicStreamChannel` over `QuicConnection`/`QuicStream`, the listener
   server channel, the bootstraps and the codec builders as option builders. Tests: the portable scenarios of
   `codec-native-quic`'s tests (echo, stream creation, half closure, shutdown, limits, readability/writability) on
   loopback, both peers in .NET. Done 2026-11-27: `QuicEndpointChannel` takes the datagram channel's place
   ([codec-classes-quic-porting.md](codec-classes-quic-porting.md#stage-2-the-msquic-implementation),
   [codec-native-quic-porting.md](codec-native-quic-porting.md)).
3. **HTTP/3 end to end**: codec-http3's `Http3ServerExample`/`Http3ClientExample` and `Http3SpecTestServer` on the
   msquic channels. Done 2026-11-27: both servers answer the example client and .NET's own HTTP/3 client (`HttpClient`
   with HTTP/3, version 3.0) on loopback; `Http3InteropTest` checks it ([codec-http3-porting.md](codec-http3-porting.md#test-results)).

Owner decisions (2026-11-27, accepted as proposed):

- **Listener-based model.** QUIC connections are children of a `QuicListener` server channel, and a client connects
  with `QuicConnection.ConnectAsync`; there is no datagram channel under QUIC, so `QuicChannel.newBootstrap(Channel)`
  and the codec builders configure the listener and connection options instead of a `DatagramChannel` handler.
- **quiche and BoringSSL are not ported.** They are reached through JNI, not through a Java library, so their units
  (`Quiche*`, `BoringSSL*`, `SockaddrIn`) and the user-space datagram machinery that only serves them are recorded with
  the status the JNI native transports use (`jvm-only-replaced`, docs/record-only-modules.md), with System.Net.Quic
  (msquic) as the CLR counterpart. API features that System.Net.Quic lacks (list above) keep their Java API; the msquic
  implementation reports them as unsupported where they are used, as `SslHandler` does for a client renegotiation.
- **Platforms.** Windows and Linux (`libmsquic`); `Quic.isAvailable()` is `QuicListener.IsSupported &&
  QuicConnection.IsSupported`, false on macOS.

## References

- System.Net.Quic overview: https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/quic/quic-overview
- `QuicConnection`: https://learn.microsoft.com/en-us/dotnet/api/system.net.quic.quicconnection?view=net-10.0
- `QuicStream`: https://learn.microsoft.com/en-us/dotnet/api/system.net.quic.quicstream?view=net-10.0
- RFC 9000 (QUIC), stream IDs: https://www.rfc-editor.org/rfc/rfc9000#section-2.1
- RFC 9114 (HTTP/3) and RFC 9204 (QPACK): https://www.rfc-editor.org/rfc/rfc9114, https://www.rfc-editor.org/rfc/rfc9204
- Pinned sources: https://github.com/netty/netty/tree/64cc10f38ea5f5bd7eae48507817c66680d0afdc/codec-classes-quic
