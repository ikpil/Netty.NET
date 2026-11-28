/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec.Base64Codec;
using Netty.NET.Transport.Channels;
using AuthSslProtocols = System.Security.Authentication.SslProtocols;

namespace Netty.NET.Handler.Ssl;

/**
 * Constants for SSL packets.
 */
// CLR: SslStream is the only TLS engine (ssl design decision 2, docs/handler-ssl-design.md), so the JDK lookups of
// the source answer for SslStream and the operating system's TLS stack it uses ("Allows the operating system to
// choose the best protocol to use", SslProtocols.None,
// https://learn.microsoft.com/en-us/dotnet/api/system.security.authentication.sslprotocols?view=net-10.0):
// isTLSv13SupportedByJDK/isTLSv13EnabledByJDK read the protocol SslStream negotiates in an in-memory handshake
// with the probing certificate (see ProbeNegotiatedProtocol), as Netty's JdkSslServerContext and OpenSsl probe
// their engines with PROBING_CERT/PROBING_KEY. java.security.Provider (a JCA provider) has no BCL counterpart, so
// the Provider parameters, newInitContext(Provider), getSSLContext(Provider[, SecureRandom]) and getTlsVersion(),
// which build a javax.net.ssl.SSLContext, are not ported. ByteBuf has no mutable byte order in this port ("Java's
// mutable ByteOrder facade is replaced by explicit BE/LE access", docs/buffer-porting.md), so the ByteBuf readers
// always read big-endian, which is what the order checks of the source produce; a java.nio.ByteBuffer is a
// ReadOnlyMemory<byte> (its position..limit), read big-endian with BinaryPrimitives. SSLHandshakeException is
// AuthenticationException (ssl design decision 3). The class is static: the source's private constructor only
// prevents instantiation.
internal static class SslUtils
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(SslUtils));

    // See https://tools.ietf.org/html/rfc8446#appendix-B.4
    // The port only checks membership, so the set does not need insertion order.
    internal static readonly ISet<string> TLSV13_CIPHERS = new ReadOnlySet<string>(new HashSet<string>(
            new[] {"TLS_AES_256_GCM_SHA384", "TLS_CHACHA20_POLY1305_SHA256",
                          "TLS_AES_128_GCM_SHA256", "TLS_AES_128_CCM_8_SHA256",
                          "TLS_AES_128_CCM_SHA256"}));

    // CLR: (short) 0xFEFF and the other DTLS versions are negative Java shorts; unchecked((short)...) keeps those
    // values, which getEncryptedPacketLength compares with the signed ByteBuf.GetShort result as the source does.
    internal const short DTLS_1_0 = unchecked((short) 0xFEFF);
    internal const short DTLS_1_2 = unchecked((short) 0xFEFD);
    internal const short DTLS_1_3 = unchecked((short) 0xFEFC);
    internal const short DTLS_RECORD_HEADER_LENGTH = 13;

    /**
     * GMSSL Protocol Version
     */
    internal const int GMSSL_PROTOCOL_VERSION = 0x101;

    internal const string INVALID_CIPHER = "SSL_NULL_WITH_NULL_NULL";

    /**
     * change cipher spec
     */
    internal const int SSL_CONTENT_TYPE_CHANGE_CIPHER_SPEC = 20;

    /**
     * alert
     */
    internal const int SSL_CONTENT_TYPE_ALERT = 21;

    /**
     * handshake
     */
    internal const int SSL_CONTENT_TYPE_HANDSHAKE = 22;

    /**
     * application data
     */
    internal const int SSL_CONTENT_TYPE_APPLICATION_DATA = 23;

    /**
     * HeartBeat Extension
     */
    internal const int SSL_CONTENT_TYPE_EXTENSION_HEARTBEAT = 24;

    /**
     * the length of the ssl record header (in bytes)
     */
    internal const int SSL_RECORD_HEADER_LENGTH = 5;

    /**
     * Not enough data in buffer to parse the record length
     */
    internal const int NOT_ENOUGH_DATA = -1;

    /**
     * data is not encrypted
     */
    internal const int NOT_ENCRYPTED = -2;

    internal static readonly string[] DEFAULT_CIPHER_SUITES;
    internal static readonly string[] DEFAULT_TLSV13_CIPHER_SUITES;
    internal static readonly string[] TLSV13_CIPHER_SUITES = { "TLS_AES_128_GCM_SHA256", "TLS_AES_256_GCM_SHA384" };

    // self-signed certificate for netty.io and the matching private-key
    internal const string PROBING_CERT = "-----BEGIN CERTIFICATE-----\n" +
            "MIICrjCCAZagAwIBAgIIdSvQPv1QAZQwDQYJKoZIhvcNAQELBQAwFjEUMBIGA1UEAxMLZXhhbXBs\n" +
            "ZS5jb20wIBcNMTgwNDA2MjIwNjU5WhgPOTk5OTEyMzEyMzU5NTlaMBYxFDASBgNVBAMTC2V4YW1w\n" +
            "bGUuY29tMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAggbWsmDQ6zNzRZ5AW8E3eoGl\n" +
            "qWvOBDb5Fs1oBRrVQHuYmVAoaqwDzXYJ0LOwa293AgWEQ1jpcbZ2hpoYQzqEZBTLnFhMrhRFlH6K\n" +
            "bJND8Y33kZ/iSVBBDuGbdSbJShlM+4WwQ9IAso4MZ4vW3S1iv5fGGpLgbtXRmBf/RU8omN0Gijlv\n" +
            "WlLWHWijLN8xQtySFuBQ7ssW8RcKAary3pUm6UUQB+Co6lnfti0Tzag8PgjhAJq2Z3wbsGRnP2YS\n" +
            "vYoaK6qzmHXRYlp/PxrjBAZAmkLJs4YTm/XFF+fkeYx4i9zqHbyone5yerRibsHaXZWLnUL+rFoe\n" +
            "MdKvr0VS3sGmhQIDAQABMA0GCSqGSIb3DQEBCwUAA4IBAQADQi441pKmXf9FvUV5EHU4v8nJT9Iq\n" +
            "yqwsKwXnr7AsUlDGHBD7jGrjAXnG5rGxuNKBQ35wRxJATKrUtyaquFUL6H8O6aGQehiFTk6zmPbe\n" +
            "12Gu44vqqTgIUxnv3JQJiox8S2hMxsSddpeCmSdvmalvD6WG4NthH6B9ZaBEiep1+0s0RUaBYn73\n" +
            "I7CCUaAtbjfR6pcJjrFk5ei7uwdQZFSJtkP2z8r7zfeANJddAKFlkaMWn7u+OIVuB4XPooWicObk\n" +
            "NAHFtP65bocUYnDpTVdiyvn8DdqyZ/EO8n1bBKBzuSLplk2msW4pdgaFgY7Vw/0wzcFXfUXmL1uy\n" +
            "G8sQD/wx\n" +
            "-----END CERTIFICATE-----";
    internal const string PROBING_KEY = "-----BEGIN PRIVATE KEY-----\n" +
            "MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQCCBtayYNDrM3NFnkBbwTd6gaWp\n" +
            "a84ENvkWzWgFGtVAe5iZUChqrAPNdgnQs7Brb3cCBYRDWOlxtnaGmhhDOoRkFMucWEyuFEWUfops\n" +
            "k0PxjfeRn+JJUEEO4Zt1JslKGUz7hbBD0gCyjgxni9bdLWK/l8YakuBu1dGYF/9FTyiY3QaKOW9a\n" +
            "UtYdaKMs3zFC3JIW4FDuyxbxFwoBqvLelSbpRRAH4KjqWd+2LRPNqDw+COEAmrZnfBuwZGc/ZhK9\n" +
            "ihorqrOYddFiWn8/GuMEBkCaQsmzhhOb9cUX5+R5jHiL3OodvKid7nJ6tGJuwdpdlYudQv6sWh4x\n" +
            "0q+vRVLewaaFAgMBAAECggEAP8tPJvFtTxhNJAkCloHz0D0vpDHqQBMgntlkgayqmBqLwhyb18pR\n" +
            "i0qwgh7HHc7wWqOOQuSqlEnrWRrdcI6TSe8R/sErzfTQNoznKWIPYcI/hskk4sdnQ//Yn9/Jvnsv\n" +
            "U/BBjOTJxtD+sQbhAl80JcA3R+5sArURQkfzzHOL/YMqzAsn5hTzp7HZCxUqBk3KaHRxV7NefeOE\n" +
            "xlZuWSmxYWfbFIs4kx19/1t7h8CHQWezw+G60G2VBtSBBxDnhBWvqG6R/wpzJ3nEhPLLY9T+XIHe\n" +
            "ipzdMOOOUZorfIg7M+pyYPji+ZIZxIpY5OjrOzXHciAjRtr5Y7l99K1CG1LguQKBgQDrQfIMxxtZ\n" +
            "vxU/1cRmUV9l7pt5bjV5R6byXq178LxPKVYNjdZ840Q0/OpZEVqaT1xKVi35ohP1QfNjxPLlHD+K\n" +
            "iDAR9z6zkwjIrbwPCnb5kuXy4lpwPcmmmkva25fI7qlpHtbcuQdoBdCfr/KkKaUCMPyY89LCXgEw\n" +
            "5KTDj64UywKBgQCNfbO+eZLGzhiHhtNJurresCsIGWlInv322gL8CSfBMYl6eNfUTZvUDdFhPISL\n" +
            "UljKWzXDrjw0ujFSPR0XhUGtiq89H+HUTuPPYv25gVXO+HTgBFZEPl4PpA+BUsSVZy0NddneyqLk\n" +
            "42Wey9omY9Q8WsdNQS5cbUvy0uG6WFoX7wKBgQDZ1jpW8pa0x2bZsQsm4vo+3G5CRnZlUp+XlWt2\n" +
            "dDcp5dC0xD1zbs1dc0NcLeGDOTDv9FSl7hok42iHXXq8AygjEm/QcuwwQ1nC2HxmQP5holAiUs4D\n" +
            "WHM8PWs3wFYPzE459EBoKTxeaeP/uWAn+he8q7d5uWvSZlEcANs/6e77eQKBgD21Ar0hfFfj7mK8\n" +
            "9E0FeRZBsqK3omkfnhcYgZC11Xa2SgT1yvs2Va2n0RcdM5kncr3eBZav2GYOhhAdwyBM55XuE/sO\n" +
            "eokDVutNeuZ6d5fqV96TRaRBpvgfTvvRwxZ9hvKF4Vz+9wfn/JvCwANaKmegF6ejs7pvmF3whq2k\n" +
            "drZVAoGAX5YxQ5XMTD0QbMAl7/6qp6S58xNoVdfCkmkj1ZLKaHKIjS/benkKGlySVQVPexPfnkZx\n" +
            "p/Vv9yyphBoudiTBS9Uog66ueLYZqpgxlM/6OhYg86Gm3U2ycvMxYjBM1NFiyze21AqAhI+HX+Ot\n" +
            "mraV2/guSgDgZAhukRZzeQ2RucI=\n" +
            "-----END PRIVATE KEY-----";

    private static readonly bool TLSV1_3_JDK_SUPPORTED;
    private static readonly bool TLSV1_3_JDK_DEFAULT_ENABLED;

    // CLR: the bound, in milliseconds, of every blocking step of the in-memory probe handshake; a probe that does not
    // finish in time counts as a failure ("assuming no"), as an exception of the source's probe does. It is a constant
    // because the probe's client thread reads it while this class is still being initialized by the static
    // constructor: reading a static field there would wait for that initialization.
    private const int PROBE_TIMEOUT_MILLIS = 10 * 1000;

    // CLR: an explicit static constructor, so the probes run when the class is first used, as the Java class
    // initializer does.
    static SslUtils()
    {
        TLSV1_3_JDK_SUPPORTED = IsTLSv13SupportedByJDK0();
        TLSV1_3_JDK_DEFAULT_ENABLED = IsTLSv13EnabledByJDK0();

        if (TLSV1_3_JDK_SUPPORTED)
        {
            DEFAULT_TLSV13_CIPHER_SUITES = TLSV13_CIPHER_SUITES;
        }
        else
        {
            DEFAULT_TLSV13_CIPHER_SUITES = EmptyArrays.EMPTY_STRINGS;
        }

        // CLR: a List keeps the insertion order of the source's LinkedHashSet; the names are distinct, so no
        // duplicate check is needed.
        List<string> defaultCiphers = new List<string>();
        // GCM (Galois/Counter Mode) requires JDK 8.
        defaultCiphers.Add("TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384");
        defaultCiphers.Add("TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256");
        defaultCiphers.Add("TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256");
        defaultCiphers.Add("TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384");
        defaultCiphers.Add("TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA");
        // AES256 requires JCE unlimited strength jurisdiction policy files.
        defaultCiphers.Add("TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA");
        // GCM (Galois/Counter Mode) requires JDK 8.
        defaultCiphers.Add("TLS_RSA_WITH_AES_128_GCM_SHA256");
        defaultCiphers.Add("TLS_RSA_WITH_AES_128_CBC_SHA");
        // AES256 requires JCE unlimited strength jurisdiction policy files.
        defaultCiphers.Add("TLS_RSA_WITH_AES_256_CBC_SHA");

        defaultCiphers.AddRange(DEFAULT_TLSV13_CIPHER_SUITES);

        DEFAULT_CIPHER_SUITES = defaultCiphers.ToArray();
    }

    /**
     * Returns {@code true} if the JDK itself supports TLSv1.3, {@code false} otherwise.
     */
    // CLR: without a Provider parameter this is the source's provider == null branch (see the class comment).
    internal static bool IsTLSv13SupportedByJDK()
    {
        return TLSV1_3_JDK_SUPPORTED;
    }

    // CLR: getSupportedSSLParameters().getProtocols() contains TLSv1.3 when SslStream completes a handshake with
    // EnabledSslProtocols = Tls13 ("Specifies the TLS 1.3 security protocol", SslProtocols) and SslStream.SslProtocol,
    // "the security protocol used to authenticate this connection"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.sslprotocol?view=net-10.0), is Tls13.
    // The provider argument of the log message is always null.
    private static bool IsTLSv13SupportedByJDK0()
    {
        try
        {
            return ProbeNegotiatedProtocol(AuthSslProtocols.Tls13) == AuthSslProtocols.Tls13;
        }
        catch (Exception cause)
        {
            logger.Debug("Unable to detect if JDK SSLEngine with provider {} supports TLSv1.3, assuming no",
                    null, cause);
            return false;
        }
    }

    /**
     * Returns {@code true} if the JDK itself supports TLSv1.3 and enabled it by default, {@code false} otherwise.
     */
    // CLR: without a Provider parameter this is the source's provider == null branch (see the class comment).
    internal static bool IsTLSv13EnabledByJDK()
    {
        return TLSV1_3_JDK_DEFAULT_ENABLED;
    }

    // CLR: getDefaultSSLParameters().getProtocols() contains TLSv1.3 when SslStream negotiates TLS 1.3 with the default
    // EnabledSslProtocols, SslProtocols.None ("Allows the operating system to choose the best protocol to use"), on
    // both sides. The provider argument of the log message is always null.
    private static bool IsTLSv13EnabledByJDK0()
    {
        try
        {
            return ProbeNegotiatedProtocol(AuthSslProtocols.None) == AuthSslProtocols.Tls13;
        }
        catch (Exception cause)
        {
            logger.Debug("Unable to detect if JDK SSLEngine with provider {} enables TLSv1.3 by default," +
                    " assuming no", null, cause);
            return false;
        }
    }

    // CLR: replaces newInitContext(provider): a client and a server SslStream with the given EnabledSslProtocols
    // authenticate over an in-memory duplex (ProbeStream) and the protocol the server negotiated is returned. The server
    // certificate is PROBING_CERT with PROBING_KEY ("Creates a new X509 certificate from the contents of an RFC 7468
    // PEM-encoded certificate and private key",
    // https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificate2.createfrompem?view=net-10.0),
    // round-tripped through PKCS #12 because an SslStream server certificate needs a persisted key (measured,
    // docs/handler-ssl-design.md); the client accepts it without validation, as the source's empty TrustManager array
    // makes the probe independent of trust. The synchronous AuthenticateAsClient/AuthenticateAsServer overloads take the
    // option bags ("Authenticates the server and optionally the client in a client-server connection",
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.authenticateasclient?view=net-10.0) and
    // throw AuthenticationException when "The authentication failed"; the client runs on its own background thread, so
    // the probe needs no thread-pool thread.
    private static AuthSslProtocols ProbeNegotiatedProtocol(AuthSslProtocols enabledProtocols)
    {
        using X509Certificate2 ephemeral = X509Certificate2.CreateFromPem(PROBING_CERT, PROBING_KEY);
        using X509Certificate2 certificate =
                X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pkcs12), null);
        BlockingCollection<byte[]> clientToServer = new BlockingCollection<byte[]>();
        BlockingCollection<byte[]> serverToClient = new BlockingCollection<byte[]>();
        using SslStream client = new SslStream(new ProbeStream(serverToClient, clientToServer), false);
        using SslStream server = new SslStream(new ProbeStream(clientToServer, serverToClient), false);
        Exception clientFailure = null;
        Thread clientThread = new Thread(() =>
        {
            try
            {
                client.AuthenticateAsClient(new SslClientAuthenticationOptions
                {
                    TargetHost = "example.com",
                    EnabledSslProtocols = enabledProtocols,
                    RemoteCertificateValidationCallback = (_, _, _, _) => true,
                });
            }
            catch (Exception e)
            {
                clientFailure = e;
                // CLR: the server must not wait for a client that gave up.
                clientToServer.CompleteAdding();
            }
        });
        clientThread.IsBackground = true;
        clientThread.Name = "netty-ssl-probe";
        clientThread.Start();

        try
        {
            server.AuthenticateAsServer(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                EnabledSslProtocols = enabledProtocols,
            });
        }
        finally
        {
            // CLR: a failed server must not leave the client waiting either.
            serverToClient.CompleteAdding();

            if (!clientThread.Join(PROBE_TIMEOUT_MILLIS))
            {
                clientFailure ??= new TimeoutException("TLS probe client did not finish");
            }
        }

        if (clientFailure != null)
        {
            throw new AuthenticationException("TLS probe client failed", clientFailure);
        }

        return server.SslProtocol;
    }

    // CLR: one direction of the in-memory duplex of ProbeNegotiatedProtocol: Write hands a copy to the peer, Read blocks
    // until the peer wrote, its side was completed (end of stream) or PROBE_TIMEOUT_MILLIS passed.
    private sealed class ProbeStream : System.IO.Stream
    {
        private readonly BlockingCollection<byte[]> inbound;
        private readonly BlockingCollection<byte[]> outbound;
        private byte[] pending = Array.Empty<byte>();
        private int pendingOffset;

        internal ProbeStream(BlockingCollection<byte[]> inbound, BlockingCollection<byte[]> outbound)
        {
            this.inbound = inbound;
            this.outbound = outbound;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            if (pendingOffset == pending.Length)
            {
                if (!inbound.TryTake(out pending, PROBE_TIMEOUT_MILLIS))
                {
                    pending = Array.Empty<byte>();
                    pendingOffset = 0;
                    return 0;
                }

                pendingOffset = 0;
            }

            int n = Math.Min(buffer.Length, pending.Length - pendingOffset);
            pending.AsSpan(pendingOffset, n).CopyTo(buffer);
            pendingOffset += n;
            return n;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            try
            {
                outbound.Add(buffer.ToArray());
            }
            catch (InvalidOperationException e)
            {
                throw new IOException("TLS probe peer closed", e);
            }
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
    }

    internal static bool ArrayContains(string[] array, string value)
    {
        foreach (string v in array)
        {
            if (value.Equals(v))
            {
                return true;
            }
        }

        return false;
    }

    /**
     * Add elements from {@code names} into {@code enabled} if they are in {@code supported}.
     */
    internal static void AddIfSupported(ISet<string> supported, IList<string> enabled, params string[] names)
    {
        foreach (string n in names)
        {
            if (supported.Contains(n))
            {
                enabled.Add(n);
            }
        }
    }

    internal static void UseFallbackCiphersIfDefaultIsEmpty(IList<string> defaultCiphers, IEnumerable<string> fallbackCiphers)
    {
        if (defaultCiphers.Count == 0)
        {
            foreach (string cipher in fallbackCiphers)
            {
                if (cipher.StartsWith("SSL_", StringComparison.Ordinal) || cipher.Contains("_RC4_"))
                {
                    continue;
                }

                defaultCiphers.Add(cipher);
            }
        }
    }

    internal static void UseFallbackCiphersIfDefaultIsEmpty(IList<string> defaultCiphers, params string[] fallbackCiphers)
    {
        UseFallbackCiphersIfDefaultIsEmpty(defaultCiphers, (IEnumerable<string>) fallbackCiphers);
    }

    /**
     * Converts the given exception to a {@link SSLHandshakeException}, if it isn't already.
     */
    // CLR: SSLHandshakeException and SSLException are both AuthenticationException, so any AuthenticationException is
    // returned as is; new SSLHandshakeException(e.getMessage()).initCause(e) is the (message, inner) constructor.
    internal static AuthenticationException ToSSLHandshakeException(Exception e)
    {
        if (e is AuthenticationException)
        {
            return (AuthenticationException) e;
        }

        return new AuthenticationException(e.Message, e);
    }

    /**
     * Return how much bytes can be read out of the encrypted data. Be aware that this method will not increase
     * the readerIndex of the given {@link ByteBuf}.
     *
     * @param   buffer      The {@link ByteBuf} to read from.
     * @param   offset      The offset to start from.
     * @param   probeSSLv2  {@code true} if the input {@code buffer} might be SSLv2.
     * @return              The length of the encrypted packet that is included in the buffer or
     *                      {@link #SslUtils#NOT_ENOUGH_DATA} if not enough data is present in the
     *                      {@link ByteBuf}. This will return {@link SslUtils#NOT_ENCRYPTED} if
     *                      the given {@link ByteBuf} is not encrypted at all.
     */
    // CLR: getUnsignedByte is GetByte, which returns the unsigned System.Byte (owner decision); getShort is GetShort,
    // the signed short, so version keeps the Java value that is compared with the negative DTLS constants.
    internal static int GetEncryptedPacketLength(ByteBuf buffer, int offset, bool probeSSLv2)
    {
        Debug.Assert(offset >= buffer.ReaderIndex);
        int remaining = buffer.WriterIndex - offset;
        if (remaining < SSL_RECORD_HEADER_LENGTH)
        {
            return NOT_ENOUGH_DATA;
        }

        int packetLength = 0;
        // SSLv3 or TLS - Check ContentType
        bool tls;

        switch (buffer.GetByte(offset))
        {
            case SSL_CONTENT_TYPE_CHANGE_CIPHER_SPEC:
            case SSL_CONTENT_TYPE_ALERT:
            case SSL_CONTENT_TYPE_HANDSHAKE:
            case SSL_CONTENT_TYPE_APPLICATION_DATA:
            case SSL_CONTENT_TYPE_EXTENSION_HEARTBEAT:
                tls = true;
                break;
            default:
                // SSLv2 or bad data
                if (!probeSSLv2)
                {
                    return NOT_ENCRYPTED;
                }

                tls = false;
                break;
        }

        if (tls)
        {
            // SSLv3 or TLS or GMSSLv1.0 or GMSSLv1.1 - Check ProtocolVersion
            int majorVersion = buffer.GetByte(offset + 1);
            int version = buffer.GetShort(offset + 1);
            if (majorVersion == 3 || version == GMSSL_PROTOCOL_VERSION)
            {
                // SSLv3 or TLS or GMSSLv1.0 or GMSSLv1.1
                packetLength = UnsignedShortBE(buffer, offset + 3) + SSL_RECORD_HEADER_LENGTH;
                if (packetLength <= SSL_RECORD_HEADER_LENGTH)
                {
                    // Neither SSLv3 or TLSv1 (i.e. SSLv2 or bad data)
                    tls = false;
                }
            }
            else if (version == DTLS_1_0 || version == DTLS_1_2 || version == DTLS_1_3)
            {
                if (remaining < DTLS_RECORD_HEADER_LENGTH)
                {
                    return NOT_ENOUGH_DATA;
                }

                // length is the last 2 bytes in the 13 byte header.
                packetLength = UnsignedShortBE(buffer, offset + DTLS_RECORD_HEADER_LENGTH - 2) +
                        DTLS_RECORD_HEADER_LENGTH;
            }
            else
            {
                // Neither SSLv3 or TLSv1 (i.e. SSLv2 or bad data)
                tls = false;
            }
        }

        if (!tls)
        {
            // SSLv2 or bad data - Check the version
            int headerLength = (buffer.GetByte(offset) & 0x80) != 0 ? 2 : 3;
            int majorVersion = buffer.GetByte(offset + headerLength + 1);
            if (majorVersion == 2 || majorVersion == 3)
            {
                // SSLv2
                packetLength = headerLength == 2 ?
                        (ShortBE(buffer, offset) & 0x7FFF) + 2 : (ShortBE(buffer, offset) & 0x3FFF) + 3;
                if (packetLength <= headerLength)
                {
                    // If there's no data then consider this package as not encrypted.
                    return NOT_ENCRYPTED;
                }
            }
            else
            {
                return NOT_ENCRYPTED;
            }
        }

        return packetLength;
    }

    // Reads a big-endian unsigned short integer from the buffer
    // CLR: the ByteBuf has no byte order to check (see the class comment); GetUnsignedShort reads big-endian.
    private static int UnsignedShortBE(ByteBuf buffer, int offset)
    {
        int value = buffer.GetUnsignedShort(offset);
        return value;
    }

    // Reads a big-endian short integer from the buffer
    // CLR: the ByteBuf has no byte order to check (see the class comment); GetShort reads big-endian.
    private static short ShortBE(ByteBuf buffer, int offset)
    {
        short value = buffer.GetShort(offset);
        return value;
    }

    // CLR: ByteBuffer.get(int) returns a signed Java byte, so the source masks it with 0xFF; an element of a
    // ReadOnlySpan<byte> is already the unsigned value, so the mask keeps it unchanged.
    private static short UnsignedByte(byte b)
    {
        return (short) (b & 0xFF);
    }

    // Reads a big-endian unsigned short integer from the buffer
    private static int UnsignedShortBE(ReadOnlySpan<byte> buffer, int offset)
    {
        return ShortBE(buffer, offset) & 0xFFFF;
    }

    // Reads a big-endian short integer from the buffer
    // CLR: a ReadOnlySpan<byte> has no byte order, so the bytes are always read big-endian
    // ("Reads an Int16 from the beginning of a read-only span of bytes, as big endian",
    // https://learn.microsoft.com/en-us/dotnet/api/system.buffers.binary.binaryprimitives.readint16bigendian?view=net-10.0),
    // which is what the source's order check and swapShort produce.
    private static short ShortBE(ReadOnlySpan<byte> buffer, int offset)
    {
        return BinaryPrimitives.ReadInt16BigEndian(buffer.Slice(offset));
    }

    // CLR: ByteBuffer[] is ReadOnlyMemory<byte>[], each element the position..limit region of a ByteBuffer;
    // duplicate(), limit() and put() become a copy of at most the missing header bytes into tmp, and flip() is the
    // length copied.
    internal static int GetEncryptedPacketLength(ReadOnlyMemory<byte>[] buffers, int offset)
    {
        ReadOnlyMemory<byte> buffer = buffers[offset];

        // Check if everything we need is in one ByteBuffer. If so we can make use of the fast-path.
        if (buffer.Length >= SSL_RECORD_HEADER_LENGTH)
        {
            return GetEncryptedPacketLength(buffer.Span);
        }

        // We need to copy 5 bytes into a temporary buffer so we can parse out the packet length easily.
        byte[] tmp = new byte[SSL_RECORD_HEADER_LENGTH];
        int tmpPosition = 0;

        do
        {
            ReadOnlySpan<byte> next = buffers[offset++].Span;
            if (next.Length > tmp.Length - tmpPosition)
            {
                next = next.Slice(0, tmp.Length - tmpPosition);
            }

            next.CopyTo(tmp.AsSpan(tmpPosition));
            tmpPosition += next.Length;
        } while (tmpPosition < tmp.Length && offset < buffers.Length);

        // Done, flip the buffer so we can read from it.
        return GetEncryptedPacketLength(tmp.AsSpan(0, tmpPosition));
    }

    // CLR: the span starts at the ByteBuffer's position, so pos is 0; buffer.get(i) is buffer[pos + i] and
    // buffer.getShort(pos + 1) is the big-endian signed short of ShortBE (0x101 reads the same in either byte order).
    private static int GetEncryptedPacketLength(ReadOnlySpan<byte> buffer)
    {
        int remaining = buffer.Length;
        if (remaining < SSL_RECORD_HEADER_LENGTH)
        {
            return NOT_ENOUGH_DATA;
        }

        int packetLength = 0;
        int pos = 0;

        // SSLv3 or TLS - Check ContentType
        bool tls;

        switch (UnsignedByte(buffer[pos]))
        {
            case SSL_CONTENT_TYPE_CHANGE_CIPHER_SPEC:
            case SSL_CONTENT_TYPE_ALERT:
            case SSL_CONTENT_TYPE_HANDSHAKE:
            case SSL_CONTENT_TYPE_APPLICATION_DATA:
            case SSL_CONTENT_TYPE_EXTENSION_HEARTBEAT:
                tls = true;
                break;
            default:
                // SSLv2 or bad data
                tls = false;
                break;
        }

        if (tls)
        {
            // SSLv3 or TLS or GMSSLv1.0 or GMSSLv1.1 - Check ProtocolVersion
            int majorVersion = UnsignedByte(buffer[pos + 1]);
            if (majorVersion == 3 || ShortBE(buffer, pos + 1) == GMSSL_PROTOCOL_VERSION)
            {
                // SSLv3 or TLS or GMSSLv1.0 or GMSSLv1.1
                packetLength = UnsignedShortBE(buffer, pos + 3) + SSL_RECORD_HEADER_LENGTH;
                if (packetLength <= SSL_RECORD_HEADER_LENGTH)
                {
                    // Neither SSLv3 or TLSv1 (i.e. SSLv2 or bad data)
                    tls = false;
                }
            }
            else
            {
                // Neither SSLv3 or TLSv1 (i.e. SSLv2 or bad data)
                tls = false;
            }
        }

        if (!tls)
        {
            // SSLv2 or bad data - Check the version
            int headerLength = (UnsignedByte(buffer[pos]) & 0x80) != 0 ? 2 : 3;
            int majorVersion = UnsignedByte(buffer[pos + headerLength + 1]);
            if (majorVersion == 2 || majorVersion == 3)
            {
                // SSLv2
                packetLength = headerLength == 2 ?
                        (ShortBE(buffer, pos) & 0x7FFF) + 2 : (ShortBE(buffer, pos) & 0x3FFF) + 3;
                if (packetLength <= headerLength)
                {
                    // If there's no data then consider this package as not encrypted.
                    return NOT_ENCRYPTED;
                }
            }
            else
            {
                return NOT_ENCRYPTED;
            }
        }

        return packetLength;
    }

    internal static void HandleHandshakeFailure(IChannelHandlerContext ctx, Exception cause, bool notify)
    {
        // We have may haven written some parts of data before an exception was thrown so ensure we always flush.
        // See https://github.com/netty/netty/issues/3900#issuecomment-172481830
        ctx.Flush();

        if (notify)
        {
            ctx.FireUserEventTriggered(new SslHandshakeCompletionEvent(cause));
        }

        _ = ctx.CloseAsync();
    }

    /**
     * Fills the {@link ByteBuf} with zero bytes.
     */
    internal static void Zeroout(ByteBuf buffer)
    {
        if (!buffer.IsReadOnly)
        {
            buffer.SetZero(0, buffer.Capacity);
        }
    }

    /**
     * Fills the {@link ByteBuf} with zero bytes and releases it.
     */
    internal static void ZerooutAndRelease(ByteBuf buffer)
    {
        Zeroout(buffer);
        buffer.Release();
    }

    /**
     * Same as {@link Base64#encode(ByteBuf, boolean)} but allows the use of a custom {@link ByteBufAllocator}.
     *
     * @see Base64#encode(ByteBuf, boolean)
     */
    internal static ByteBuf ToBase64(IByteBufAllocator allocator, ByteBuf src)
    {
        ByteBuf dst = Base64.Encode(src, src.ReaderIndex,
                src.ReadableBytes, true, Base64Dialect.STANDARD, allocator);
        src.ReaderIndex = src.WriterIndex;
        return dst;
    }

    /**
     * Validate that the given hostname can be used in SNI extension.
     */
    internal static bool IsValidHostNameForSNI(string hostname)
    {
        // See  https://datatracker.ietf.org/doc/html/rfc6066#section-3
        return hostname != null &&
                // SNI HostName has to be a FQDN according to TLS SNI Extension spec (see [1]),
                // which means that is has to have at least a host name and a domain part.
                hostname.IndexOf('.') > 0 &&
                !hostname.EndsWith('.') && !hostname.StartsWith('/') &&
                !NetUtil.IsValidIpV4Address(hostname) &&
                !NetUtil.IsValidIpV6Address(hostname);
    }

    /**
     * Returns {@code true} if the given cipher (in openssl format) is for TLSv1.3, {@code false} otherwise.
     */
    internal static bool IsTLSv13Cipher(string cipher)
    {
        // See https://tools.ietf.org/html/rfc8446#appendix-B.4
        return TLSV13_CIPHERS.Contains(cipher);
    }
}
