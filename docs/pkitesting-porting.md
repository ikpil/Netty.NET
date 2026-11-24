# pkitesting porting

Netty.NET ports `netty-pkitesting` of Netty 4.2.19.Final (pinned commit
`64cc10f38ea5f5bd7eae48507817c66680d0afdc`) to `src/Netty.NET.PkiTesting`, with its original tests in
`test/Netty.NET.PkiTesting.Tests`. The module builds X.509 certificates, certificate paths, private keys and
certificate revocation lists for tests; the handler ssl port uses it ([handler-ssl-design.md](handler-ssl-design.md),
owner decision 4). The [manifest](pkitesting-porting-manifest.json) records every pinned file of `pkitesting/src`
(10 files) with its C# counterpart, its CLR adaptations and evidence, or the reason it is not ported. Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module pkitesting
```

The checker verifies the file inventory and source hashes, that every original comment (license, Javadoc and line
comments) is present in the mapped C# files or recorded as belonging to a member whose code uses a JVM-only API, that
every declared JUnit test is mapped to a C# test of the same name (case may differ), and the facts and evidence of
every unit that is not ported. It fails while any entry is still `pending`.

| Status | Files |
| --- | --- |
| `clr-adapted` | 7 |
| `verified` (tests whose every original method is mapped) | 1 |
| `jvm-only-replaced` | 2 |

All 18 declared original tests are mapped to C# tests with the same names; none is CLR-inapplicable. On this host
(Windows 11 10.0.26300, .NET 10.0.8) the 74 test results are 37 passed and 37 skipped: the skips are the algorithms the
BCL does not support here (Ed25519, Ed448, the 12 SLH-DSA sets), RSA 4096/8192 (skipped by the Java test itself) and
the ML-KEM rows of `createCertOfEveryKeyType` (ML-KEM cannot sign), as Java's assumptions skip them. 11 original
comments of members that only serve a JCA provider, random source or JSSE factory type are recorded as
`jvm-only-replaced`.

## Namespaces and assembly (owner decisions, 2026-11-24)

The project, folder and assembly are `Netty.NET.PkiTesting` (word casing; the package has no type to take the casing
from), and the module is ported on the BCL.

| Java package | C# namespace | Original package summary |
| --- | --- | --- |
| `io.netty.pkitesting` | `Netty.NET.PkiTesting` | Tools for generating X.509 certificates for testing. |
| test `io.netty.pkitesting` | `Netty.NET.PkiTesting.Tests` | |

The project references Netty.NET.Common only (`NetUtil`, `ObjectUtil`, `EmptyArrays`); `module-info.yml` (which also
requires `org.bouncycastle.provider` and `jdk.crypto.ec`) maps to the project file.

## Bouncy Castle and the JDK replaced by the BCL

| Java (Bouncy Castle / JDK) | C# (BCL) |
| --- | --- |
| `V3TBSCertificateGenerator`, `ExtensionsGenerator`, `Signed` + `CertificateFactory` | `CertificateRequest(subject, PublicKey, HashAlgorithmName)`, its `CertificateExtensions`, and `Create(issuerName, X509SignatureGenerator, notBefore, notAfter, serialNumber)`, which encodes, signs and returns the `X509Certificate2` |
| `Signed` (sign a TBS structure, wrap it as `SEQUENCE { tbs, algId, signature }`) | `Signed : X509SignatureGenerator`; the BCL builders do the wrapping and pass the to-be-signed bytes to `SignData` |
| `TBSCertList` (`CertificateList`) | `CertificateRevocationListBuilder.Build(issuerName, Signed, ...)` |
| `GeneralName`, `DistributionPoint` objects | their DER encodings (`byte[]`): `SubjectAlternativeNameBuilder` for dNSName/rfc822Name/URI/iPAddress, `CertificateRevocationListBuilder.BuildCrlDistributionPointExtension` for a URI distribution point, `AsnWriter` for otherName, directoryName, registeredID and a distribution point with a cRLIssuer |
| `BasicConstraints`, key usage `DERBitString`, `ExtendedKeyUsage`, `GeneralNames` | `X509BasicConstraintsExtension`, `X509KeyUsageExtension` (built from the Java BIT STRING bytes), `X509EnhancedKeyUsageExtension`, `X509SubjectAlternativeNameExtension` |
| `DERUTF8String`, `DERIA5String`, custom `Extension` | `AsnWriter.WriteCharacterString`, `X509Extension(oid, rawData, critical)` |
| `KeyPairGenerator` / `Signature` engines and providers | the BCL key factories (`ECDsa.Create(ECCurve)`, `RSA.Create(int)`, `MLDsa`/`MLKem`/`SlhDsa.GenerateKey`) and each key type's `SignData`/`VerifyData` (`Algorithms.KeyPairGenerator`, `Sign`, `Verify`) |
| `com.sun.net.httpserver.HttpServer` | `System.Net.HttpListener` |
| `X500Principal`, `X509Certificate`, `Instant`, `BigInteger`, `OptionalInt`, `URI`, `InetAddress`, `File` | `X500DistinguishedName`, `X509Certificate2`, `DateTimeOffset`, `System.Numerics.BigInteger` (nullable where Java passes `null`), `int?`, `Uri`, `IPAddress`, `FileInfo` |

Evidence: [CertificateRequest.Create](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.certificaterequest.create?view=net-10.0),
[X509SignatureGenerator](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509signaturegenerator?view=net-10.0),
[CertificateRevocationListBuilder.Build](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.certificaterevocationlistbuilder.build?view=net-10.0),
[BuildCrlDistributionPointExtension](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.certificaterevocationlistbuilder.buildcrldistributionpointextension?view=net-10.0),
[SubjectAlternativeNameBuilder](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.subjectalternativenamebuilder?view=net-10.0),
[AsnWriter](https://learn.microsoft.com/en-us/dotnet/api/system.formats.asn1.asnwriter?view=net-10.0),
[HttpListener](https://learn.microsoft.com/en-us/dotnet/api/system.net.httplistener?view=net-10.0).

## Key objects (decision for owner review)

The design named `AsymmetricAlgorithm` for `PublicKey`/`PrivateKey`/`KeyPair`. `MLDsa`, `MLKem` and `SlhDsa` derive
from `Object`, not from `AsymmetricAlgorithm` (measured by reflection on .NET 10.0.8; the
[MLDsa](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.mldsa?view=net-10.0) page lists
`Object` as its base), so an `AsymmetricAlgorithm`-typed API could not hold the ML-DSA and ML-KEM keys that the BCL
supports on this host. The port therefore types a key pair as `object`:

- A generated or given key pair is the BCL key object (`ECDsa`, `RSA`, `MLDsa`, `MLKem` or `SlhDsa`), which holds both
  keys. `Algorithm.GenerateKeyPair()`, `CertificateBuilder.KeyPair(object)`, `X509Bundle.GetKeyPair()` and
  `FromRootCertificateAuthority`/`FromCertificatePath` use it.
- A key pair without a private key (`publicKey(PublicKey)`, Java `new KeyPair(publicKey, null)`) is the BCL
  [`PublicKey`](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.publickey?view=net-10.0),
  which has a constructor for every key type and is what `CertificateRequest` takes. `CertificateBuilder.PublicKey`
  takes a `PublicKey`.
- `Algorithms.GetPublic`/`GetPrivate`/`GetEncoded` are the `KeyPair.getPublic()`/`getPrivate()` and
  `PrivateKey.getEncoded()` counterparts. `getPrivateKeyPEM()` of a bundle without a private key throws
  `NullReferenceException`, Java's `NullPointerException`.
- The private key is not attached to the certificates (`X509Bundle` keeps it next to them, as Java does); see ML-DSA.

## Algorithm support

`Algorithm.isSupported()` reports what the BCL can do on the running platform: the named parameter specs are the BCL
`MLDsaAlgorithm`/`MLKemAlgorithm`/`SlhDsaAlgorithm` values when `MLDsa.IsSupported`/`MLKem.IsSupported`/
`SlhDsa.IsSupported` is true. `supportSigning()` stays a property of the algorithm, as its Javadoc says.

| Algorithm | BCL | This host |
| --- | --- | --- |
| `ecp256`, `ecp384` | `ECDsa.Create(ECCurve.NamedCurves.nistP256/nistP384)` (secp256r1/secp384r1, [RFC 5480](https://www.rfc-editor.org/rfc/rfc5480)) | supported |
| `rsa2048` ... `rsa8192` | `RSA.Create(bits)`; public exponent 65537 = F4 (measured) | supported |
| `ed25519`, `ed448` | no BCL type; the Bouncy Castle `EdDSAParameterSpec` fallback is Bouncy Castle | unsupported everywhere: constants kept, `isSupported()` false, `algorithm(...)` throws `NotSupportedException` |
| `mlDsa44/65/87` | `MLDsa.GenerateKey` | supported |
| `mlKem512/768/1024` | `MLKem.GenerateKey` (cannot sign) | supported |
| `slhDsa*` (12 sets) | `SlhDsa.GenerateKey` ([Experimental]) | unsupported (`SlhDsa.IsSupported` false) |

The PQC members that carry `[Experimental]` (SYSLIB5006, "Types for post-quantum cryptography (PQC) are experimental",
[overview](https://learn.microsoft.com/en-us/dotnet/fundamentals/syslib-diagnostics/experimental-overview)) are used
inside `#pragma warning disable SYSLIB5006` regions with a comment. On .NET 10.0.8 these are the `PublicKey(MLDsa)`,
`PublicKey(MLKem)` and `PublicKey(SlhDsa)` constructors, the PQC PKCS #8 exports, the PQC `CopyWithPrivateKey`
overloads, `PublicKey.GetMLDsaPublicKey()` and the whole `SlhDsa` type. `MLDsa`/`MLKem` key generation, signing and
`IsSupported` are not experimental.

