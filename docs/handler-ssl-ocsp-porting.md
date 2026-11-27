# handler-ssl-ocsp porting

Netty.NET ports `netty-handler-ssl-ocsp` of Netty 4.2.19.Final (pinned commit
`64cc10f38ea5f5bd7eae48507817c66680d0afdc`) to `src/Netty.NET.Handler.Ssl.Ocsp`, with its tests in
`test/Netty.NET.Handler.Ssl.Ocsp.Tests`. The [manifest](handler-ssl-ocsp-porting-manifest.json) records every pinned file
of `handler-ssl-ocsp/src` (14 files) with its C# counterpart, its CLR adaptations and evidence, or the reason it is not
ported. Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module handler-ssl-ocsp
```

| Status | Files |
| --- | --- |
| `clr-adapted` | 8 (the seven main classes and `OcspTest`) |
| `verified` | 3 (`AbstractOcspTest`, `OcspClientTest`, `OcspServerCertificateValidatorTest`) |
| `jvm-only-replaced` | 2 (`package-info.java`, `module-info.yml`) |
| `library-not-ported` | 1 (`OcspClientHandler`, which needs the OpenSSL engine) |

The module validates the server certificate after the TLS handshake: `OcspServerCertificateValidator` takes the peer's
certificate and its issuer, `OcspClient` sends an OCSP request over HTTP/1.1 to the responder named in the certificate's
Authority Information Access extension, checks the response (CertID, nonce, signature and the responder's authority) and
the validator fires an `OcspValidationEvent`, and closes the channel for a certificate that is not valid.

## Namespaces and assembly

The project, folder and assembly are `Netty.NET.Handler.Ssl.Ocsp`, the namespace root of `io.netty.handler.ssl.ocsp`
(the 2026-11-19 rule, see [codec-base-porting.md](codec-base-porting.md); no new name choice); the tests use
`Netty.NET.Handler.Ssl.Ocsp.Tests`. The project references `Netty.NET.Handler.Codec.Http` (which brings handler,
codec-base, transport, buffer and common) and `Netty.NET.Resolver.Dns`, the modules the pom lists;
`InternalsVisibleTo` opens internals only to the test assembly.

| Java package | C# namespace | Original package summary |
| --- | --- | --- |
| `io.netty.handler.ssl.ocsp` | `Netty.NET.Handler.Ssl.Ocsp` | Certificate validation using OCSP |

## Bouncy Castle replaced by the BCL

The Java module needs Bouncy Castle (`bcpkix-jdk18on`, `provided` scope) for the OCSP request and response. The BCL has
no OCSP API, but its ASN.1 reader and writer (`System.Formats.Asn1`) and certificate types cover what the module uses, as
pkitesting replaces the Bouncy Castle certificate building ([pkitesting-porting.md](pkitesting-porting.md)). The CLR-only
`OcspAsn1.cs` implements the structures of [RFC 6960](https://www.rfc-editor.org/rfc/rfc6960.txt) section 4; its types
are internal, because Bouncy Castle types appear only in package-private signatures of the Java module.

| Bouncy Castle | C# (`OcspAsn1.cs`) |
| --- | --- |
| `CertificateID` (`HASH_SHA1`, `matchesIssuer`, `getSerialNumber`) | `CertificateId` (SHA-1, SHA-256/384/512 for responses; the issuer name hash covers the subject DER and the key hash the `subjectPublicKey` bits) |
| `OCSPReqBuilder`, `Extension`, `DEROctetString` nonce | `OcspReqBuilder`, `OcspExtension`; the nonce extension carries the 16 nonce octets as its `extnValue`, as `new Extension(id_pkix_ocsp_nonce, false, new DEROctetString(nonce))` does |
| `OCSPResp`, `getResponseObject()` | `OcspResp`, `GetResponseObject()`: null without `responseBytes`, a `BasicOcspResp` for `id-pkix-ocsp-basic`, the undecoded bytes otherwise |
| `BasicOCSPResp` (`getResponses`, `getCerts`, `getExtension`, `isSignatureValid`) | `BasicOcspResp`; signatures verified with the BCL's RSA (PKCS #1 v1.5, SHA-1/256/384/512) and ECDSA (DER, SHA-1/256/384/512) keys |
| `SingleResp`, `RevokedStatus`, `UnknownStatus`, `CertificateStatus.GOOD` (null) | `SingleResp`, `RevokedStatus`, `UnknownStatus`; a good status is null |
| `AuthorityInformationAccess`, `AccessDescription.id_ad_ocsp` | the BCL's `X509AuthorityInformationAccessExtension.EnumerateOcspUris()` |
| `OCSPException`, `OperatorCreationException` | `CryptographicException`, as pkitesting and handler map the JCA's `CertificateException` |

The tests build responses with Bouncy Castle (`BasicOCSPRespBuilder`, `RespID`, `OCSPRespBuilder`, `OCSPResponse`,
`ResponseBytes`, `JcaContentSignerBuilder`); the CLR-only `OcspTestBuilders.cs` of the test project writes the same
structures with `AsnWriter`.

Not covered: RSASSA-PSS and EdDSA response signatures (EdDSA has no BCL type, see pkitesting), SHA-224 hashes, and
`ResponderID byKey` is read but not used (as in Java, the responder certificate is the first included certificate).

## Peer certificates from SslHandler

`OcspServerCertificateValidator` reads `ctx.pipeline().get(SslHandler.class).engine().getSession().getPeerCertificates()`.
`SslHandler.engine()` is not public in the C# port ([handler-ssl-design.md](handler-ssl-design.md), owner decision 2), and
`SslStream` exposes only the remote certificate. So `Netty.NET.Handler` gains an internal, CLR-only
`SslHandler.PeerCertificates()`:

- The handler wraps the `RemoteCertificateValidationCallback` of its authentication options. The wrapper records the
  chain `SslStream` built for the remote certificate (`X509Chain.ChainElements`, copied, as the chain is disposed after the
  callback) when the sender is the handler's own `SslStream`, then decides as before. It calls the configured callback,
  or without one returns `SslStream`'s own decision (`SslPolicyErrors.None`, with `RemoteCertificateNotAvailable` cleared
  when no remote certificate is required).
- The result is the remote certificate followed by its issuers. When `SslStream` skipped the callback (a resumed session)
  it is the remote certificate alone, and it is null before a remote certificate is known.
- `InternalsVisibleTo` opens it to `Netty.NET.Handler.Ssl.Ocsp`. A public accessor waits for an owner naming decision,
  as the internal `Engine()` does.

Measured on this host (Windows 11 10.0.26300, .NET 10.0.8): in the validation callback the chain's `ExtraStore` is
empty, while `ChainElements` holds the remote certificate and its issuer, so the chain elements are what is recorded.
Java's `getPeerCertificates()` returns the certificates as the peer sent them; the built chain starts with the same
certificate and continues with its issuer, which is what the validator uses (`certificates[1]`).

The handler's ssl tests were run after the change: `ssl` and `ssl.util` 281 pass and 27 are skipped in a run alone, as
recorded in [handler-porting.md](handler-porting.md), and handler-proxy's tests pass (69 of 69).

`JdkSslEngineTest.TestRSASSAPSS` failed intermittently on this host, with this change and without it:
- In ssl-only runs it failed in 2 of 8 runs without the change and 3 of 6 with it.
- In full `Netty.NET.Handler.Tests` runs 3 to 7 of its rows failed per run, also without the uncommitted transport change
  (`AbstractChannel.cs`).
- Run alone, it passed 6 of 6 times.

The cause was not this change but `SslContext.BuildKeyManagerFactory`, fixed the same day (see
[handler-porting.md](handler-porting.md#lead-decisions-2026-11-24), lead decision 5): the server key store holds its key
entry's certificate twice, and finalizing the unused entry deleted the shared key.

## CLR adaptations

- **Futures and promises.** `Promise<T>` is `TaskCompletionSource<T>` (created without
  `RunContinuationsAsynchronously`, as codec-http2's `Http2CodecUtil.NewPromise()`), and each listener is a continuation
  on Common's `EventExecutorTaskScheduler` over the event loop that owns the promise, so it runs on that event loop as the
  `DefaultPromise` listener does. `ScheduledFuture` of the response timeout is the `CancellationTokenSource` of
  `ctx.Executor.ScheduleAsync`, as handler's `AbstractSniHandler`. `ChannelPromise` is the returned `Task` of each
  outbound operation ([transport-porting.md](transport-porting.md)).
- **Types.** `X509Certificate` is `X509Certificate2`; `java.util.Date` is `DateTimeOffset` (UTC) and the optional
  `nextUpdate` a nullable one; `java.net.URL` is `Uri` (`URL.getPath()` keeps its empty result for a URL without a path);
  `InetAddress` is `IPAddress`; `SecureRandom` is `RandomNumberGenerator`; `ChannelFactory<T>` is `Func<T>`, as resolver-dns
  takes it; `AttributeKey<Boolean>` is `AttributeKey<object>` with a boxed bool, as resolver-dns's
  `DNS_PIPELINE_ATTRIBUTE`.
- **Exceptions.** `OCSPException`, `OperatorCreationException` and `CertificateException` are `CryptographicException`;
  `IllegalStateException` is `InvalidOperationException`, `IllegalArgumentException` `ArgumentException`, and
  `NullPointerException` `ArgumentNullException`, from which `NoOcspResponderException` derives
  ([codec-base-porting.md](codec-base-porting.md)).
- **Responder certificate chain.** `CertPathBuilder` ("PKIX") with the issuer as the only trust anchor:
  - A PKIX trust anchor can be any CA certificate, while the BCL chain engine anchors a chain at a self-signed root.
    With an intermediate CA as the only `CustomTrustStore` certificate, Windows reports `PartialChain` (measured with
    `validateSignatureWithIncludedChainSucceeds` and the apple.com responder).
  - So the `X509Chain` is built with the response's certificates and the issuer in its `ExtraStore`. The path is the
    part before the first element that matches the issuer by name and key.
  - The path is trusted when that element exists and no element of the path has a status. Its size, which must be 1, is
    the anchor's index.
  - `X500Principal.equals` is equal DER or equal decoded names ignoring case, and `PublicKey.equals` equal
    `SubjectPublicKeyInfo` encodings.
- **Names.** `OcspResponse.status()` is `Status_()`, because a method cannot share the nested `Status` enum's name (as
  pkitesting's `Algorithm_`); the protected static `createDefaultResolver`, which the tests reach through package access,
  has the internal forwarder `InvokeCreateDefaultResolver` (as handler-proxy's `InvokeNewInitialMessage`).
- **Value types.** `checkNotNull` of `OcspResponse`'s status and `thisUpdate`, and the validator's `thisUpdate == null`
  check, have no counterpart: the enum and `DateTimeOffset` cannot be null, and `thisUpdate` is a required field of the
  response.
- **assert.** `assert certificates.length >= 2` is `Debug.Assert`, as elsewhere in the port.

Kept as in Java: when the `BasicOCSPResponse` of a successful OCSP response cannot be decoded, `OcspClient` sets the
failure with `future.cause()` of the successful future, which is null. `DefaultPromise.setFailure(null)` then throws, the
listener's exception is only logged, and the response promise stays incomplete. In C#, `SetException(null)` throws
`ArgumentNullException`, which faults only the discarded continuation.

## Not ported

| Unit | Reason |
| --- | --- |
| `OcspClientHandler` | Reads the OCSP response stapled by the peer from `ReferenceCountedOpenSslEngine` (netty-tcnative), which handler records as `library-not-ported`; `SslStream` exposes no stapled response. |
| 12 of the 14 `OcspTest` cases | They run `SslProvider.OPENSSL`/`OPENSSL_REFCNT` engines (CLR-inapplicable); their comments are kept in `OcspTest.cs`. The two JDK cases run without the class-level `OpenSsl.isOcspSupported()` assumption, as they use only the JDK provider. |
| `package-info.java`, `module-info.yml` | JVM-only descriptors, recorded in the manifest (package summary in the namespace table above; the module maps to the project file). |

## Tests

29 original tests are declared: 17 are mapped and 12 are CLR-inapplicable. The C# run has 19 cases (the three
`testCertIdBypass` scenarios are `[InlineData]` rows). On 2026-11-26 all 19 passed in each of 3 runs (Windows 11
10.0.26300, .NET 10.0.8). `SimpleOcspQueryTest` and `ConnectUsingHttpAndValidateCertificateUsingOcspTest` reach apple.com
and its OCSP responder over the internet, as the Java tests do; they validated Apple's delegated OCSP responder
certificate through the chain check above.

### Test CAs in the Windows certificate store (2026-11-27)

On Windows, `SslStreamCertificateContext.Create` adds the certificates passed with the server certificate to the
current user's intermediate certification authority store (`CurrentUser\CA`), with or without `offline`, so that
SChannel can send them; nothing removes them (measured with a BCL-only probe: the store grew by the issuer on each
`Create`). Java's JSSE context keeps the chain in memory and writes to no store, so the Java test needs no cleanup.
Each `ValidateCertificate` run creates a new `CN=OcspIssuerCA` with a new key, as the source does; after two days of
runs the store held 72 of them, and `X509Chain.Build` of the next server certificate (which, as in the source, has no
authority key identifier, so the issuer is looked up by subject) threw "An unknown chain building error occurred": the
6 cases of `OcspServerCertificateValidatorTest` that build a server context failed in every run, while an issuer with
a unique subject built. The test now removes the issuer it created from `CurrentUser\CA` (by thumbprint, Windows only)
at the end of `ValidateCertificate`, so a run leaves the store as the Java test does. The 73 accumulated test CAs (72
`CN=OcspIssuerCA` and one left by the diagnostic probe, no private keys) were backed up and removed with the owner's
approval. Afterwards all 19 cases passed in each of 3 runs and the store kept its 14 certificates.