## ML-DSA

The design probe's `new CertificateRequest(name, mlDsaKey).CreateSelfSigned(...)` threw `CryptographicException` "The
parameter is incorrect". The stack trace shows that signing is not the cause. `CertificateRequest.Create(name,
X509SignatureGenerator.CreateForMLDsa(key), ...)` succeeds for ML-DSA-44/65/87 and the certificate verifies.
`CreateSelfSigned` fails only when it attaches the private key: `X509Certificate2.CopyWithPrivateKey(MLDsa)` calls
`MLDsaImplementation.CreateEphemeralCng()`, which exports the key with `BCryptExportKey` and imports it with
`CngKey.Import`. The CNG key import rejects the ML-DSA key blob with NTE_INVALID_PARAMETER on this Windows build. The
call fails the same way for a key re-imported from PKCS #8. `CopyWithPrivateKey(MLKem)` throws
`PlatformNotSupportedException` ("Algorithm 'MLKem' is not supported on this platform").

The port therefore signs every certificate with `CertificateRequest.Create(issuer, Signed, ...)` and keeps the key
pair in the bundle without attaching it. ML-DSA self-signed and issued certificates and ML-DSA-signed ML-KEM
certificates work (`createCertOfEveryKeyType`, `createMlKemCerts` run and pass). Only the conversions that need a
certificate carrying its key fail for ML-DSA and ML-KEM bundles on this host: `ToKeyStore`, `ToTempKeyStoreFile` and
`ToKeyManagerFactory`.

## RevocationServer on HttpListener

- **Server.** `HttpListener` with the prefixes `http://127.0.0.1:<port>/` and `http://localhost:<port>/` starts
  without administrator rights here. A prefix needs an explicit port, so the random port that
  `HttpServer.create(loopback:0)` binds is taken from a loopback `TcpListener` bound to port 0. The listener starts in
  the constructor and retries if the port was taken in between.
- **Dispatch.** One dispatch loop on the thread pool handles the requests one at a time, as `HttpServer`'s default
  executor does. Thread-pool threads are background threads
  ([foreground and background threads](https://learn.microsoft.com/en-us/dotnet/standard/threading/foreground-and-background-threads)),
  so the singleton never keeps the process alive. The Java `isDaemon()`/ForkJoin branch exists only because Java
  threads inherit daemon status, which .NET threads do not.
- **CRL lifetime (difference).** The `@implNote` sets nextUpdate to thisUpdate. Measured with `X509Chain` and
  `X509RevocationMode.Online` on this host, the Windows chain engine reports `OfflineRevocation` and
  `RevocationStatusUnknown` for such a CRL, because it is already expired. The C# server sets nextUpdate ten seconds
  after thisUpdate. With that, a revoked certificate is reported `Revoked`, a valid one passes, and an expired CRL is
  fetched again on the next check. Each issuer keeps its own path `/crl/N.crl`, so a test's CRL is fetched after its
  revocations.
- **CRL contents (difference).** `CertificateRevocationListBuilder` always writes nextUpdate, as RFC 5280 5.1.2.5
  requires. It adds a CRL Number (thisUpdate in Unix milliseconds, increasing) and an Authority Key Identifier (issuer
  name and serial number, because no Subject Key Identifier is written). It omits an empty revokedCertificates list,
  where Java writes an empty SEQUENCE.
- **Other mappings.** The `issuers` map is keyed by the SHA-256 hash of the certificate encoding: `Certificate.equals`
  compares encodings, while `X509Certificate.Equals` compares issuer and serial number. `register` takes a lock, because
  `GetOrAdd`'s value factory "may be called multiple times" where `computeIfAbsent` runs once.

## X509Bundle conversions

The JSSE/JCA conversions are ported with SslStream-ready BCL objects. Members whose only purpose is a JCA type name have
no counterpart and are recorded as `jvm-only-replaced`, with their Javadocs (owner decision 2026-11-16: JVM-only API
families are replaced without facades).

| Java member | C# |
| --- | --- |
| `toTrustManagerFactory()` | `X509ChainPolicy`: `CustomRootTrust` with the root in `CustomTrustStore` and `RevocationMode` `NoCheck`. "If the TrustManagerFactory.init(KeyStore) method is used, then default PKIX parameters are used with the exception that revocation checking is disabled" ([JSSE guide](https://docs.oracle.com/en/java/javase/17/security/java-secure-socket-extension-jsse-reference-guide.html)), and "Online is the default value" of `RevocationMode`. This is the type of `SslClientAuthenticationOptions`/`SslServerAuthenticationOptions.CertificateChainPolicy`. |
| `toTrustManager()` | an `X509Chain` with that policy; `Build` is `checkClientTrusted`/`checkServerTrusted` |
| `toKeyStore(char[])` | `byte[]`: the PKCS #12 of an `X509Certificate2Collection` holding the root and, with a private key, the leaf carrying the key plus the intermediates, protected by `keyEntryPassword` |
| `toTempKeyStoreFile(char[])`, `toTempRootCertPem()`, `toTempCertChainPem()`, `toTempPrivateKeyPem()` | `FileInfo`; the files are deleted on `AppDomain.ProcessExit` (`deleteOnExit`) |
| `toKeyManagerFactory()` | `SslStreamCertificateContext.Create(leaf with key, intermediates, offline: true)`. The leaf is loaded back from the PKCS #12. With the ephemeral `CopyWithPrivateKey` key, the Windows `SslStream` server fails with "Authentication failed because the platform does not support ephemeral keys", while the reloaded key completes a TLS 1.3 handshake against `ToTrustManagerFactory()` (both measured). The loaded PKCS #12 entries live as long as the returned context (see Key lifetime below). |
| `getTrustAnchor()` | not ported: `TrustAnchor` has no BCL type; the root in `CustomTrustStore` is the anchor |
| `toTrustManagerFactory(String)`, `toKeyManagerFactory(String)` | not ported: JCA factory algorithm names |
| `toKeyStore(String, char[])` | not ported: JCA key store types; the BCL writes PKCS #12 |
| `toTempKeyStoreFile(char[], char[])` | not ported: a key entry password separate from the store password, which `X509Certificate2Collection.Export` cannot write |

**Key lifetime (fixed 2026-11-24, found by the handler ssl tests).** For a self-signed bundle, `ToKeyStore` stores the
same certificate twice (the root and the key entry), so loading the PKCS #12 returns two certificates that both report
`HasPrivateKey` and share one imported key container (measured for rsa2048 and ecp256). `ToKeyManagerFactory` used to
keep only the target entry; when the garbage collector finalized the other one, the key was deleted with it and the
`SslStream` server failed in `AcquireCredentialsHandle` with `SEC_E_UNKNOWN_CREDENTIALS` (0x8009030D, "The credentials
supplied to the package were not recognized"): 6 of 6 runs with a forced `GC.Collect()` and
`GC.WaitForPendingFinalizers()` before the handshake, and about 1 in 70 runs of
`PkiTestingTlsTest.connectWithClassicalAlgorithms` without. `ToKeyManagerFactory` now keeps the loaded entries in a
static `ConditionalWeakTable<SslStreamCertificateContext, X509Certificate2Collection>` keyed by the returned context,
which "automatically removes the key/value entry as soon as no other references to a key exist outside the table"
([ConditionalWeakTable](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.conditionalweaktable-2?view=net-10.0)),
so the key lives exactly as long as the context. The pkitesting results are unchanged (37 passed, 37 skipped); the
handler's `SslContext.BuildKeyManagerFactory` was not affected, because it keeps every non-target entry as an
additional certificate ([handler-porting.md](handler-porting.md#lead-decisions-2026-11-24), lead decision 5).

## Other CLR adaptations

- **Provider and SecureRandom.** `CertificateBuilder.provider(Provider)`, `secureRandom(SecureRandom)`,
  `Algorithm.generateKeyPair(SecureRandom, Provider)` and `RevocationServer.register(X509Bundle, Provider)` are not
  ported: the BCL has no provider registry, and its key factories take no random source. `generateKeyPair(SecureRandom)`
  is `GenerateKeyPair()`. The random serial is 20 `RandomNumberGenerator` bytes with the top bit cleared, the range
  of `new BigInteger(159, random)`. The two comments inside `Algorithms.keyPairGenerator` describe provider behaviour
  and are recorded as JVM-only.
- **Signature algorithms.** `Signed` keeps the Java algorithm names. It signs with `Algorithms.Sign`: ECDSA in the
  DER `Rfc3279DerSequence` format, RSA with PKCS #1 v1.5, and pure ML-DSA/SLH-DSA. The AlgorithmIdentifier keeps Java's
  `SEQUENCE { OID }` without parameters (`oidForAlgorithmName`). A CRL is signed with the issuer certificate's own
  signature algorithm (`getSigAlgName`, `Algorithms.GetSigAlgName`), as in Java.
- **Names.** `algorithm(Algorithm)` is `Algorithm_(Algorithm)`: a method cannot share the nested `Algorithm` class's
  name ([CS0102](https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0102)), as handler's `Builder_()`
  ([handler-porting.md](handler-porting.md)). The other methods keep their Java names in PascalCase.
- **Java enums.** `Algorithm`, `KeyUsage` and `ExtendedKeyUsage` are sealed classes with static readonly constants, as
  codec-base's `Base64Dialect`, with `Name()`, `ToString()` and `Values()`. Their fields are `internal` where
  `CertificateBuilder` reads them, because a C# private member is limited to its containing type.
- **Extensions.**
  - The key usage BIT STRING is computed as in Java and wrapped in `X509KeyUsageExtension`, so even an empty usage list
    keeps Java's bytes (`03 02 07 00`).
  - Extended key usages are written in ordinal (`TreeSet`) order. The SAN extension is critical when the subject is
    empty.
  - A CRL distribution point with a cRLIssuer is written with `AsnWriter`, because the BCL builder has no issuer
    parameter.
  - `dnsName` keeps `URI.create("ip://" + name).getHost()` as `new Uri(...).Host`. That lower-cases the host and throws
    `UriFormatException` for a host that `java.net.URI` treats as registry-based, such as a wildcard (Java then fails
    with a null host).
- **ACME identifier (finding).** `addAcmeIdentifierExtension(sha256)` writes the given bytes as the extnValue content,
  in Java and in C#. [RFC 8737](https://www.rfc-editor.org/rfc/rfc8737) section 3 defines that content as "the ASN.1
  DER encoding [X.690] of the Authorization structure", `Authorization ::= OCTET STRING (SIZE (32))`. A conforming
  extension therefore needs the caller to pass the DER OCTET STRING of the digest. The Java behaviour is kept.
- **Certificate accessors.** `X509Certificate2` lacks several `X509Certificate` accessors, and `Algorithms` supplies
  them:
  - `getSigAlgName`, `getSerialNumber` and `getBasicConstraints`;
  - `getTBSCertificate`, `getSignature` and the unique identifiers, read from the DER certificate with `AsnReader`.
- **Comparisons.** Certificates are compared by `RawData` and names by their DER encoding: `X500Principal.equals`
  compares canonical names, and `X500DistinguishedName` has no equality. The `X500DistinguishedName(string)`
  constructor encodes `"CN=netty.io, O=Netty"` with `O` first, as the RFC 2253 parser of `X500Principal` does
  (measured).
- **Exceptions.** `IllegalStateException` is `InvalidOperationException`, `IllegalArgumentException` is
  `ArgumentException`, `UnsupportedOperationException` is `NotSupportedException`, and `GeneralSecurityException`/
  `KeyStoreException` are `CryptographicException`. `requireNonNull` is `ObjectUtil.CheckNotNull`
  (`ArgumentNullException`), with no counterpart for the value types `DateTimeOffset` and `int?`.
- **Serial number.** `CertificateRequest.Create` reads the serial as an unsigned big-endian integer, so a negative
  serial throws `OverflowException`. RFC 5280 4.1.2.2 requires a positive serial; Bouncy Castle writes a negative one.

## Not ported

| Unit or member | Status | Reason |
| --- | --- | --- |
| `package-info.java` | `jvm-only-replaced` | C# namespaces cannot carry documentation comments; the summary is in the table above. |
| `module-info.yml` | `jvm-only-replaced` | JPMS module descriptor; `Netty.NET.PkiTesting.csproj` references Netty.NET.Common, and `InternalsVisibleTo` opens internals to the test assembly only. |
| `Algorithms.bouncyCastle()`, `DefaultProvider`, provider fallback | comments recorded as `jvm-only-replaced` | Bouncy Castle and JCA providers; the BCL factories replace them. |
| `CertificateBuilder.provider`, `secureRandom`, `Algorithm.generateKeyPair(SecureRandom, Provider)`, `SecureRandomHolder` | comments recorded as `jvm-only-replaced` | JCA Provider/SecureRandom (see Other CLR adaptations). |
| `RevocationServer.register(X509Bundle, Provider)` | comment recorded as `jvm-only-replaced` | JCA Provider. |
| `X509Bundle.getTrustAnchor()`, `toTrustManagerFactory(String)`, `toKeyStore(String, char[])`, `toTempKeyStoreFile(char[], char[])`, `toKeyManagerFactory(String)` | comments recorded as `jvm-only-replaced` | See X509Bundle conversions. |

## Test notes

- **JCA/JSSE checks on the BCL.**
  - A certificate signature is verified with `Algorithms.Verify` over the tbsCertificate and signature read from the
    DER certificate (`Signature.initVerify/update/verify`).
  - The PKIX trust manager is an `X509Chain` with `CustomRootTrust`. `getX509TrustManager` sets
    `X509RevocationMode.Online` and `ExcludeRoot` (the `PKIXRevocationChecker`).
  - `CheckClientTrusted` adds the intermediates to `ExtraStore` and requires the client-authentication EKU. It throws a
    `CryptographicException` naming the chain status flags, so the revoked case asserts `Revoked` in its message.
  - The CRL is fetched by the Windows chain engine from the `HttpListener` server.
- **Skips.** `assumeTrue`/`@EnabledForJreRange` are `Assert.SkipUnless`/`SkipWhen`. `createMlKemCerts` (Java 24+) runs
  where ML-KEM and ML-DSA are supported. `createEdCertsWithProvider` (Java 11 and older, where the JVM has no EdDSA) runs
  while the BCL has no EdDSA and expects `NotSupportedException` from `Algorithm_(ed25519/ed448)`, because there is no
  provider to pass. The two `createCertIssuedByDifferentAlgorithmEd*` tests skip while Ed25519/Ed448 are unsupported.
- **Providers.** `createKeyPairWithProvider` generates the keys with the platform implementation (no
  `BouncyCastleProvider` to choose). `providers()` keeps only its default (`null`) row, so the CRL tests run once. Their
  unused parameter is allowed with an `xUnit1026` pragma and a reason.
- **Certificate fields.** `createCertificateWithSans` decodes the SAN extension into Java's `[type, value]` lists as
  Java 19 and later do. Its negative Java byte literals are unchecked byte casts with the same bits.
  `createCertificateWithOtherFields` maps the remaining accessors:
  - `hasUnsupportedCriticalExtension()` is the absence of `X509ChainStatusFlags.HasNotSupportedCriticalExtension`;
  - `getKeyUsage()` is the nine bits of the `X509KeyUsageExtension`;
  - `getNotBefore().getTime()` is the Unix milliseconds of the local-time `NotBefore`.
- **Key identity.** `generateCertificateWithGivenPublicKey` keeps the `PublicKey` it passes and checks that the bundle
  holds that object. `GetPublic` makes a new `PublicKey` on each call.
